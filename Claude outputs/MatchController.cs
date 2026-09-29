using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TowerDefense.Data;
using TowerDefense.Network;

namespace TowerDefense.Map
{
    /// <summary>
    /// 1:1 대전 한 판 전체를 서버 권위로 조율하는 컨트롤러. 씬에 딱 하나만 존재해야 하고(맵 5종
    /// 루트 바깥에, MapSelector와 같은 레벨에 둘 것 - 어느 맵이 선택되든 항상 존재해야 하니까),
    /// NetworkObject가 붙어서 인게임 씬 로드 시 자동 스폰되는 "in-scene placed NetworkObject"여야 함.
    ///
    /// 책임:
    /// 1) 맵 무작위 선택을 서버에서만 하고 NetworkVariable로 복제(양쪽이 같은 맵을 보게 함 - MapSelector가 구독)
    /// 2) 접속한 두 클라이언트를 BoardA/BoardB에 배정(NetworkVariable로 복제 - PlayerBoard.IsMine이 참조)
    /// 3) 두 보드 모두 준비되면 웨이브를 "동시에" 시작(요청사항: 양쪽에서 동시에 몬스터가 옴)
    /// 4) 타워 배치 요청을 서버에서 검증(SP/칸/보드 소유권) 후 NetworkObject로 스폰 - 클라이언트가
    ///    직접 스폰하면 위조 배치/무한 SP 사용 같은 치팅이 가능해지므로 반드시 서버를 거쳐야 함
    /// 5) 기지 파괴(패배) 감지 -> 양쪽에 결과 통보 -> RankManager/MatchmakingService 후처리 호출
    /// 6) 항복 요청 처리 - 기지 파괴와 동일한 패배 처리 경로(HandleBaseDestroyed)를 그대로 재사용함
    ///    (인게임 UI의 일시정지 메뉴 -> 항복 확인창에서 호출, Assets/UI/Popups/PauseMenu 참고)
    /// </summary>
    public class MatchController : NetworkBehaviour
    {
        public static MatchController Instance { get; private set; }

        [Tooltip("MapSelector의 mapRoots 배열 길이랑 반드시 같아야 함 - 안 맞으면 무효 인덱스가 뽑힐 수 있음")]
        [SerializeField] private int mapCount = 5;

        [Tooltip("타워 공용 비주얼 프리팹 - NetworkObject 컴포넌트가 붙어있어야 하고 NetworkManager의 Network Prefabs 목록에 등록돼 있어야 함")]
        [SerializeField] private TowerUnit genericTowerVisualPrefab;

        // 클라이언트 소유권 부재를 표시하는 값 - ulong엔 음수가 없어서 0 대신 MaxValue를 "미배정"으로 씀
        // (clientId 0은 보통 서버/호스트 자신이라 0을 미배정으로 쓰면 헷갈림).
        private const ulong Unassigned = ulong.MaxValue;

        public NetworkVariable<int> SelectedMapIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> BoardOwnerA = new(Unassigned, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> BoardOwnerB = new(Unassigned, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BoardHpA = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BoardHpB = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> BoardSpA = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> BoardSpB = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Dictionary<int, PlayerBoard> _boards = new();
        private Dictionary<string, CharacterDataSO> _characterLookup;
        private bool _matchStarted;
        private bool _matchEnded;

        /// <summary>매치가 끝났을 때(승패 결정) 발생 - bool은 "이 클라이언트 로컬 관점에서 내가 이겼는지".
        /// UI(결과 팝업)가 이걸 구독해서 화면을 띄움 - Map 레이어가 UI를 직접 참조하지 않도록
        /// 이벤트로만 알려주고, 실제로 뭘 보여줄지는 UI 쪽 책임으로 남겨둠(느슨한 결합).</summary>
        public event Action<bool> OnMatchEnded;

        private void Awake()
        {
            Instance = this;

            // CharacterJsonImporter가 캐릭터 SO를 항상 이 경로에 생성함(Assets/Editor/CharacterJsonImporter.cs
            // 참고) - 서버가 배치 요청을 검증할 때 characterId 문자열로 실제 데이터를 찾아야 해서 캐싱해둠.
            var all = Resources.LoadAll<CharacterDataSO>("CharacterData/Generated");
            _characterLookup = new Dictionary<string, CharacterDataSO>();
            foreach (var c in all)
            {
                if (c != null && !string.IsNullOrEmpty(c.characterId)) _characterLookup[c.characterId] = c;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            // using System(Action<bool> OnMatchEnded용으로 추가함) 때문에 System.Random이랑
            // UnityEngine.Random이 둘 다 시야에 들어와서 모호해짐 - 명시적으로 UnityEngine.Random을 지정함.
            SelectedMapIndex.Value = UnityEngine.Random.Range(0, mapCount);

            // 씬 로드 전에 이미 두 플레이어가 다 붙어있는 경우(매치메이킹 후 씬 전환이라 보통 이 경우임)를
            // 대비해서 기존 접속자를 먼저 확인하고, 그 이후 접속/이탈은 콜백으로 처리함.
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AssignBoardOwner(clientId);
            }
            NetworkManager.Singleton.OnClientConnectedCallback += AssignBoardOwner;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= AssignBoardOwner;
            }
        }

        private void AssignBoardOwner(ulong clientId)
        {
            if (BoardOwnerA.Value == clientId || BoardOwnerB.Value == clientId) return; // 이미 배정됨(재접속 등)

            if (BoardOwnerA.Value == Unassigned) BoardOwnerA.Value = clientId;
            else if (BoardOwnerB.Value == Unassigned) BoardOwnerB.Value = clientId;
            else
            {
                Debug.LogWarning($"[MatchController] 이미 두 보드 모두 배정된 상태에서 clientId={clientId}가 추가로 접속함 - 1:1 대전이라 무시함(관전 미구현).");
                return;
            }

            TryStartMatch();
        }

        /// <summary>PlayerBoard.Start()의 재시도 코루틴에서 호출됨. 서버/클라이언트 둘 다 자기 로컬 보드 참조를 등록해야 함(UI/입력용).</summary>
        public void RegisterBoard(PlayerBoard board)
        {
            _boards[board.BoardIndex] = board;
            if (IsServer) TryStartMatch();
        }

        public ulong GetOwnerClientId(int boardIndex) => boardIndex == 0 ? BoardOwnerA.Value : BoardOwnerB.Value;

        /// <summary>로컬 플레이어(나) 소유의 보드를 찾음 - TowerPlacementController가 "내 보드에만 배치" 판단에 씀. 아직 배정 전이면 null.</summary>
        public PlayerBoard GetLocalBoard()
        {
            foreach (var board in _boards.Values)
            {
                if (board.IsMine) return board;
            }
            return null;
        }

        private void TryStartMatch()
        {
            if (!IsServer || _matchStarted) return;
            if (BoardOwnerA.Value == Unassigned || BoardOwnerB.Value == Unassigned) return;
            if (!_boards.ContainsKey(0) || !_boards.ContainsKey(1)) return;

            _matchStarted = true;

            var boardA = _boards[0];
            var boardB = _boards[1];

            BoardHpA.Value = boardA.Health.MaxHp;
            BoardHpB.Value = boardB.Health.MaxHp;
            boardA.Health.OnHpChanged += (hp, _) => BoardHpA.Value = hp;
            boardB.Health.OnHpChanged += (hp, _) => BoardHpB.Value = hp;
            boardA.Health.OnBaseDestroyed += () => HandleBaseDestroyed(0);
            boardB.Health.OnBaseDestroyed += () => HandleBaseDestroyed(1);

            boardA.Resources.OnSPChanged += sp => BoardSpA.Value = sp;
            boardB.Resources.OnSPChanged += sp => BoardSpB.Value = sp;
            BoardSpA.Value = boardA.Resources.CurrentSP;
            BoardSpB.Value = boardB.Resources.CurrentSP;

            // 요청사항: "웨이브는 양쪽에서 동시에 옴" - 두 보드의 웨이브를 바로 이어서 시작함.
            boardA.Spawner.StartWaves();
            boardB.Spawner.StartWaves();

            Debug.Log("[MatchController] 매치 시작 - 두 보드 웨이브 동시 개시.");
        }

        private void HandleBaseDestroyed(int loserBoardIndex)
        {
            if (!IsServer || _matchEnded) return;
            _matchEnded = true;
            MatchEndedRpc(loserBoardIndex);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MatchEndedRpc(int loserBoardIndex)
        {
            // 각 클라이언트가 자기 보드가 진 쪽인지 스스로 판단함(서버가 누구한테 이겼다고 개별
            // 통보하는 것보다, 모두에게 "진 쪽 인덱스"만 broadcast하고 각자 로컬에서 판단하는 게
            // 더 단순함 - 어차피 BoardOwnerA/B는 이미 Everyone 권한으로 복제돼 있어서 다들 앎).
            PlayerBoard myBoard = null;
            foreach (var kv in _boards)
            {
                if (kv.Value.IsMine) { myBoard = kv.Value; break; }
            }
            if (myBoard == null) return;

            bool won = myBoard.BoardIndex != loserBoardIndex;
            RankManager.Instance?.ReportMatchResult(won);

            if (MatchmakingService.Instance != null)
            {
                _ = MatchmakingService.Instance.LeaveMatchAsync();
            }

            Debug.Log($"[MatchController] 매치 종료. 내 결과: {(won ? "승리" : "패배")}");
            OnMatchEnded?.Invoke(won); // 결과 화면 표시는 이 이벤트를 구독하는 UI 쪽 책임.
        }

        /// <summary>
        /// 클라이언트가 자기 보드에 타워를 놓고 싶을 때 호출(TowerPlacementController 참고).
        /// 서버가 소유권/SP/칸을 전부 검증한 뒤에만 실제로 스폰함.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestPlaceTowerRpc(int boardIndex, int cellX, int cellY, string characterId, RpcParams rpcParams = default)
        {
            if (!_boards.TryGetValue(boardIndex, out var board))
            {
                Debug.LogWarning($"[MatchController] 존재하지 않는 boardIndex={boardIndex} 배치 요청 - 무시함.");
                return;
            }

            ulong senderId = rpcParams.Receive.SenderClientId;
            if (GetOwnerClientId(boardIndex) != senderId)
            {
                // 내 보드가 아닌데 배치를 시도함 - 정상 클라이언트라면 절대 안 일어나야 하고, 일어났다면
                // 조작된 요청이라는 뜻이라 그냥 무시함(에러 메시지도 안 줌 - 공격자에게 힌트를 줄 필요 없음).
                Debug.LogWarning($"[MatchController] clientId={senderId}가 소유하지 않은 보드({boardIndex})에 배치를 시도함 - 거부.");
                return;
            }

            if (_characterLookup == null || !_characterLookup.TryGetValue(characterId, out var characterData))
            {
                Debug.LogWarning($"[MatchController] 알 수 없는 characterId={characterId}.");
                return;
            }

            if (genericTowerVisualPrefab == null)
            {
                Debug.LogError("[MatchController] genericTowerVisualPrefab이 인스펙터에 연결 안 됨.");
                return;
            }

            var cell = new Vector3Int(cellX, cellY, 0);
            if (!board.Grid.IsBuildable(cell))
            {
                return; // 칸이 막혀있음 - 클라이언트가 이미 로컬에서 IsBuildable을 먼저 확인하고 보내므로 정상 흐름에선 드묾
            }

            if (!board.Resources.TrySpendSP(characterData.summonCost))
            {
                return; // SP 부족
            }

            var instance = Instantiate(genericTowerVisualPrefab, board.Grid.GetCellCenterWorld(cell), Quaternion.identity);
            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[MatchController] genericTowerVisualPrefab에 NetworkObject 컴포넌트가 없음.");
                Destroy(instance.gameObject);
                board.Resources.AddSP(characterData.summonCost); // 이미 낸 SP 환불
                return;
            }

            networkObject.Spawn();
            board.Grid.MarkOccupied(cell, instance.gameObject);
            instance.Init(characterData);
        }

        /// <summary>
        /// 클라이언트가 항복을 요청할 때 호출(PauseMenuController - 항복 확인창의 "확인" 콜백 참고).
        /// 자기 보드가 파괴된 것과 동일한 패배 처리 경로(HandleBaseDestroyed)를 그대로 태우므로,
        /// 랭크 반영/매칭 세션 정리/결과 화면 표시가 기지 파괴 때와 완전히 똑같이 동작함.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestSurrenderRpc(RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;

            int surrenderingBoardIndex = -1;
            if (BoardOwnerA.Value == senderId) surrenderingBoardIndex = 0;
            else if (BoardOwnerB.Value == senderId) surrenderingBoardIndex = 1;

            if (surrenderingBoardIndex < 0)
            {
                // 보드가 아직 배정 안 됐거나(매칭 직후) 조작된 요청 - 정상 흐름에선 거의 안 일어남.
                Debug.LogWarning($"[MatchController] clientId={senderId}의 보드를 찾을 수 없어 항복 요청을 무시함.");
                return;
            }

            Debug.Log($"[MatchController] clientId={senderId}(보드 {surrenderingBoardIndex}) 항복 요청 - 패배 처리함.");
            HandleBaseDestroyed(surrenderingBoardIndex);
        }
    }
}
