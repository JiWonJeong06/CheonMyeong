using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Economy;
using TowerDefense.Network;

namespace TowerDefense.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("테크트리 팝업 GameObject (하이어라키에서 기본 비활성화 상태)")]
        [SerializeField] private TechTreeController techTreePopup;

        [Tooltip("상점 팝업 GameObject (하이어라키에서 기본 비활성화 상태)")]
        [SerializeField] private ShopController shopPopup;

        [Tooltip("보관함 팝업 GameObject (하이어라키에서 기본 비활성화 상태)")]
        [SerializeField] private InventoryController inventoryPopup;

        [Tooltip("매칭 대기 팝업 GameObject (하이어라키에서 기본 비활성화 상태)")]
        [SerializeField] private MatchmakingOverlayController matchmakingOverlay;

        private UIDocument _document;
        private Label _goldLabel;
        private Label _gemLabel;
        private Button _playButton;
        private Button _settingsButton;
        private Button _techTreeButton;
        private Button _shopButton;
        private Button _inventoryButton;

        // UIDocument는 같은 오브젝트에 있으니 OnEnable에서 참조해도 안전함
        // (컴포넌트 추가 순서상 UIDocument가 이 스크립트보다 위에 있으면 이 스크립트 OnEnable 시점엔 이미 준비돼 있음).
        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _goldLabel = root.Q<Label>("gold-label");
            _gemLabel = root.Q<Label>("gem-label");
            _playButton = root.Q<Button>("play-button");
            _settingsButton = root.Q<Button>("settings-button");
            _techTreeButton = root.Q<Button>("techtree-button");
            _shopButton = root.Q<Button>("shop-button");
            _inventoryButton = root.Q<Button>("inventory-button");

            _playButton.clicked += OnPlayClicked;
            _settingsButton.clicked += OnSettingsClicked;
            _techTreeButton.clicked += OnTechTreeClicked;
            _shopButton.clicked += OnShopClicked;
            _inventoryButton.clicked += OnInventoryClicked;
        }

        // EconomyManager는 "다른 오브젝트"의 싱글턴이라 OnEnable이 아니라 Start에서 참조해야 안전함.
        // 유니티는 씬에 있는 모든 오브젝트의 Awake가 끝난 뒤에야 Start를 호출하는 걸 보장하지만,
        // OnEnable은 오브젝트마다 자기 Awake 직후 바로 실행돼서 다른 오브젝트가 아직 준비 안 됐을 수 있음.
        private void Start()
        {
            if (EconomyManager.Instance == null)
            {
                Debug.LogWarning("[MainMenu] EconomyManager를 찾을 수 없음 - 씬에 배치했는지 확인해줘.");
                return;
            }

            EconomyManager.Instance.OnGoldChanged += UpdateGoldLabel;
            EconomyManager.Instance.OnGemChanged += UpdateGemLabel;
            UpdateGoldLabel(EconomyManager.Instance.Gold);
            UpdateGemLabel(EconomyManager.Instance.Gem);
        }

        private void OnDisable()
        {
            _playButton.clicked -= OnPlayClicked;
            _settingsButton.clicked -= OnSettingsClicked;
            _techTreeButton.clicked -= OnTechTreeClicked;
            _shopButton.clicked -= OnShopClicked;
            _inventoryButton.clicked -= OnInventoryClicked;

            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnGoldChanged -= UpdateGoldLabel;
                EconomyManager.Instance.OnGemChanged -= UpdateGemLabel;
            }
        }

        private void UpdateGoldLabel(int value) => _goldLabel.text = value.ToString();
        private void UpdateGemLabel(int value) => _gemLabel.text = value.ToString();

        // 스테이지는 서버(MatchController.OnNetworkSpawn)가 무작위로 고르는 구조라 스테이지 선택
        // 화면 자체가 필요 없음 - Play를 누르면 바로 매칭을 시작함.
        private void OnPlayClicked()
        {
            if (MatchmakingService.Instance == null)
            {
                Debug.LogError("[MainMenu] MatchmakingService가 씬에 없음 - 매칭을 시작할 수 없음.");
                return;
            }
            if (matchmakingOverlay == null)
            {
                Debug.LogError("[MainMenu] matchmakingOverlay가 인스펙터에 연결 안 됨.");
                return;
            }

            matchmakingOverlay.Open();
            MatchmakingService.Instance.StartMatchmaking();
        }

        private void OnSettingsClicked() =>
            Debug.Log("[MainMenu] 설정 버튼 - 다른 개발자 담당이라 더미 로그만 찍음");

        private void OnTechTreeClicked() => techTreePopup.Open();
        private void OnShopClicked() => shopPopup.Open();

        private void OnInventoryClicked() => inventoryPopup.Open();
    }
}
