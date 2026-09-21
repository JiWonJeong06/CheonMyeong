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
            if (_document == null)
            {
                // [RequireComponent]가 있어도 방어적으로 체크함 - 프리팹이 깨져서 컴포넌트가
                // 빠진 채로 인스턴스화되는 경우가 실무에서 종종 있음.
                Debug.LogError("[ToastController] UIDocument 컴포넌트를 못 찾음.", this);
                return;
            }

            var root = _document.rootVisualElement;
            if (root == null)
            {
                // UI Toolkit(com.unity.ui, 6000.3 내장)에서 UIDocument의 Panel Settings가
                // 비어있거나 아직 패널이 초기화 안 된 시점(예: DontDestroyOnLoad 오브젝트가
                // 아주 이른 프레임에 OnEnable되는 경우)이면 rootVisualElement가 예외 없이
                // null로 돌아옴 - 여기서 안 걸러내면 나중에 Show() 호출 시점에서야 터짐.
                Debug.LogError("[ToastController] rootVisualElement가 null - UIDocument의 Panel Settings가 인스펙터에 연결돼 있는지 확인할 것.", this);
                return;
            }

            _messageLabel = root.Q<Label>("toast-label");
            if (_messageLabel == null)
            {
                // Q<Label>()도 못 찾으면 예외 없이 조용히 null을 반환함 - UXML 쪽에
                // name="toast-label" Label이 실제로 있는지 확인 필요.
                Debug.LogWarning("[ToastController] UXML에서 name=\"toast-label\" Label을 못 찾음 - UXML 구조를 확인할 것.", this);
            }

            root.style.display = DisplayStyle.None;
        }

        public void Show(string message)
        {
            if (_document == null || _messageLabel == null)
            {
                // 초기화가 덜 된 상태에서 Show가 불려도(예: OnEnable 실패) 여기서 조용히
                // 리턴해서 방어함 - Toast 하나 때문에 TowerPlacementController.Update()가
                // 매 프레임 예외를 던지며 타워 배치 자체가 막히는 걸 막는 게 목적.
                Debug.LogWarning($"[ToastController] 초기화 안 된 상태에서 Show(\"{message}\") 호출됨 - 위쪽 OnEnable 에러 로그를 확인할 것.", this);
                return;
            }

            if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);

            _messageLabel.text = message;
            _document.rootVisualElement.style.display = DisplayStyle.Flex;
            _hideCoroutine = StartCoroutine(HideAfterDelay());
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSeconds(displaySeconds);
            if (_document != null)
            {
                _document.rootVisualElement.style.display = DisplayStyle.None;
            }
            _hideCoroutine = null;
        }
    }
}
