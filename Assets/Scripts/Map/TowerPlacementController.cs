using System;
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
        public static TowerPlacementController Instance { get; private set; }

        [Tooltip("테스트용: 인스펙터에서 바로 지정해서 소환 테스트 가능. 실제로는 인게임 덱 UI가 SetSelectedCharacter()로 지정함")]
        [SerializeField] private CharacterDataSO selectedCharacter;

        private Camera _mainCamera;

        /// <summary>
        /// 실제로 배치 요청(RequestPlaceTowerRpc)을 서버로 보낸 시점에 발생함 - InGameDeckController가
        /// 덱 패널의 "SP 소모값" 표시(누적 카운터, 소환마다 +10)를 갱신하는 데 구독해서 씀.
        /// 서버 최종 승인 여부와 무관하게 "로컬에서 소환을 시도한 시점"에 낙관적으로 발생함
        /// (이 클래스 doc 주석에 적힌 것처럼, 로컬 사전 체크 자체가 순전히 UX용이라는 기존 설계와
        /// 동일한 결로 맞춘 것 - 서버가 거부하는 극히 드문 경쟁 상황까지 정확히 반영하려면
        /// MatchController 쪽에 배치 성공/실패를 클라이언트로 회신하는 Rpc가 따로 필요한데,
        /// 지금은 그 정도 정밀도가 필요하지 않다고 판단함).
        /// </summary>
        public event Action OnTowerPlaced;

        private void Awake()
        {
            // 씬에 하나만 있는 컴포넌트 - 다른 매니저들과 동일한 static Instance 패턴(InGameDeckController가
            // 카드 선택 시 이걸 통해 SetSelectedCharacter를 호출함).
            Instance = this;
        }

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

        /// <summary>현재 선택된 캐릭터. InGameDeckController가 카드 선택 상태를 되돌리거나
        /// 표시할 때 참조함(null이면 아무 카드도 선택 안 된 상태).</summary>
        public CharacterDataSO SelectedCharacter => selectedCharacter;

        // 인게임 덱 UI(InGameDeckController)가 카드를 클릭하면 이 메서드로 현재 선택된 캐릭터를 넘겨줌.
        public void SetSelectedCharacter(CharacterDataSO character) => selectedCharacter = character;

        private void TryPlaceAtPointer()
        {
            if (_mainCamera == null || selectedCharacter == null || MatchController.Instance == null) return;

            // HUD 버튼/덱 카드 위에서의 클릭(InGamePointerOverUI)이나 모달 팝업이 열려있는 동안의
            // 클릭(PopupManager.HasOpenPopups)은 보드 클릭으로 처리하면 안 됨 - UI Toolkit 런타임
            // 패널은 uGUI EventSystem을 안 거쳐서 이렇게 명시적으로 걸러줘야 함(TowerPlacementController
            // 클래스 doc, InGamePointerOverUI 참고).
            if (InGamePointerOverUI.IsPointerOverUI) return;
            if (PopupManager.Instance != null && PopupManager.Instance.HasOpenPopups) return;

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

            // [기획 확정] 캐릭터별 개별 SP 비용(summonCost)은 더 이상 안 씀 - 소환 비용은 항상
            // MatchResourceManager.SummonSpCost(고정 10)로 통일됨(MatchResourceManager.cs 클래스 doc 참고).
            if (myBoard.Resources != null && myBoard.Resources.CurrentSP < MatchResourceManager.SummonSpCost)
            {
                ToastController.Instance?.Show("SP가 부족합니다");
                return;
            }

            // 최종 SP 차감/타워 생성은 서버가 함 - 여기서는 요청만 보냄.
            MatchController.Instance.RequestPlaceTowerRpc(myBoard.BoardIndex, cell.x, cell.y, selectedCharacter.characterId);
            OnTowerPlaced?.Invoke();
        }
    }
}
