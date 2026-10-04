using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Data;

namespace TowerDefense.UI
{
    /// <summary>
    /// 보관함 - 이름은 "보관함"이지만 실제로는 타워 디펜스류 게임의 "편성(덱 구성)" 화면에 가까움.
    /// 상단엔 편성 저장 프리셋 탭(DeckManager.PresetCount개)과 그 프리셋의 덱 슬롯(DeckManager.DeckSize칸)이,
    /// 아래엔 보유 캐릭터 전체 로스터(해금=컬러/미해금=회색조)가 있음.
    /// 로스터에서 해금된 캐릭터를 누르면 현재 편성에 넣거나(빈 슬롯 자동 배정) 빼는(토글) 방식이고,
    /// 같은 캐릭터를 여러 슬롯에 중복 배치하는 건 막혀있음(DeckManager.ToggleCharacter 참고).
    /// 미해금 캐릭터를 누르면 여기서 구매/해금하지 않음 - 캐릭터 해금은 테크트리 화면에서만
    /// 진행되도록 정책이 바뀌어서(기획 확인됨), 여기선 안내 토스트만 띄움.
    ///
    /// 편성 변경은 "편성 저장" 버튼을 눌러야만 실제로 저장됨(DeckManager 참고) - 저장 안 하고
    /// 다른 편성 탭으로 넘어가거나 팝업을 닫으면 편집 중이던 내용은 버려짐. 또한 편성 5칸이
    /// 전부 채워져야만 저장이 실제로 반영됨(DeckManager.SaveActivePreset 참고).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InventoryController : MonoBehaviour, IPopupPanel
    {
        private UIDocument _document;
        private VisualElement _presetTabs;
        private VisualElement _deckSlots;
        private Label _saveStatusLabel;
        private Button _saveButton;
        private VisualElement _grid;
        private Button _closeButton;

        // 팝업은 열릴 때마다 OnEnable이 다시 돌기 때문에, 열 때마다 최신 상태로 전부 새로 그림 -
        // 그래서 여기서 CharacterDatabase/CharacterUnlockManager/DeckManager를 참조해도 안전함
        // (다른 오브젝트의 싱글턴이지만, 이 시점엔 이미 씬의 모든 Start()가 끝난 뒤임).
        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _presetTabs = root.Q<VisualElement>("preset-tabs");
            _deckSlots = root.Q<VisualElement>("deck-slots");
            _saveStatusLabel = root.Q<Label>("deck-save-status");
            _saveButton = root.Q<Button>("deck-save-button");
            _grid = root.Q<VisualElement>("inventory-grid");
            _closeButton = root.Q<Button>("close-button");
            _closeButton.clicked += OnCloseClicked;
            _saveButton.clicked += OnSaveClicked;

            RefreshAll();
        }

        private void OnDisable()
        {
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
            if (_saveButton != null) _saveButton.clicked -= OnSaveClicked;

            if (CharacterUnlockManager.Instance != null)
            {
                CharacterUnlockManager.Instance.OnCharacterUnlocked -= OnExternalChanged;
            }

            if (DeckManager.Instance != null)
            {
                // 구독부터 끊어서, 바로 아래 DiscardDraft()가 쏘는 OnDeckChanged 때문에 팝업이 닫히는
                // 도중에 불필요하게 한 번 더 다시 그려지는 걸 막음(어차피 비활성화될 UI라 낭비임).
                DeckManager.Instance.OnDeckChanged -= OnExternalChanged;

                // 저장 안 하고 팝업을 닫는 경우 - 편집 중이던 내용은 버리고 마지막 저장 상태로 되돌림.
                if (DeckManager.Instance.IsDraftDirty)
                {
                    DeckManager.Instance.DiscardDraft();
                }
            }
        }

        public void Open() => PopupManager.Instance.Open(this);

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        // 캐릭터 해금 상태가 바뀌거나(다른 경로로 해금됨) 편성이 바뀌면 전체를 다시 그림 -
        // 로스터의 in-deck 강조 표시나 덱 슬롯 내용이 서로 영향을 주기 때문에 통째로 갱신하는 게 안전함.
        private void OnExternalChanged(string _) => RefreshAll();
        private void OnExternalChanged() => RefreshAll();

        private void RefreshAll()
        {
            if (CharacterUnlockManager.Instance != null)
            {
                CharacterUnlockManager.Instance.OnCharacterUnlocked -= OnExternalChanged;
                CharacterUnlockManager.Instance.OnCharacterUnlocked += OnExternalChanged;
            }

            if (DeckManager.Instance != null)
            {
                DeckManager.Instance.OnDeckChanged -= OnExternalChanged;
                DeckManager.Instance.OnDeckChanged += OnExternalChanged;
            }

            BuildPresetTabs();
            BuildDeckSlots();
            BuildGrid();
            UpdateSaveStatusLabel();
        }

        private void BuildPresetTabs()
        {
            _presetTabs.Clear();

            if (DeckManager.Instance == null)
            {
                Debug.LogWarning("[Inventory] DeckManager를 찾을 수 없음 - 씬에 배치했는지 확인할 것.");
                return;
            }

            int active = DeckManager.Instance.ActivePresetIndex;
            for (int i = 0; i < DeckManager.PresetCount; i++)
            {
                int presetIndex = i; // 클로저 캡처용 로컬 변수
                var tab = new Button { text = $"편성 {i + 1}" };
                tab.AddToClassList("preset-tab");
                if (i == active) tab.AddToClassList("active");
                tab.clicked += () => OnPresetTabClicked(presetIndex);
                _presetTabs.Add(tab);
            }
        }

        // 저장 안 한 편집 내용이 있는 채로 다른 탭을 누르면 그 내용은 버려짐 - 미리 안내만 띄워줌.
        private void OnPresetTabClicked(int index)
        {
            if (DeckManager.Instance == null) return;
            if (index == DeckManager.Instance.ActivePresetIndex) return;

            if (DeckManager.Instance.IsDraftDirty)
            {
                ToastController.Instance?.Show("저장하지 않은 편성 변경사항은 적용되지 않습니다");
            }

            DeckManager.Instance.SetActivePreset(index);
        }

        private void OnSaveClicked()
        {
            if (DeckManager.Instance == null) return;

            bool success = DeckManager.Instance.SaveActivePreset();
            ToastController.Instance?.Show(success
                ? "편성을 저장했습니다"
                : $"편성 {DeckManager.DeckSize}칸을 모두 채워야 저장할 수 있습니다");
        }

        private void UpdateSaveStatusLabel()
        {
            if (_saveStatusLabel == null) return;
            bool dirty = DeckManager.Instance != null && DeckManager.Instance.IsDraftDirty;
            _saveStatusLabel.text = dirty ? "* 변경사항 저장 안 됨" : string.Empty;
        }

        private void BuildDeckSlots()
        {
            _deckSlots.Clear();

            if (DeckManager.Instance == null) return;

            var deck = DeckManager.Instance.GetDraftDeck();
            for (int i = 0; i < DeckManager.DeckSize; i++)
            {
                string characterId = i < deck.Count ? deck[i] : null;
                _deckSlots.Add(BuildDeckSlotElement(i, characterId));
            }
        }

        private VisualElement BuildDeckSlotElement(int slotIndex, string characterId)
        {
            var slot = new VisualElement();
            slot.AddToClassList("deck-slot");

            if (string.IsNullOrEmpty(characterId))
            {
                slot.AddToClassList("empty");
                var placeholder = new Label("+");
                placeholder.AddToClassList("deck-slot-placeholder");
                slot.Add(placeholder);
                // 빈 슬롯 자체는 눌러도 할 일이 없음(로스터에서 캐릭터를 눌러야 채워짐) - 클릭 핸들러 없음.
                return slot;
            }

            slot.AddToClassList("filled");

            var data = CharacterDatabase.GetById(characterId);

            var icon = new VisualElement();
            icon.AddToClassList("deck-slot-icon");
            if (data != null && data.iconSprite != null)
            {
                icon.style.backgroundImage = new StyleBackground(data.iconSprite);
            }
            slot.Add(icon);

            var nameLabel = new Label(data != null ? data.displayName : characterId);
            nameLabel.AddToClassList("deck-slot-name");
            slot.Add(nameLabel);

            // 채워진 슬롯을 누르면 편성에서 뺌.
            slot.RegisterCallback<ClickEvent>(_ => DeckManager.Instance.ClearSlot(slotIndex));

            return slot;
        }

        private void BuildGrid()
        {
            _grid.Clear();

            foreach (var data in CharacterDatabase.GetAll())
            {
                _grid.Add(BuildSlot(data));
            }
        }

        private VisualElement BuildSlot(CharacterDataSO data)
        {
            bool unlocked = CharacterUnlockManager.Instance != null && CharacterUnlockManager.Instance.IsUnlocked(data.characterId);
            bool inDeck = DeckManager.Instance != null && DeckManager.Instance.IsInDraftDeck(data.characterId);

            var slot = new VisualElement();
            slot.AddToClassList("character-slot");
            slot.AddToClassList(unlocked ? "unlocked" : "locked");
            if (inDeck) slot.AddToClassList("in-deck");

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

            if (!unlocked)
            {
                // 캐릭터 해금은 여기서 안 함 - 테크트리 화면에서만 진행하도록 정책이 바뀜(기획 확인됨).
                ToastController.Instance?.Show("테크트리에서 해금할 수 있습니다");
                return;
            }

            if (DeckManager.Instance == null)
            {
                Debug.LogWarning("[Inventory] DeckManager가 연결 안 돼있음 - 씬에 배치했는지 확인할 것.");
                return;
            }

            bool success = DeckManager.Instance.ToggleCharacter(data.characterId);
            if (!success)
            {
                ToastController.Instance?.Show("편성 인원이 가득 찼습니다");
            }
        }

        private void OnCloseClicked() => PopupManager.Instance.RequestClose(this);
    }
}
