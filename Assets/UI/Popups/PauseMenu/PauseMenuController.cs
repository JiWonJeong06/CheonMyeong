using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Map;

namespace TowerDefense.UI
{
    /// <summary>
    /// 인게임 일시정지 메뉴 - HUD의 일시정지 버튼(InGameHUDController)에서 열림.
    /// 실시간 1:1 대전이라 실제로 게임 진행을 멈추는(Time.timeScale) 기능은 없음 - 상대방은
    /// 이 메뉴가 떠있는 동안에도 계속 진행 중이라, "일시정지"는 로컬 화면을 가리는 메뉴 오버레이일
    /// 뿐이고(그래서 배경 타워 배치 입력만 막힘), 항복만 실제로 서버에 요청을 보내는 유일한 버튼임.
    ///
    /// 다른 팝업(ConfirmDialog, TechTree 등)과 동일하게 IPopupPanel + PopupManager 조합을 씀 -
    /// InGame 씬에도 MainMenu 씬과 똑같이 PopupManager 싱글턴 GameObject가 있어야 함(Editor 배치
    /// 체크리스트 참고).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenuController : MonoBehaviour, IPopupPanel
    {
        [Tooltip("항복 확인에 재사용할 공용 확인창 - InGame 씬에 배치된 ConfirmDialogController를 연결할 것 (MainMenu 씬의 확인창과는 별개 인스턴스)")]
        [SerializeField] private ConfirmDialogController confirmDialog;

        private UIDocument _document;
        private Button _resumeButton;
        private Button _settingsButton;
        private Button _surrenderButton;

        // 팝업 GameObject는 하이어라키에서 기본 비활성화 상태로 둘 것.
        // Show()가 SetActive(true)를 호출하는 순간 OnEnable이 동기적으로 실행되어 아래 필드들이 채워짐.
        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _resumeButton = root.Q<Button>("resume-button");
            _settingsButton = root.Q<Button>("settings-button");
            _surrenderButton = root.Q<Button>("surrender-button");

            _resumeButton.clicked += OnResumeClicked;
            _settingsButton.clicked += OnSettingsClicked;
            _surrenderButton.clicked += OnSurrenderClicked;
        }

        private void OnDisable()
        {
            // 메모리 누수/중복 구독 방지 - 열 때 구독한 이벤트는 닫을 때 반드시 해제.
            if (_resumeButton != null) _resumeButton.clicked -= OnResumeClicked;
            if (_settingsButton != null) _settingsButton.clicked -= OnSettingsClicked;
            if (_surrenderButton != null) _surrenderButton.clicked -= OnSurrenderClicked;
        }

        public void Open() => PopupManager.Instance.Open(this);

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        private void OnResumeClicked() => PopupManager.Instance.RequestClose(this);

        private void OnSettingsClicked() =>
            Debug.Log("[PauseMenu] 설정 버튼 - 다른 개발자 담당이라 더미 로그만 찍음");

        private void OnSurrenderClicked()
        {
            if (confirmDialog == null)
            {
                Debug.LogError("[PauseMenu] confirmDialog가 인스펙터에 연결 안 됨.");
                return;
            }

            // ConfirmDialog는 PopupManager 스택에 새로 쌓이므로(일시정지 메뉴 위에 확인창이 뜸),
            // 취소를 누르면 확인창만 닫히고 일시정지 메뉴는 그대로 남음 - 의도된 동작.
            confirmDialog.Open("정말 항복하시겠습니까?\n패배로 처리되고 트로피가 감소합니다.", () =>
            {
                if (MatchController.Instance == null)
                {
                    Debug.LogError("[PauseMenu] MatchController.Instance가 없어서 항복 요청을 보낼 수 없음.");
                    return;
                }
                MatchController.Instance.RequestSurrenderRpc();
                PopupManager.Instance.RequestClose(this); // 일시정지 메뉴도 같이 닫음 - 곧 MatchResult 화면이 뜸
            });
        }
    }
}
