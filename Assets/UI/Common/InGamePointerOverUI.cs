using UnityEngine;

namespace TowerDefense.UI
{
    /// <summary>
    /// UI Toolkit 런타임 패널은 uGUI의 EventSystem을 거치지 않기 때문에, Mouse.current를 직접 읽는
    /// TowerPlacementController(Assets/Scripts/Map/TowerPlacementController.cs)는 HUD 버튼이나
    /// 인게임 덱 카드 위에서의 클릭도 "보드 위 월드 클릭"으로 그대로 처리해버림 - 그대로 두면 일시정지
    /// 버튼이나 카드 하나 눌렀을 뿐인데 그 화면 좌표에 대응하는 보드 칸에 타워 배치 요청까지 같이 나가는
    /// 버그가 생김. 인게임의 상시 노출 클릭 영역(HUD 일시정지 버튼, 덱 카드 등)이 여기에 자기 hover
    /// 상태를 등록해두면, TowerPlacementController가 클릭을 처리하기 전에 이걸로 "지금 포인터가 그
    /// UI 위에 있는지"를 확인해서 월드 클릭 처리를 건너뜀.
    ///
    /// 모달 팝업(일시정지 메뉴, 확인창 등)은 이걸로 처리 안 함 - 그건 화면 전체를 덮는 형태라 정확한
    /// 클릭 좌표를 따질 필요 없이 "팝업이 하나라도 열려있으면 무조건 차단"이 더 간단하고 정확함
    /// (PopupManager.HasOpenPopups 참고).
    /// </summary>
    public static class InGamePointerOverUI
    {
        private static int _hoverCount;

        public static bool IsPointerOverUI => _hoverCount > 0;

        // VisualElement.RegisterCallback&lt;PointerEnterEvent&gt;에서 호출할 것.
        public static void Enter() => _hoverCount++;

        // VisualElement.RegisterCallback&lt;PointerLeaveEvent&gt;에서 호출할 것 - OnDisable에서도
        // 반드시 호출해서, UI가 hover된 채로 비활성화되는 경우(예: 마우스 위에서 팝업이 닫힘) 카운트가
        // 영원히 쌓여있는 누수를 방지할 것.
        public static void Exit() => _hoverCount = Mathf.Max(0, _hoverCount - 1);

        // 씬 전환 시(InGame -> MainMenu 등) 이전 씬의 hover 등록이 해제 없이 남아있을 수 있으므로
        // 안전하게 리셋. MatchResultController.OnConfirmClicked처럼 씬을 나가는 지점에서 호출하면 됨.
        public static void ResetAll() => _hoverCount = 0;
    }
}
