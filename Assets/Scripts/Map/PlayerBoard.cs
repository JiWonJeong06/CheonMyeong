using System.Collections;
using UnityEngine;
using Unity.Netcode;
using TowerDefense.Monsters;

namespace TowerDefense.Map
{
    /// <summary>
    /// 1:1 대전의 보드 하나(BoardA 또는 BoardB)를 나타냄 - 화면에 두 보드가 대칭으로 배치되고,
    /// 각 플레이어는 자기 보드에만 타워를 배치할 수 있는 구조(요청사항)를 구현하기 위한 컴포넌트.
    /// 각 맵 프리팹(5종) 안에 BoardA/BoardB 루트가 하나씩 있고, 그 밑에 PlacementGrid/MonsterSpawner/
    /// BaseHealth/MatchResourceManager가 세트로 딸려있는 구조를 가정함 - 이 컴포넌트가 그 넷을
    /// 한데 묶어서 참조하는 역할을 함.
    ///
    /// NetworkBehaviour가 아니라 그냥 MonoBehaviour임 - 보드 자체(그리드/스포너 등)는 양쪽 클라이언트
    /// 화면에 이미 똑같이 존재하는 씬 오브젝트라 네트워크로 스폰할 필요가 없고, "이 보드가 누구
    /// 소유인지"만 MatchController의 NetworkVariable로 동기화되면 충분함.
    /// </summary>
    public class PlayerBoard : MonoBehaviour
    {
        [Tooltip("0 = BoardA, 1 = BoardB - 반드시 인스펙터에서 보드마다 다르게 지정할 것")]
        [SerializeField] private int boardIndex;

        [SerializeField] private PlacementGrid grid;
        [SerializeField] private MonsterSpawner spawner;
        [SerializeField] private BaseHealth health;
        [SerializeField] private MatchResourceManager resources;

        public int BoardIndex => boardIndex;
        public PlacementGrid Grid => grid;
        public MonsterSpawner Spawner => spawner;
        public BaseHealth Health => health;
        public MatchResourceManager Resources => resources;

        /// <summary>이 보드가 로컬 플레이어(나) 소유인지 - UI/입력 쪽에서 "내 보드에만 배치 가능" 판단에 씀.</summary>
        public bool IsMine =>
            MatchController.Instance != null &&
            NetworkManager.Singleton != null &&
            MatchController.Instance.GetOwnerClientId(boardIndex) == NetworkManager.Singleton.LocalClientId;

        private void Start()
        {
            if (grid == null || spawner == null || health == null || resources == null)
            {
                Debug.LogWarning($"[PlayerBoard] Board {boardIndex}에 필요한 컴포넌트 참조가 인스펙터에서 안 채워짐.");
            }

            StartCoroutine(RegisterWhenReady());
        }

        // MatchController가 이 오브젝트보다 늦게 초기화될 수 있어서(씬 계층 순서에 의존하고 싶지
        // 않음) 등록에 성공할 때까지 한 프레임씩 재시도함 - 매치당 한 번만 일어나는 초기화라
        // 비용 걱정은 없음.
        private IEnumerator RegisterWhenReady()
        {
            while (MatchController.Instance == null)
            {
                yield return null;
            }
            MatchController.Instance.RegisterBoard(this);
        }
    }
}
