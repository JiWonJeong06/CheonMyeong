using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDefense.UI
{
    /// <summary>
    /// 상점 구매, 테크노드 해금 등 "재화 소모 전 확인"이 필요한 모든 곳에서 공용으로 쓰는 확인창.
    /// 화면마다 확인창을 새로 만들지 말고 이 컴포넌트 하나를 씬에 두고 Open()만 호출하면 됨.
    /// 예: confirmDialog.Open("100골드를 사용해 해금하시겠습니까?", () => { 실제 해금 로직(); });
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ConfirmDialogController : MonoBehaviour, IPopupPanel
    {
        private UIDocument _document;
        private Label _messageLabel;
        private Button _confirmButton;
        private Button _cancelButton;

        private Action _onConfirm;

        // 팝업 GameObject는 기본적으로 하이어라키에서 비활성화 상태로 둘 것.
        // Show()가 SetActive(true)를 호출하는 순간 OnEnable이 동기적으로 실행되어 아래 필드들이 채워짐.
        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _messageLabel = root.Q<Label>("message-label");
            _confirmButton = root.Q<Button>("confirm-button");
            _cancelButton = root.Q<Button>("cancel-button");

            _confirmButton.clicked += OnConfirmClicked;
            _cancelButton.clicked += OnCancelClicked;
        }

        private void OnDisable()
        {
            // 메모리 누수/중복 호출 방지 — 열 때 구독한 이벤트는 닫을 때 반드시 해제
            if (_confirmButton != null) _confirmButton.clicked -= OnConfirmClicked;
            if (_cancelButton != null) _cancelButton.clicked -= OnCancelClicked;
        }

        public void Open(string message, Action onConfirm)
        {
            _onConfirm = onConfirm;
            PopupManager.Instance.Open(this); // Show() 호출 -> OnEnable 동기 실행 -> 이 아래부터 _messageLabel 사용 가능
            _messageLabel.text = message;
        }

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        private void OnConfirmClicked()
        {
            _onConfirm?.Invoke();
            PopupManager.Instance.RequestClose(this);
        }

        private void OnCancelClicked()
        {
            PopupManager.Instance.RequestClose(this);
        }
    }
}
