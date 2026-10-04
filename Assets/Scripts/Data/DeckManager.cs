using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// 편성(덱) 관리 - 저장 프리셋 PresetCount개, 프리셋마다 덱 슬롯 DeckSize개.
    /// 덱 인원수(DeckSize)는 기획 확정 전 더미값(5) - 나중에 5~10 사이로 확정되면
    /// 이 상수 한 줄만 바꾸면 UI(InventoryController)와 저장 로직이 전부 그대로 따라감.
    /// 같은 캐릭터를 여러 슬롯에 중복 배치하는 건 금지 정책(기획 확인됨) - ToggleCharacter가 그걸 보장함.
    ///
    /// 저장 방식: 캐릭터를 넣고 빼는 건 전부 "임시 편집본(_draft)"에서만 일어나고,
    /// SaveActivePreset()을 명시적으로 호출해야 그 편집본이 실제 저장 상태(_savedPresets, PlayerPrefs)에
    /// 반영됨. 저장 안 하고 다른 편성 탭으로 넘어가거나 팝업을 닫으면 편집 중이던 내용은 버려짐
    /// (SetActivePreset / DiscardDraft가 임시 편집본을 저장된 상태로 되돌림).
    /// </summary>
    public class DeckManager : MonoBehaviour
    {
        public static DeckManager Instance { get; private set; }

        public const int DeckSize = 5;    // 더미값. 기획 확정되면 이 값만 수정할 것 (5~10 범위 예정).
        public const int PresetCount = 5; // 편성 저장 탭 개수 - 탭 UI라 고정값으로 둠.

        private const string PrefKeyPrefix = "Deck_Preset_"; // + 프리셋 인덱스

        private List<string>[] _savedPresets; // 실제로 저장된(=PlayerPrefs와 동기화된) 확정 상태
        private List<string> _draft;          // 현재 활성 프리셋을 편집 중인 임시 상태 - 저장 전까지는 여기만 바뀜

        public int ActivePresetIndex { get; private set; }

        public event Action OnDeckChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _savedPresets = new List<string>[PresetCount];
            for (int i = 0; i < PresetCount; i++)
            {
                _savedPresets[i] = LoadPresetFromPrefs(i);
            }

            ActivePresetIndex = 0;
            _draft = CopyOf(_savedPresets[0]);
        }

        private static List<string> LoadPresetFromPrefs(int index)
        {
            var slots = new List<string>(new string[DeckSize]);
            string raw = PlayerPrefs.GetString(PrefKeyPrefix + index, string.Empty);
            if (string.IsNullOrEmpty(raw)) return slots;

            var parts = raw.Split(',');
            for (int i = 0; i < DeckSize && i < parts.Length; i++)
            {
                slots[i] = string.IsNullOrEmpty(parts[i]) ? null : parts[i];
            }
            return slots;
        }

        private void PersistPresetToPrefs(int index)
        {
            string raw = string.Join(",", _savedPresets[index].Select(id => id ?? string.Empty));
            PlayerPrefs.SetString(PrefKeyPrefix + index, raw);
            PlayerPrefs.Save();
        }

        private static List<string> CopyOf(List<string> src) => new List<string>(src);

        /// <summary>편집 중인 내용이 마지막 저장 상태와 다른지. UI에서 "저장 안 됨" 표시에 사용.</summary>
        public bool IsDraftDirty
        {
            get
            {
                var saved = _savedPresets[ActivePresetIndex];
                for (int i = 0; i < DeckSize; i++)
                {
                    if (_draft[i] != saved[i]) return true;
                }
                return false;
            }
        }

        // 탭을 눌러 다른 프리셋으로 넘어갈 때 호출함. 저장 안 한 편집 내용은 여기서 그냥 버려짐(요청 사항) -
        // 저장하고 싶으면 넘어가기 전에 반드시 SaveActivePreset()을 호출해야 함.
        public void SetActivePreset(int index)
        {
            if (index < 0 || index >= PresetCount || index == ActivePresetIndex) return;
            ActivePresetIndex = index;
            _draft = CopyOf(_savedPresets[index]);
            OnDeckChanged?.Invoke();
        }

        // 팝업을 저장 없이 닫을 때 호출 - 편집 중이던 내용을 마지막 저장 상태로 되돌림.
        public void DiscardDraft()
        {
            _draft = CopyOf(_savedPresets[ActivePresetIndex]);
            OnDeckChanged?.Invoke();
        }

        /// <summary>
        /// "편성 저장" 버튼을 눌렀을 때만 호출 - 이때 비로소 PlayerPrefs에 실제로 반영됨.
        /// DeckSize칸이 전부 채워져 있어야만 저장되고, 빈 칸이 하나라도 있으면 저장 자체를 거부함
        /// (기획 확인 사항) - 호출 쪽에서 반환값을 보고 "다 채워야 저장 가능" 안내를 띄우면 됨.
        /// </summary>
        public bool SaveActivePreset()
        {
            if (_draft.Any(string.IsNullOrEmpty)) return false;

            _savedPresets[ActivePresetIndex] = CopyOf(_draft);
            PersistPresetToPrefs(ActivePresetIndex);
            OnDeckChanged?.Invoke();
            return true;
        }

        public IReadOnlyList<string> GetDraftDeck() => _draft;

        public bool IsInDraftDeck(string characterId) =>
            !string.IsNullOrEmpty(characterId) && _draft.Contains(characterId);

        /// <summary>
        /// 실제 대전(인게임)에서 써야 할 덱 - 편집 중인 임시본(_draft)이 아니라 마지막으로 "편성 저장"을
        /// 눌러 확정된 프리셋을 돌려줌. 인게임 덱 UI(InGameDeckController)는 반드시 이걸 써야 함 -
        /// GetDraftDeck()은 보관함 편성 화면 전용이고, 저장 안 한 편집 중 상태를 그대로 노출하기 때문에
        /// 게임플레이에 쓰면 "편성 화면을 열어놨다가 저장 안 하고 나간 상태"가 그대로 반영되는 버그가 생김.
        /// 슬롯이 비어있을 수 있음(한 번도 저장 안 한 프리셋) - 호출 쪽에서 null 체크할 것.
        /// </summary>
        public IReadOnlyList<string> GetActiveDeck() => _savedPresets[ActivePresetIndex];

        // 덱 슬롯을 직접 클릭해서 비울 때 사용 (임시 편집본만 변경됨).
        public void ClearSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= DeckSize) return;
            if (_draft[slotIndex] == null) return;

            _draft[slotIndex] = null;
            OnDeckChanged?.Invoke();
        }

        /// <summary>
        /// 로스터에서 캐릭터를 눌렀을 때 호출함. 이미 편집 중인 편성에 들어있으면 빼고(토글 off),
        /// 없으면 빈 슬롯에 채움(토글 on). 이 변경은 저장 버튼을 누르기 전까지는 임시 편집본에만 남음.
        /// 빈 슬롯이 없으면 false를 반환해서 호출 쪽에서 "편성 인원이 가득 찼습니다" 안내를 띄울 수 있게 함.
        /// </summary>
        public bool ToggleCharacter(string characterId)
        {
            int existingIndex = _draft.IndexOf(characterId);
            if (existingIndex >= 0)
            {
                _draft[existingIndex] = null;
                OnDeckChanged?.Invoke();
                return true;
            }

            int emptyIndex = _draft.IndexOf(null);
            if (emptyIndex < 0) return false; // 편성 꽉 참

            _draft[emptyIndex] = characterId;
            OnDeckChanged?.Invoke();
            return true;
        }
    }
}
