using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDefense.UI
{
    // 확인이 필요 없이 잠깐 떴다 사라지는 알림. 배경 딤도 없고 클릭도 막지 않는다는 점에서
    // ConfirmDialog 같은 모달 팝업과는 성격이 다르다 (그래서 PopupManager 스택에 안 들어감).
    // 항상 켜져 있는 싱글턴 오브젝트로 두고 style.display만 토글한다 — SetActive로 끄면
    // 실행 중인 코루틴(HideAfterDelay)까지 같이 죽어버리기 때문.
    [RequireComponent(typeof(UIDocument))]
    public class ToastController : MonoBehaviour
    {
        public static ToastController Instance { get; private set; }

        [SerializeField] private float displaySeconds = 2f;

        private UIDocument _document;
        private Label _messageLabel;
        private Coroutine _hideCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;
            _messageLabel = root.Q<Label>("toast-label");
            root.style.display = DisplayStyle.None;
        }

        public void Show(string message)
        {
            if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);

            _messageLabel.text = message;
            _document.rootVisualElement.style.display = DisplayStyle.Flex;
            _hideCoroutine = StartCoroutine(HideAfterDelay());
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSeconds(displaySeconds);
            _document.rootVisualElement.style.display = DisplayStyle.None;
            _hideCoroutine = null;
        }
    }
}
