using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TowerDefense.Data;
using TowerDefense.Network;
using TowerDefense.Monsters;

namespace TowerDefense.Map
{
    /// <summary>매치 결과(로컬 클라이언트 관점).</summary>
    public enum MatchOutcome { Win, Lose, Draw }

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
    /// 7) 웨이브별 보스 랜덤 선택(서버 권위, 양쪽 동일 보스) - MonsterSpawner.RunBossPhase가
    ///    80초 타이머 종료 시점마다 RequestBossForWave(wave)를 호출함. 이 랜덤은 반드시 서버에서
    ///    "웨이브당 딱 한 번만" 굴려야 함(안 그러면 BoardA/BoardB가 각자 다른 보스를 뽑아버림) -
    ///    그래서 굴린 결과를 WaveDataSO 에셋 레퍼런스 기준으로 캐싱해서, 같은 웨이브에 대해 먼저
    ///    요청한 쪽이 굴리고 나중에 요청한 쪽(상대 보드)은 캐싱된 값을 그대로 받아감.
    /// 8) 계절/날씨 시스템(천명.pptx 슬라이드 12/13) - 계절은 매치 시작 시 서버가 1회만 굴려
    ///    SelectedSeason에 저장하고 한 판 내내 유지함. 날씨는 웨이브마다 RequestWeatherForWave가
    ///    보스 선택과 동일한 WaveDataSO 레퍼런스 캐싱 방식으로 "양쪽 동일 날씨"를 보장하고,
    ///    CurrentWeather NetworkVariable로 브로드캐스트함(TowerUnit/InGameHUDController가 구독).
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

        // MatchEndedRpc의 loserBoardIndex에 이 값이 오면 무승부(진 쪽 없음).
        private const int DrawBoardIndex = -1;

        public NetworkVariable<int> SelectedMapIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> BoardOwnerA = new(Unassigned, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> BoardOwnerB = new(Unassigned, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BoardHpA = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BoardHpB = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> BoardSpA = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> BoardSpB = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 다음 소환 가격(100 + 10 × 소환 횟수) - 클라이언트 UI(카드 흐림/SP 소모 표시)와 사전 체크용.
        public NetworkVariable<int> BoardSummonCostA = new(MatchResourceManager.SummonBaseCost, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BoardSummonCostB = new(MatchResourceManager.SummonBaseCost, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 계절/날씨 시스템(천명.pptx 슬라이드 12/13) - 계절은 매치 시작 시 1회, 날씨는 웨이브마다
        // 서버가 굴려서 여기 NetworkVariable로 브로드캐스트함(양쪽 클라이언트/HUD가 그대로 구독).
        // int로 저장하는 이유는 NetworkVariable<enum>이 Netcode 2.13.2 기준 직렬화에 커스텀 struct
        // wrapper가 필요해서 굳이 복잡하게 안 만들고 SelectedMapIndex(int)와 같은 방식을 그대로 따름.
        public NetworkVariable<int> SelectedSeason = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CurrentWeather = new((int)WeatherType.Clear, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // clientId별 로드아웃(캐릭터 레벨/노드 해금) - 서버만 채우고 씀. 1:1 매치 한 판 단위라 OnNetworkDespawn에서 비움.
        private readonly Dictionary<ulong, PlayerLoadout> _loadouts = new();

        // [5단계] SP 강화 레벨 - (보드, 캐릭터 종류)별 1트랙. 필드에 캐릭터가 없어도 강화 가능(엑셀 구현 규칙).
        // 서버가 값을 바꾸고 EnhanceLevelChangedRpc로 양쪽에 알려서 모든 피어가 같은 값을 가짐(UI 표시/사전 체크용).
        private readonly Dictionary<(int boardIndex, string characterId), int> _enhanceLevels = new();

        /// <summary>SP 강화 레벨이 바뀐 순간(모든 피어) - 강화 패널이 구독. (boardIndex, characterId, 새 레벨)</summary>
        public event Action<int, string, int> OnEnhanceLevelChanged;

        /// <summary>[서버 전용] 합성이 성사된 직후, 소멸 타워가 파괴되기 전에 발생. (boardIndex, 남는 타워, 사라질 타워)
        /// 조작(조진호) 해제 같은 "합성 시 처리" 스킬 로직이 여기에 붙으면 됨.</summary>
        public event Action<int, TowerUnit, TowerUnit> OnTowerMerged;

        private readonly Dictionary<int, PlayerBoard> _boards = new();
        private Dictionary<string, CharacterDataSO> _characterLookup;
        private bool _matchStarted;
        private bool _matchEnded;
        private int _boardsFinishedWaves; // 마지막 웨이브(보스전 포함)까지 끝낸 보드 수 - 2가 되면 하트 비교로 승패 판정

        // 웨이브당 보스 랜덤 선택 캐시 - WaveDataSO 에셋 레퍼런스를 키로 씀(BoardA/BoardB 스포너가
        // 인스펙터에서 같은 WaveDataSO 에셋 리스트를 공유하는 구성을 전제함 - RequestBossForWave 참고).
        private readonly Dictionary<WaveDataSO, MonsterDataSO> _bossPickForWave = new();

        // 웨이브당 날씨 랜덤 선택 캐시 - 보스 선택과 완전히 같은 이유·같은 구조로 WaveDataSO 에셋
        // 레퍼런스를 키로 씀(RequestWeatherForWave 참고).
        private readonly Dictionary<WaveDataSO, WeatherType> _weatherPickForWave = new();

        /// <summary>낙엽 날씨가 뜬 순간(웨이브당 1회) 발생 - 순수 시각 연출용(시야 가림, 지속시간 초 단위).
        /// InGameHUDController가 구독해서 화면 오버레이를 띄움(게임플레이 로직에는 영향 없음).</summary>
        public event Action<float> OnVisionBlockStarted;

        /// <summary>매치가 끝났을 때(승패/무승부 결정) 발생 - MatchOutcome은 "이 클라이언트 로컬 관점의 결과".
        /// UI(결과 팝업)가 이걸 구독해서 화면을 띄움 - Map 레이어가 UI를 직접 참조하지 않도록
        /// 이벤트로만 알려주고, 실제로 뭘 보여줄지는 UI 쪽 책임으로 남겨둠(느슨한 결합).</summary>
        public event Action<MatchOutcome> OnMatchEnded;

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
            // 캐릭터 레벨/노드 해금 스냅샷을 서버에 제출(호스트는 서버 겸 클라이언트라 자기 자신에게도 보냄) -
            // 레벨 차이는 PvP에 그대로 반영됨(엑셀 구현 규칙). SubmitLoadoutRpc 참고.
            if (IsClient) SubmitLoadoutRpc(PlayerLoadout.CaptureLocal().Pack());

            if (!IsServer) return;

            // using System(Action<bool> OnMatchEnded용으로 추가함) 때문에 System.Random이랑
            // UnityEngine.Random이 둘 다 시야에 들어와서 모호해짐 - 명시적으로 UnityEngine.Random을 지정함.
            SelectedMapIndex.Value = UnityEngine.Random.Range(0, mapCount);

            // 계절도 맵과 동일하게 매치 시작 시 서버가 1회만 굴리고 한 판 내내 유지함(슬라이드 12:
            // "계절 1개가 랜덤으로 정해지고, 한 판 내내 유지됩니다").
            SelectedSeason.Value = UnityEngine.Random.Range(0, System.Enum.GetValues(typeof(SeasonType)).Length);

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
            _loadouts.Clear();
            _enhanceLevels.Clear();
        }

        /// <summary>클라이언트가 매치 시작 때 보내는 로드아웃 스냅샷(PlayerLoadout.Pack 문자열). 서버가 보낸 사람 id로 저장함.</summary>
        [Rpc(SendTo.Server)]
        private void SubmitLoadoutRpc(string packedLoadout, RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;
            _loadouts[senderId] = PlayerLoadout.Unpack(packedLoadout);
        }

        // 배치하는 플레이어의 레벨/노드 효과로 이 타워의 배율을 계산함. 로드아웃을 못 받았거나 매니저가 없으면
        // 레벨 1 + 노드 효과 없음으로 안전하게 동작함. 노드 수치는 [미정]이라 지금은 보너스 합이 0.
        private TowerStatModifiers ComputeTowerModifiers(ulong ownerClientId, CharacterDataSO character)
        {
            if (!_loadouts.TryGetValue(ownerClientId, out var loadout)) return TowerStatModifiers.Identity;

            // 화면(노드 상세창)과 같은 계산기를 써서 표시 숫자와 실제 전투 숫자가 어긋나지 않게 함.
            return NodeBonusCalculator.Compute(character, loadout.GetLevel(character.characterId), loadout.unlockedNodeIds);
        }

        private void AssignBoardOwner(ulong clientId)
        {
            // [임시 진단 로그 - 2026-09-30] 매치 시작 실패 원인 추적용. 문제 해결되면 제거할 것.
            Debug.Log($"[MatchController][진단] AssignBoardOwner 호출됨. clientId={clientId}, 현재 BoardOwnerA={BoardOwnerA.Value}, BoardOwnerB={BoardOwnerB.Value}");

            if (BoardOwnerA.Value == clientId || BoardOwnerB.Value == clientId)
            {
                Debug.Log($"[MatchController][진단] clientId={clientId}는 이미 배정돼 있어서 무시함(재접속 등).");
                return; // 이미 배정됨(재접속 등)
            }

            if (BoardOwnerA.Value == Unassigned) BoardOwnerA.Value = clientId;
            else if (BoardOwnerB.Value == Unassigned) BoardOwnerB.Value = clientId;
            else
            {
                Debug.LogWarning($"[MatchController] 이미 두 보드 모두 배정된 상태에서 clientId={clientId}가 추가로 접속함 - 1:1 대전이라 무시함(관전 미구현).");
                return;
            }

            Debug.Log($"[MatchController][진단] 배정 완료. BoardOwnerA={BoardOwnerA.Value}, BoardOwnerB={BoardOwnerB.Value} → TryStartMatch 호출.");
            TryStartMatch();
        }

        /// <summary>PlayerBoard.Start()의 재시도 코루틴에서 호출됨. 서버/클라이언트 둘 다 자기 로컬 보드 참조를 등록해야 함(UI/입력용).</summary>
        public void RegisterBoard(PlayerBoard board)
        {
            _boards[board.BoardIndex] = board;

            // [임시 진단 로그 - 2026-09-30] 매치 시작 실패 원인 추적용. 문제 해결되면 제거할 것.
            Debug.Log($"[MatchController][진단] RegisterBoard 호출됨. boardIndex={board.BoardIndex}, IsServer={IsServer}, 현재 등록된 보드 개수={_boards.Count}(키: {string.Join(",", _boards.Keys)})");

            if (IsServer) TryStartMatch();
        }

        /// <summary>복제된 SP/소환 가격 조회(클라이언트도 정확) - 로컬 사전 체크/UI용.</summary>
        public float GetBoardSp(int boardIndex) => boardIndex == 0 ? BoardSpA.Value : BoardSpB.Value;
        public int GetSummonCost(int boardIndex) => boardIndex == 0 ? BoardSummonCostA.Value : BoardSummonCostB.Value;

        public ulong GetOwnerClientId(int boardIndex) => boardIndex == 0 ? BoardOwnerA.Value : BoardOwnerB.Value;

        /// <summary>
        /// MonsterSpawner.RunBossPhase(80초 타이머 종료 시점)가 호출함 - 이 웨이브의 보스를
        /// bossPool 중 하나로 서버 권위 랜덤 선택함. 같은 WaveDataSO에 대해 두 번째로 호출되면
        /// (상대 보드 스포너가 뒤이어 요청) 처음 굴린 값을 그대로 반환해서 "양쪽 동일 보스"를
        /// 보장함 - 클라이언트에서 호출되거나(IsServer 아님) bossPool이 비어있으면 null 반환.
        /// </summary>
        public MonsterDataSO RequestBossForWave(WaveDataSO wave)
        {
            if (!IsServer) return null;
            if (wave == null || wave.bossPool == null || wave.bossPool.Count == 0) return null;

            if (_bossPickForWave.TryGetValue(wave, out var cached)) return cached;

            var pick = wave.bossPool[UnityEngine.Random.Range(0, wave.bossPool.Count)];
            _bossPickForWave[wave] = pick;
            return pick;
        }

        /// <summary>
        /// MonsterSpawner.RunWaves()가 웨이브를 시작할 때마다 호출함 - 이 웨이브의 날씨를
        /// SelectedSeason 기준 후보(맑음 + 계절 전용 2종 + 공용 2종) 중 서버 권위 랜덤으로 선택함.
        /// 보스 선택(RequestBossForWave)과 완전히 같은 이유·같은 구조로 WaveDataSO 에셋 레퍼런스
        /// 기준 캐싱을 써서 "양쪽 동일 날씨"를 보장함. 클라이언트에서 호출되면(IsServer 아님)
        /// WeatherType.Clear를 반환함(안전한 기본값 - 효과 없음).
        /// </summary>
        public WeatherType RequestWeatherForWave(WaveDataSO wave)
        {
            if (!IsServer) return WeatherType.Clear;
            if (wave == null) return WeatherType.Clear;

            if (_weatherPickForWave.TryGetValue(wave, out var cached)) return cached;

            var season = (SeasonType)SelectedSeason.Value;
            var candidates = WeatherEffectTable.GetCandidates(season);
            var pick = candidates[UnityEngine.Random.Range(0, candidates.Length)];

            _weatherPickForWave[wave] = pick;
            CurrentWeather.Value = (int)pick; // HUD/TowerUnit이 구독하는 전역값 갱신

            if (WeatherEffectTable.HasVisionBlockEffect(pick, out float duration))
            {
                VisionBlockStartedRpc(duration);
            }

            return pick;
        }

        /// <summary>낙엽 날씨 시작을 양쪽 클라이언트에 브로드캐스트함(순수 시각 연출 트리거 - 게임플레이
        /// 로직과 무관, OnVisionBlockStarted 구독자(HUD)가 화면 오버레이를 띄움).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void VisionBlockStartedRpc(float durationSeconds)
        {
            OnVisionBlockStarted?.Invoke(durationSeconds);
        }

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
            // [임시 진단 로그 - 2026-09-30] 매치 시작 실패 원인 추적용. 문제 해결되면 제거할 것.
            if (!IsServer)
            {
                Debug.Log("[MatchController][진단] TryStartMatch: 서버가 아니라서 리턴함.");
                return;
            }
            if (_matchStarted)
            {
                Debug.Log("[MatchController][진단] TryStartMatch: 이미 _matchStarted=true라서 리턴함.");
                return;
            }
            if (BoardOwnerA.Value == Unassigned || BoardOwnerB.Value == Unassigned)
            {
                Debug.Log($"[MatchController][진단] TryStartMatch: BoardOwner 미배정이라 리턴함. BoardOwnerA={BoardOwnerA.Value}, BoardOwnerB={BoardOwnerB.Value} (Unassigned={Unassigned})");
                return;
            }
            if (!_boards.ContainsKey(0) || !_boards.ContainsKey(1))
            {
                Debug.Log($"[MatchController][진단] TryStartMatch: 보드 등록 미완료라 리턴함. 등록된 키: {string.Join(",", _boards.Keys)}");
                return;
            }

            Debug.Log("[MatchController][진단] TryStartMatch: 모든 조건 통과 - 매치 시작 진행.");
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
            boardA.Resources.OnSummonCostChanged += c => BoardSummonCostA.Value = c;
            boardB.Resources.OnSummonCostChanged += c => BoardSummonCostB.Value = c;
            BoardSummonCostA.Value = boardA.Resources.NextSummonCost;
            BoardSummonCostB.Value = boardB.Resources.NextSummonCost;

            // 몬스터 처치 SP는 그 몬스터가 속한 보드(스포너)의 지갑으로 들어감.
            boardA.Spawner.OnSpRewardEvent += sp => boardA.Resources.AddSP(sp);
            boardB.Spawner.OnSpRewardEvent += sp => boardB.Resources.AddSP(sp);

            // 몬스터 전송("넘어온 몬스터") - 한쪽에서 죽은 몬스터가 상대 보드로 넘어가려면 두
            // 스포너가 서로를 알아야 함. 인스펙터에서 미리 연결해두는 대신 여기서(양쪽 보드가
            // 모두 확정된 시점) 서버가 직접 배선함 - PlayerBoard/MonsterSpawner 에셋을 매치마다
            // 미리 짝지어둘 필요가 없어져서 맵이 바뀌어도(SelectedMapIndex) 그대로 동작함.
            boardA.Spawner.SetOpponentSpawner(boardB.Spawner);
            boardB.Spawner.SetOpponentSpawner(boardA.Spawner);
            boardA.Spawner.SetCellSize(boardA.Grid.CellSize.x); // 몬스터 이동속도(칸/초) 환산용
            boardB.Spawner.SetCellSize(boardB.Grid.CellSize.x);

            // 20웨이브(보스전 포함)까지 양쪽 다 끝나면 하트 비교로 승패 판정(많은 쪽 승리, 같으면 무승부).
            boardA.Spawner.OnAllWavesSpawned += HandleBoardWavesFinished;
            boardB.Spawner.OnAllWavesSpawned += HandleBoardWavesFinished;

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

        // MonsterSpawner.OnAllWavesSpawned는 마지막 웨이브의 보스전(보스 처치 또는 기지 도달)이 끝난 뒤
        // 보드당 1회 발생함 - 늦게 끝나는 쪽 보스가 아직 하트를 깎을 수 있어서 양쪽 다 끝나길 기다림.
        private void HandleBoardWavesFinished()
        {
            if (!IsServer || _matchEnded) return;
            _boardsFinishedWaves++;
            if (_boardsFinishedWaves < 2) return;

            int hpA = _boards[0].Health.CurrentHp;
            int hpB = _boards[1].Health.CurrentHp;

            // 하트 많은 쪽 승리, 같으면 무승부(loserBoardIndex = DrawBoardIndex).
            int loser = hpA > hpB ? 1 : (hpB > hpA ? 0 : DrawBoardIndex);
            _matchEnded = true;
            Debug.Log($"[MatchController] 전 웨이브 종료 - 하트 A={hpA}, B={hpB} → {(loser == DrawBoardIndex ? "무승부" : $"보드 {loser} 패배")}");
            MatchEndedRpc(loser);
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

            MatchOutcome outcome;
            if (loserBoardIndex == DrawBoardIndex) outcome = MatchOutcome.Draw;
            else outcome = myBoard.BoardIndex != loserBoardIndex ? MatchOutcome.Win : MatchOutcome.Lose;

            if (outcome == MatchOutcome.Draw) RankManager.Instance?.ReportMatchDraw();
            else RankManager.Instance?.ReportMatchResult(outcome == MatchOutcome.Win);

            if (MatchmakingService.Instance != null)
            {
                _ = MatchmakingService.Instance.LeaveMatchAsync();
            }

            Debug.Log($"[MatchController] 매치 종료. 내 결과: {outcome}");
            OnMatchEnded?.Invoke(outcome); // 결과 화면 표시는 이 이벤트를 구독하는 UI 쪽 책임.
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

            // 소환 가격은 보드별 누적 소환 횟수 기반(100 + 10n) - MatchResourceManager 참고.
            float summonCost = board.Resources.NextSummonCost;
            if (!board.Resources.TrySpendSP(summonCost))
            {
                return; // SP 부족
            }

            var instance = Instantiate(genericTowerVisualPrefab, board.Grid.GetCellCenterWorld(cell), Quaternion.identity);
            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[MatchController] genericTowerVisualPrefab에 NetworkObject 컴포넌트가 없음.");
                Destroy(instance.gameObject);
                board.Resources.AddSP(summonCost); // 이미 낸 SP 환불(소환 횟수는 안 올림)
                return;
            }

            // [버그 수정 - 2026-09-29] 배치된 타워 비주얼이 타일 한 칸보다 크게 보이는 문제 - 스폰 직후
            // Spawn() 전에 스케일을 그리드 칸 크기에 맞춤. Spawn() 전에 바꾸는 이유: NetworkObject의
            // SynchronizeTransform이 "스폰 시점의 초기 transform(스케일 포함)"을 스폰 페이로드에 그대로
            // 담아서 클라이언트에 보내주므로, 여기서 서버가 미리 스케일을 맞춰두면 별도 NetworkTransform
            // 없이도 양쪽 클라이언트에 동일하게 반영됨. 칸 크기(PlacementGrid.CellSize) 대비 스프라이트
            // 원본 크기의 비율로 계산해서 나중에 실제 아트(다른 PPU/픽셀 크기)로 교체돼도 코드 수정 없이
            // 그대로 맞게 동작함(하드코딩된 배율 숫자를 안 씀).
            FitVisualToCell(instance.transform, board.Grid.CellSize);

            networkObject.Spawn();
            board.Resources.CommitSummon(); // 소환 성공 확정 - 다음 소환 가격 +10
            board.Grid.MarkOccupied(cell, instance.gameObject);
            // [버그 수정 - 2026-09-29] TowerUnit이 소속 보드(board.Spawner)를 알아야 상대 보드
            // 몬스터를 잘못 타겟팅하지 않음 - TowerUnit.cs의 _ownBoardSpawner 필드 주석 참고.
            instance.Init(characterData, board.Spawner, board.Grid.CellSize.x, ComputeTowerModifiers(senderId, characterData));
            // 이미 올려둔 SP 강화 레벨을 새로 소환한 캐릭터에도 적용(같은 캐릭터 전부 적용 - 엑셀 구현 규칙).
            instance.ApplyProgression(1, GetEnhanceLevel(boardIndex, characterId));
        }

        /// <summary>이 보드에서 해당 캐릭터의 SP 강화 레벨(Lv1~5, 강화 전엔 1). 모든 피어에서 동일하게 조회됨.</summary>
        public int GetEnhanceLevel(int boardIndex, string characterId)
        {
            return _enhanceLevels.TryGetValue((boardIndex, characterId), out int level) ? level : 1;
        }

        /// <summary>
        /// [합성] 같은 캐릭터 + 같은 별인 내 타워 두 개를 합쳐 별 +1(최대 5★). (toX,toY)의 타워가 남아 별이 오르고
        /// (fromX,fromY)의 타워는 사라지며 그 칸이 비워짐. 소유권/칸/조건 검증은 전부 서버가 함(조건 불일치는 조용히 무시).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestMergeRpc(int boardIndex, int fromX, int fromY, int toX, int toY, RpcParams rpcParams = default)
        {
            if (!_boards.TryGetValue(boardIndex, out var board)) return;
            if (GetOwnerClientId(boardIndex) != rpcParams.Receive.SenderClientId) return;
            if (fromX == toX && fromY == toY) return;

            var fromCell = new Vector3Int(fromX, fromY, 0);
            var from = TowerUnit.FindAtCell(board.Grid, fromCell);
            var to = TowerUnit.FindAtCell(board.Grid, new Vector3Int(toX, toY, 0));
            if (from == null || to == null || from == to) return;
            if (from.Data == null || to.Data == null) return;
            if (from.Data.characterId != to.Data.characterId) return;
            if (from.Stars != to.Stars || to.Stars >= TowerProgression.MaxStars) return;

            to.ApplyProgression(to.Stars + 1, GetEnhanceLevel(boardIndex, to.Data.characterId));
            OnTowerMerged?.Invoke(boardIndex, to, from);

            board.Grid.ReleaseCell(fromCell); // 슬롯 비우기
            var fromObject = from.NetworkObject;
            if (fromObject != null && fromObject.IsSpawned) fromObject.Despawn(true);
        }

        /// <summary>
        /// [SP 강화] 캐릭터 종류 하나의 강화 레벨을 1 올림(Lv5까지, 구간 가격 500/1000/2500/5000 - 소속 가격 감소 노드 반영).
        /// 필드에 그 캐릭터가 없어도 가능하고, 이미 필드에 있는 같은 캐릭터 전부에 즉시 적용됨. SP는 서버 지갑에서 차감.
        /// 강화 패널(UI)은 이 RPC를 호출하고 OnEnhanceLevelChanged를 구독하면 됨.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestEnhanceRpc(int boardIndex, string characterId, RpcParams rpcParams = default)
        {
            if (!_boards.TryGetValue(boardIndex, out var board)) return;
            ulong senderId = rpcParams.Receive.SenderClientId;
            if (GetOwnerClientId(boardIndex) != senderId) return;
            if (_characterLookup == null || !_characterLookup.TryGetValue(characterId, out var characterData)) return;

            int level = GetEnhanceLevel(boardIndex, characterId);
            int baseCost = TowerProgression.GetEnhanceCost(level);
            if (baseCost <= 0) return; // 이미 최대 레벨

            float discountPercent = ComputeEnhanceDiscountPercent(senderId, characterData);
            int cost = Mathf.Max(0, Mathf.RoundToInt(baseCost * (1f - discountPercent / 100f)));
            if (cost > 0 && !board.Resources.TrySpendSP(cost)) return; // SP 부족

            level++;
            _enhanceLevels[(boardIndex, characterId)] = level;

            var towers = TowerUnit.All;
            for (int i = 0; i < towers.Count; i++)
            {
                var t = towers[i];
                if (t == null || t.Data == null) continue;
                if (t.OwnBoardSpawner != board.Spawner || t.Data.characterId != characterId) continue;
                t.ApplyProgression(t.Stars, level);
            }

            EnhanceLevelChangedRpc(boardIndex, characterId, level);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EnhanceLevelChangedRpc(int boardIndex, string characterId, int level)
        {
            _enhanceLevels[(boardIndex, characterId)] = level;
            OnEnhanceLevelChanged?.Invoke(boardIndex, characterId, level);
        }

        // 소속 라인 노드의 "SP 강화 가격 -n%" 합(무소속은 소속 효과를 받지 않음). 수치 [미정]이라 지금은 0.
        private float ComputeEnhanceDiscountPercent(ulong ownerClientId, CharacterDataSO character)
        {
            var tree = TechTreeManager.Instance;
            if (tree == null || !NodeTreeLines.ReceivesLineEffect(character.code)) return 0f;
            if (!_loadouts.TryGetValue(ownerClientId, out var loadout)) return 0f;

            string line = NodeTreeLines.GetLine(character.code);
            float percent = tree.GetBonusPercent(NodeEffectType.LineEnhancePriceDiscountPercent, line, loadout.unlockedNodeIds);
            return Mathf.Clamp(percent, 0f, 100f);
        }

        // 스프라이트의 "스케일 1일 때 월드 크기"(SpriteRenderer.sprite.bounds.size) 대비 그리드 한 칸
        // 크기의 비율로 균일 스케일을 구함. 가로/세로 중 더 작은 쪽 기준으로 잡아서(Mathf.Min), 정사각형이
        // 아닌 스프라이트가 와도 칸 밖으로 삐져나오지 않고 항상 칸 안에 딱 맞게 들어감.
        private static void FitVisualToCell(Transform towerTransform, Vector3 cellSize)
        {
            var spriteRenderer = towerTransform.GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null || spriteRenderer.sprite == null) return;

            Vector2 spriteWorldSize = spriteRenderer.sprite.bounds.size;
            if (spriteWorldSize.x <= 0f || spriteWorldSize.y <= 0f) return;

            float targetCellSize = Mathf.Min(cellSize.x, cellSize.y);
            float scale = Mathf.Min(targetCellSize / spriteWorldSize.x, targetCellSize / spriteWorldSize.y);
            towerTransform.localScale = new Vector3(scale, scale, 1f);
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
