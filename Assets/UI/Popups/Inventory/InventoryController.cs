using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Data;

namespace TowerDefense.UI
{
    /// <summary>
    /// 보관함 - 전체 캐릭터를 그리드로 보여주고, 해금된 캐릭터는 컬러, 미해금은 회색조로 표시함.
    /// 다른 타워 디펜스 게임들의 "캐릭터 도감/보관함" 화면과 동일한 컨셉의 더미 버전.
    /// 미해금 슬롯을 누르면 확인창을 띄우고, 확인하면 CharacterDataSO.unlockCost/unlockCurrency만큼
    /// 재화를 소모해서 해금함 (실제 소모/해금 로직은 CharacterUnlockManager에 위임).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InventoryController : MonoBehaviour, IPopupPanel
    {
        [Tooltip("확인창 GameObject (하이어라키에서 기본 비활성화 상태)")]
        [SerializeField] private ConfirmDialogController confirmDialog;

        private UIDocument _document;
        private VisualElement _grid;
        private Button _closeButton;

        // 팝업은 열릴 때마다 OnEnable이 다시 돌기 때문에, 열 때마다 최신 해금 상태로 그리드를 새로 그림 -
        // 그래서 여기서 CharacterDatabase/CharacterUnlockManager를 참조해도 안전함
        // (다른 오브젝트의 싱글턴이지만, 이 시점엔 이미 씬의 모든 Start()가 끝난 뒤임).
        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _grid = root.Q<VisualElement>("inventory-grid");
            _closeButton = root.Q<Button>("close-button");
            _closeButton.clicked += OnCloseClicked;

            BuildGrid();
        }

        private void OnDisable()
        {
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;

            if (CharacterUnlockManager.Instance != null)
            {
                CharacterUnlockManager.Instance.OnCharacterUnlocked -= OnCharacterUnlockedChanged;
            }
        }

        public void Open() => PopupManager.Instance.Open(this);

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        private void BuildGrid()
        {
            _grid.Clear();

            if (CharacterUnlockManager.Instance != null)
            {
                // 중복 구독 방지 - Clear 대신 -= 먼저 해두고 다시 +=
                CharacterUnlockManager.Instance.OnCharacterUnlocked -= OnCharacterUnlockedChanged;
                CharacterUnlockManager.Instance.OnCharacterUnlocked += OnCharacterUnlockedChanged;
            }

            foreach (var data in CharacterDatabase.GetAll())
            {
                _grid.Add(BuildSlot(data));
            }
        }

        private VisualElement BuildSlot(CharacterDataSO data)
        {
            bool unlocked = CharacterUnlockManager.Instance != null && CharacterUnlockManager.Instance.IsUnlocked(data.characterId);

            var slot = new VisualElement();
            slot.AddToClassList("character-slot");
            slot.AddToClassList(unlocked ? "unlocked" : "locked");

            var icon = new VisualElement();
            icon.AddToClassList("character-icon");
            if (data.iconSprite != null)
            {
                icon.style.backgroundImage = new StyleBackground(data.iconSprite);
            }
            slot.Add(icon);

            var nameLabel = new Label(data.displayName);
            nameLabel.AddToClassList("character-name");
            slot.Add(nameLabel);

            if (!unlocked)
            {
                string currencySymbol = data.unlockCurrency == CurrencyType.Gem ? "◆" : "G";
                var costLabel = new Label($"{currencySymbol} {data.unlockCost}");
                costLabel.AddToClassList("character-cost");
                slot.Add(costLabel);
            }

            slot.RegisterCallback<ClickEvent>(_ => OnSlotClicked(data));
            return slot;
        }

        private void OnSlotClicked(CharacterDataSO data)
        {
            bool unlocked = CharacterUnlockManager.Instance != null && CharacterUnlockManager.Instance.IsUnlocked(data.characterId);

            if (unlocked)
            {
                // 상세 화면은 아직 범위 밖 - 더미 단계에선 로그만 남김.
                Debug.Log($"[Inventory] {data.displayName} 상세 보기 (더미, 아직 미구현)");
                return;
            }

            if (confirmDialog == null)
            {
                Debug.LogWarning("[Inventory] ConfirmDialog가 연결 안 돼있음 - 인스펙터에서 연결할 것.");
                return;
            }

            string currencyName = data.unlockCurrency == CurrencyType.Gem ? "보석" : "골드";
            confirmDialog.Open(
                $"{data.displayName}을(를) {currencyName} {data.unlockCost}(으)로 해금하시겠습니까?",
                () =>
                {
                    if (CharacterUnlockManager.Instance == null) return;
                    bool success = CharacterUnlockManager.Instance.TryUnlock(data);
                    if (!success)
                    {
                        ToastController.Instance?.Show("재화가 부족합니다");
                    }
                });
        }

        // 다른 경로(예: 상점)에서 해금됐을 때도 그리드가 갱신되도록.
        private void OnCharacterUnlockedChanged(string characterId) => BuildGrid();

        private void OnCloseClicked() => PopupManager.Instance.RequestClose(this);
    }
}
