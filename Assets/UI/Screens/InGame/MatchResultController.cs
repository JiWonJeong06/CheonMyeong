using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using TowerDefense.Map;

namespace TowerDefense.UI
{
    /// <summary>
    /// 매치 종료 결과 팝업 - MatchController.OnMatchEnded(Assets/Scripts/Map/MatchController.cs)를
    /// 구독해서 승/패를 보여줌. 랭크 반영·매칭 세션 정리는 이미 MatchController.MatchEndedRpc 안에서
    /// 끝난 뒤(RankManager.ReportMatchResult, MatchmakingService.LeaveMatchAsync) 이 이벤트가
    /// 발생하므로, 여기서는 화면 표시 + 확인 시 메인메뉴 복귀만 책임짐.
    ///
    /// InGame 씬에도 이제 PopupManager(일시정지 메뉴, 항복 확인창)가 있지만, 이 결과 화면은 일부러
    /// 그 스택을 안 거침 - ESC로 닫히면 안 되고(매치가 끝났는데 취소할 방법이 없어야 함), 뭐가 열려
    /// 있든 무조건 최상단에 떠야 하기 때문. 대신 매치가 끝나는 순간 PopupManager.CloseAll()로 다른
    /// 팝업(일시정지 메뉴 등)을 먼저 정리해서, 이 결과 화면 아래에 다른 팝업이 파묻혀 남아있는 일이
    /// 없도록 함. 그래서 UIDocument의 style.display만 직접 토글함(ToastController와 같은 방식).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MatchResultController : MonoBehaviour
    {
        [Tooltip("결과 확인 후 돌아갈 메인메뉴 씬 이름 - Build Settings에 등록된 씬 이름과 정확히 같아야 함")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private UIDocument _document;
        private Label _resultLabel;
        private Button _confirmButton;

        private Action<bool> _matchEndedHandler;
        private Coroutine _subscribeRoutine;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;
            root.style.display = DisplayStyle.None; // 매치 끝나기 전엔 항상 숨김

            _resultLabel = root.Q<Label>("result-label");
            _confirmButton = root.Q<Button>("confirm-button");
            _confirmButton.clicked += OnConfirmClicked;

            _subscribeRoutine = StartCoroutine(SubscribeWhenReady());
        }

        private void OnDisable()
        {
            if (_subscribeRoutine != null)
            {
                StopCoroutine(_subscribeRoutine);
                _subscribeRoutine = null;
            }
            if (_confirmButton != null) _confirmButton.clicked -= OnConfirmClicked;
            if (MatchController.Instance != null && _matchEndedHandler != null)
            {
                MatchController.Instance.OnMatchEnded -= _matchEndedHandler;
            }
            _matchEndedHandler = null;
        }

        // MatchController는 in-scene placed NetworkObject라 씬 로드 직후 스폰이 끝날 때까지
        // Instance가 비어있을 수 있어서 한 프레임씩 기다렸다가 구독함.
        private IEnumerator SubscribeWhenReady()
        {
            while (MatchController.Instance == null) yield return null;
            _matchEndedHandler = HandleMatchEnded;
            MatchController.Instance.OnMatchEnded += _matchEndedHandler;
            _subscribeRoutine = null;
        }

        private void HandleMatchEnded(bool won)
        {
            // 일시정지 메뉴/항복 확인창이 열려있는 상태로 매치가 끝날 수 있음(항복 자체가 그 경로임) -
            // 결과 화면 아래에 다른 팝업이 남아있지 않도록 먼저 전부 닫음.
            PopupManager.Instance?.CloseAll();

            _resultLabel.text = won ? "승리!" : "패배...";
            _document.rootVisualElement.style.display = DisplayStyle.Flex;
        }

        private void OnConfirmClicked()
        {
            // MatchController.MatchEndedRpc가 이미 매칭 세션(Lobby/Relay)은 정리했지만
            // (MatchmakingService.LeaveMatchAsync), Netcode 연결(NetworkManager) 자체는 여기서
            // 명시적으로 끊어줘야 다음 매칭 때 깨끗한 상태로 새로 붙을 수 있음.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}
