using System;
using System.Collections.Generic;
using UnityEngine;
using TowerDefense.Economy;

namespace TowerDefense.Data
{
    /// <summary>
    /// "어떤 캐릭터가 해금됐는지"만 담당하는 더미 매니저. PlayerPrefs에 characterId를
    /// 쉼표로 이어붙인 문자열 하나로 저장함 (EconomyManager와 같은 임시 저장 방식 —
    /// 나중에 정식 세이브 스키마가 생기면 저장 로직만 바꾸면 되고 IsUnlocked/TryUnlock을
    /// 쓰는 쪽 코드는 안 바뀌어도 되게 설계함).
    ///
    /// 실제 재화 차감은 여기서 EconomyManager에 위임함 - 재화 종류(Gold/Gem)는
    /// CharacterDataSO.unlockCurrency를 그대로 따름.
    /// </summary>
    public class CharacterUnlockManager : MonoBehaviour
    {
        public static CharacterUnlockManager Instance { get; private set; }

        private const string UnlockedKey = "CharacterUnlock_UnlockedIds";

        private readonly HashSet<string> _unlockedIds = new();

        public event Action<string> OnCharacterUnlocked;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Load();
        }

        // 시작 캐릭터(CharacterDataSO.isStarter)는 저장된 해금 목록과 상관없이 항상 보유 상태.
        public bool IsUnlocked(string characterId)
        {
            if (_unlockedIds.Contains(characterId)) return true;
            var data = CharacterDatabase.GetById(characterId);
            return data != null && data.isStarter;
        }

        /// <summary>
        /// 재화를 소모해서 캐릭터를 해금 시도함. 이미 해금된 캐릭터거나 재화가 부족하면 false.
        /// 성공하면 OnCharacterUnlocked 이벤트가 발생함 (UI 갱신용).
        /// </summary>
        public bool TryUnlock(CharacterDataSO data)
        {
            if (data == null) return false;
            if (IsUnlocked(data.characterId)) return false;

            if (EconomyManager.Instance == null)
            {
                Debug.LogWarning("[CharacterUnlockManager] EconomyManager를 찾을 수 없음.");
                return false;
            }

            bool spent = data.unlockCurrency == CurrencyType.Gem
                ? EconomyManager.Instance.TrySpendGem(data.unlockCost)
                : EconomyManager.Instance.TrySpendGold(data.unlockCost);

            if (!spent) return false;

            _unlockedIds.Add(data.characterId);
            Save();
            OnCharacterUnlocked?.Invoke(data.characterId);
            return true;
        }

        // 디버그/기획 확인용 - 재화 소모 없이 강제로 해금 처리 (인스펙터 우클릭 메뉴로 호출).
        [ContextMenu("Debug: Unlock All")]
        private void DebugUnlockAll()
        {
            foreach (var data in CharacterDatabase.GetAll())
            {
                _unlockedIds.Add(data.characterId);
            }
            Save();
        }

        /// <summary>
        /// [디버그 전용] 해금 기록을 전부 지움 - QA 테스트 중 해금 흐름을 반복 확인하기 위한 기능.
        /// TechTree의 "[테스트] 초기화" 버튼(TechTreeManager.DebugResetAllPurchases)에서 호출됨.
        /// 출시 전에 호출부(UI 버튼 포함)와 함께 반드시 제거할 것.
        /// </summary>
        [ContextMenu("Debug: Reset Unlocks")]
        public void DebugResetUnlocks()
        {
            _unlockedIds.Clear();
            Save();
            OnCharacterUnlocked?.Invoke(null);
        }

        private void Load()
        {
            _unlockedIds.Clear();
            string raw = PlayerPrefs.GetString(UnlockedKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;

            foreach (string id in raw.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(id)) _unlockedIds.Add(id);
            }
        }

        private void Save()
        {
            PlayerPrefs.SetString(UnlockedKey, string.Join(",", _unlockedIds));
        }
    }
}
