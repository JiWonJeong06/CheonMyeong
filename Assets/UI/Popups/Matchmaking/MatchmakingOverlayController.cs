using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Network;

namespace TowerDefense.UI
{
    /// <summary>
    /// Play 버튼을 누른 뒤 상대를 찾는 동안 보여주는 대기 팝업. MatchmakingService(Assets/Scripts/
    /// Network/MatchmakingService.cs)의 실패 이벤트만 구독함 - 성공(매칭 완료)은 따로 안 봐도 됨:
    /// 매칭이 실제로 끝나면 NetworkSceneManager가 InGame 씬을 로드하면서 MainMenu 씬 전체(이 팝업
    /// 포함)가 자동으로 사라지기 때문.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MatchmakingOverlayController : MonoBehaviour, IPopupPanel
    {
        private UIDocument _document;
        private Label _statusLabel;
        private Button _cancelButton;

        // 팝업 GameObject는 하이어라키에서 기본 비활성화 상태로 둘 것.
        // Show()가 SetActive(true)를 호출하는 순간 OnEnable이 동기적으로 실행되어 아래 필드들이 채워짐.
        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _statusLabel = root.Q<Label>("status-label");
            _cancelButton = root.Q<Button>("cancel-button");
            _cancelButton.clicked += OnCancelClicked;

            if (MatchmakingService.Instance != null)
            {
                MatchmakingService.Instance.OnMatchmakingFailed += OnMatchmakingFailed;
            }
        }

        private void OnDisable()
        {
            // 메모리 누수/중복 구독 방지 - 열 때 구독한 이벤트는 닫을 때 반드시 해제.
            if (_cancelButton != null) _cancelButton.clicked -= OnCancelClicked;
            if (MatchmakingService.Instance != null)
            {
                MatchmakingService.Instance.OnMatchmakingFailed -= OnMatchmakingFailed;
            }
        }

        public void Open()
        {
            PopupManager.Instance.Open(this); // Show() 호출 -> OnEnable 동기 실행 -> 아래부터 필드 사용 가능
            _statusLabel.text = "상대를 찾는 중...";
        }

        public void Show() => gameObject.SetActive(true);

        public void Hide()
        {
            // ESC나 취소 버튼으로 닫힐 때 매칭이 아직 진행 중이면 같이 취소함 - 안 그러면 화면만
            // 사라지고 백그라운드에서 매칭은 계속 진행되다가 갑자기 인게임 씬으로 넘어가버림.
            if (MatchmakingService.Instance != null && MatchmakingService.Instance.IsMatchmaking)
            {
                MatchmakingService.Instance.CancelMatchmaking();
            }
            gameObject.SetActive(false);
        }

        private void OnCancelClicked() => PopupManager.Instance.RequestClose(this);

        private void OnMatchmakingFailed(string reason)
        {
            ToastController.Instance?.Show($"매칭 실패: {reason}");
            PopupManager.Instance.RequestClose(this);
        }
    }
}
