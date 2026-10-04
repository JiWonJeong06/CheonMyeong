namespace TowerDefense.UI
{
    // 모든 팝업(테크트리, 상점, 확인창 등)이 지켜야 하는 최소 계약.
    // 내부 구현(어떤 요소를 갖는지)은 몰라도 되고, PopupManager는 이 두 메서드만 호출한다.
    public interface IPopupPanel
    {
        void Show();
        void Hide();
    }
}
