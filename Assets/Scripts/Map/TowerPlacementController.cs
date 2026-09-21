using UnityEngine;
using UnityEngine.InputSystem;
using TowerDefense.Data;
using TowerDefense.UI;

namespace TowerDefense.Map
{
    /// <summary>
    /// 클릭(포인터)으로 캐릭터를 "소환"해서 내 보드에 타워로 배치하는 입력 처리. 지금은
    /// InputActions 에셋(InputSystem_Actions)을 안 쓰고 Mouse.current를 직접 읽는 최소 구현임 -
    /// 리바인딩(키 재설정) UI가 필요해지면 그때 액션 에셋 기반으로 바꾸면 됨. 현재
    /// com.unity.inputsystem 1.19.0 기준 API.
    ///
    /// [네트워킹] 예전엔 이 스크립트가 직접 PlacementGrid.TryPlaceTower + EconomyManager를 불러서
    /// 그 자리에서 타워를 만들었는데, 1:1 대전이 되면서 그러면 안 됨 - 클라이언트가 직접 타워를
    /// 만들면 SP 조작이나 상대 보드에 배치하는 것 같은 치팅이 가능해짐. 그래서 이제 이 스크립트는
    /// "칸이 비어있는지" 정도만 로컬에서 미리 확인해 즉각적인 UX 피드백(Toast)을 주고, 실제 배치는
    /// MatchController.RequestPlaceTowerRpc로 요청만 보냄 - 최종 검증/생성은 전부 서버가 함.
    /// 이 로컬 사전 체크는 순전히 UX용이라, 서버 쪽 최종 판정과 어긋나도(예: 그 사이 상대가 같은
    /// 칸을 먼저 차지) 게임이 깨지지 않음(서버가 그냥 조용히 거부함).
    /// </summary>
    public class TowerPlacementController : MonoBehaviour
    {
        [Tooltip("테스트용: 인스펙터에서 바로 지정해서 소환 테스트 가능. 실제로는 편성 UI가 SetSelectedCharacter()로 지정함")]
        [SerializeField] private CharacterDataSO selectedCharacter;

        private Camera _mainCamera;

        private void Start()
        {
            // Camera.main은 내부적으로 태그 검색이라 매 프레임 부르면 비용이 있음 - 한 번만 캐싱해서 씀.
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Debug.LogWarning("[TowerPlacement] MainCamera 태그가 붙은 카메라를 못 찾음.");
            }
        }

        private void Update()
        {
            if (Mouse.current == null) return;
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            TryPlaceAtPointer();
        }

        // 나중에 편성 UI가 이 메서드로 현재 선택된 캐릭터를 넘겨주면 됨.
        public void SetSelectedCharacter(CharacterDataSO character) => selectedCharacter = character;

        private void TryPlaceAtPointer()
        {
            if (_mainCamera == null || selectedCharacter == null || MatchController.Instance == null) return;

            var myBoard = MatchController.Instance.GetLocalBoard();
            if (myBoard == null || myBoard.Grid == null) return; // 아직 배정/초기화 전

            Vector2 screenPos = Mouse.current.position.ReadValue();
            // ScreenToWorldPoint의 세 번째 인자는 "카메라로부터의 거리"임 - 여기에 0을 넘기면
            // 카메라 바로 앞(원점)으로 좌표가 뭉개지는 흔한 실수라, 카메라~z=0 평면까지의 거리를
            // 명시적으로 넘겨서 정확한 지점을 계산함(오소그래픽/퍼스펙티브 카메라 모두 안전).
            float distanceToZeroPlane = -_mainCamera.transform.position.z;
            Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, distanceToZeroPlane));
            worldPos.z = 0f; // 2D 프로젝트라 최종적으로 z=0 평면에 명시적으로 고정

            var cell = myBoard.Grid.WorldToCell(worldPos);
            if (!myBoard.Grid.IsBuildable(cell))
            {
                ToastController.Instance?.Show("여기엔 지을 수 없습니다");
                return;
            }

            if (myBoard.Resources != null && myBoard.Resources.CurrentSP < selectedCharacter.summonCost)
            {
                ToastController.Instance?.Show("SP가 부족합니다");
                return;
            }

            // 최종 SP 차감/타워 생성은 서버가 함 - 여기서는 요청만 보냄.
            MatchController.Instance.RequestPlaceTowerRpc(myBoard.BoardIndex, cell.x, cell.y, selectedCharacter.characterId);
        }
    }
}
