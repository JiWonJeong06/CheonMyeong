using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDefense.UI
{
    // 더미 스텁 — 스킨/확정캐릭터/맵 구매 목록은 나중에 채움 (가챠 없음, 전부 확정 구매 방식으로 확정됨).
    [RequireComponent(typeof(UIDocument))]
    public class ShopController : MonoBehaviour, IPopupPanel
    {
        private UIDocument _document;
        private Button _closeButton;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;
            _closeButton = root.Q<Button>("close-button");
            _closeButton.clicked += OnCloseClicked;
        }

        private void OnDisable()
        {
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
        }

        public void Open() => PopupManager.Instance.Open(this);

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        private void OnCloseClicked() => PopupManager.Instance.RequestClose(this);
    }
}
