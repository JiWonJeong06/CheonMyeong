using System.Collections;
using UnityEngine;

namespace TowerDefense.Map
{
    /// <summary>
    /// 인게임 씬 진입 시 5개의 맵 중 하나를 활성화함. 맵마다 몬스터가 오는 길이 달라야 하므로
    /// (요청사항) 맵 5종을 각각 통짜 루트 오브젝트로 구성하고, 이 스크립트는 그중 하나만 켜는
    /// 역할만 함 - 맵별 내부 구성(길 모양, BoardA/BoardB 배치 등)은 레벨 디자인 영역이라 여기선
    /// 손대지 않음.
    ///
    /// [네트워킹 변경] 예전엔 이 스크립트가 직접 Random.Range로 맵을 골랐는데, 그러면 호스트랑
    /// 상대방이 각자 다른 맵을 무작위로 고르게 돼서 서로 다른 맵을 보게 되는 심각한 동기화
    /// 버그가 생김. 그래서 실제 무작위 선택은 서버 권위로 MatchController가 하고(NetworkVariable로
    /// 양쪽에 같은 인덱스를 복제함), 이 스크립트는 그 값을 받아서 "로컬 화면에 반영"만 함.
    ///
    /// [설정 필수] mapRoots에 연결하는 5개의 오브젝트는 반드시 씬에서 비활성화 상태로 배치돼
    /// 있어야 함 - 비활성 오브젝트는 Awake/OnEnable이 아예 호출되지 않으므로, SetActive(true)를
    /// 호출하는 시점에 비로소 선택된 맵의 PlayerBoard/PlacementGrid 등이 초기화됨.
    /// </summary>
    public class MapSelector : MonoBehaviour
    {
        [Tooltip("5개의 맵 루트 오브젝트 - 전부 씬에서 비활성화 상태로 미리 배치해둘 것")]
        [SerializeField] private GameObject[] mapRoots;

        public GameObject SelectedMap { get; private set; }
        public int SelectedIndex { get; private set; } = -1;

        private void Awake()
        {
            if (mapRoots == null || mapRoots.Length == 0)
            {
                Debug.LogWarning("[MapSelector] mapRoots가 비어있음 - 인스펙터에서 5개 맵을 연결할 것.");
                return;
            }

            // 혹시 실수로 에디터에서 켜둔 맵이 있어도 여기서 전부 확실히 꺼둠 - 서버가 정한 맵
            // 인덱스가 도착하기 전까지는 아무 맵도 초기화되면 안 됨(싱글턴류 중복 방지 포함).
            foreach (var root in mapRoots)
            {
                if (root != null) root.SetActive(false);
            }
        }

        private void Start()
        {
            StartCoroutine(WaitForMapIndexFromServer());
        }

        private IEnumerator WaitForMapIndexFromServer()
        {
            // MatchController.SelectedMapIndex 기본값은 -1(미정) - 서버가 값을 정해서 복제해줄
            // 때까지 대기함. MatchController 자체가 아직 씬에 없을 수도 있어서(초기화 순서 문제)
            // 그 경우도 같이 기다림.
            while (MatchController.Instance == null || MatchController.Instance.SelectedMapIndex.Value < 0)
            {
                yield return null;
            }

            ActivateMap(MatchController.Instance.SelectedMapIndex.Value);
        }

        private void ActivateMap(int index)
        {
            if (index < 0 || index >= mapRoots.Length || mapRoots[index] == null)
            {
                Debug.LogWarning($"[MapSelector] 서버가 알려준 맵 인덱스({index})가 유효하지 않음.");
                return;
            }

            SelectedIndex = index;
            SelectedMap = mapRoots[index];
            SelectedMap.SetActive(true);
        }
    }
}
