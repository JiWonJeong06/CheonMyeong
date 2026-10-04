using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TowerDefense.Economy;

namespace TowerDefense.Data
{
    /// <summary>
    /// 캐릭터별 레벨(노드 트리, Lv1~50) 보관/레벨업. 캐릭터 종류마다 레벨이 하나이고(EconomyManager와 같은
    /// 더미 저장 방식 - PlayerPrefs) 비용은 CharacterLevelTable(엑셀 '캐릭터 레벨' 시트)을 따름:
    /// 다음 레벨 금화, 돌파 레벨(10/20/30/40)에서는 금화 + 태극 휘장.
    ///
    /// 씬에 따로 배치하지 않아도 게임 시작 시 자동 생성됨(RuntimeInitializeOnLoadMethod) - 이미
    /// 만들어져 있으면(DontDestroyOnLoad) 중복 생성 안 함. 정식 SaveData 스키마가 나오면 저장 부분만 교체.
    /// 이벤트(OnLevelChanged) 구독자는 OnDisable/OnDestroy에서 반드시 해제할 것(싱글톤이라 해제 안 하면
    /// 파괴된 UI 참조가 남음).
    /// </summary>
    public class CharacterLevelManager : MonoBehaviour
    {
        public static CharacterLevelManager Instance { get; private set; }

        private const string SaveKey = "CharacterLevels";

        private readonly Dictionary<string, int> _levels = new();

        /// <summary>(characterId, 새 레벨). 디버그 초기화 땐 (null, 1)로 한 번 발생.</summary>
        public event Action<string, int> OnLevelChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return; // 도메인 리로드 끄기 옵션이어도 파괴된 오브젝트는 == null로 판정됨
            var go = new GameObject("CharacterLevelManager");
            go.AddComponent<CharacterLevelManager>(); // Awake에서 Instance 등록 + DontDestroyOnLoad
        }

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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>현재 레벨(저장된 값 없으면 Lv1).</summary>
        public int GetLevel(string characterId)
        {
            return characterId != null && _levels.TryGetValue(characterId, out int lv)
                ? CharacterLevelTable.ClampLevel(lv)
                : CharacterLevelTable.MinLevel;
        }

        public bool IsMaxLevel(string characterId) => GetLevel(characterId) >= CharacterLevelTable.MaxLevel;

        /// <summary>레벨업 가능 여부. 불가면 이유를 reason에 담음(UI 토스트용).</summary>
        public bool CanLevelUp(string characterId, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(characterId)) { reason = "알 수 없는 캐릭터"; return false; }

            if (CharacterUnlockManager.Instance == null || !CharacterUnlockManager.Instance.IsUnlocked(characterId))
            {
                reason = "해금되지 않은 캐릭터";
                return false;
            }

            int level = GetLevel(characterId);
            if (level >= CharacterLevelTable.MaxLevel) { reason = "최대 레벨"; return false; }

            var economy = EconomyManager.Instance;
            if (economy == null) { reason = "재화 매니저 없음"; return false; }

            int gold = CharacterLevelTable.GetGoldCostToNext(level);
            int medal = CharacterLevelTable.GetMedalCostToNext(level);
            if (economy.Gold < gold) { reason = "금화 부족"; return false; }
            if (economy.Medal < medal) { reason = "태극 휘장 부족"; return false; }
            return true;
        }

        public bool TryLevelUp(string characterId)
        {
            if (!CanLevelUp(characterId, out _)) return false;

            int level = GetLevel(characterId);
            int gold = CharacterLevelTable.GetGoldCostToNext(level);
            int medal = CharacterLevelTable.GetMedalCostToNext(level);

            var economy = EconomyManager.Instance;
            // CanLevelUp에서 잔액을 확인했으므로 두 번의 소모는 모두 성공해야 함. 금화가 0인 레벨(표 오류 등)이어도
            // TrySpend는 0 이하를 거절하므로 amount > 0일 때만 호출함.
            if (gold > 0 && !economy.TrySpendGold(gold)) return false;
            if (medal > 0 && !economy.TrySpendMedal(medal))
            {
                economy.AddGold(gold); // 휘장 소모에 실패하면 이미 낸 금화 환불
                return false;
            }

            int newLevel = level + 1;
            _levels[characterId] = newLevel;
            Save();
            OnLevelChanged?.Invoke(characterId, newLevel);
            return true;
        }

        /// <summary>PlayerLoadout.CaptureLocal이 쓰는 복사 - 내부 딕셔너리를 밖에 노출하지 않음.</summary>
        public void CopyLevelsTo(Dictionary<string, int> destination)
        {
            foreach (var kv in _levels) destination[kv.Key] = kv.Value;
        }

        [ContextMenu("Debug: Reset Levels")]
        public void DebugResetLevels()
        {
            _levels.Clear();
            Save();
            OnLevelChanged?.Invoke(null, CharacterLevelTable.MinLevel);
        }

        private void Load()
        {
            _levels.Clear();
            string raw = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;

            foreach (string pair in raw.Split(';'))
            {
                int colon = pair.LastIndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(pair.Substring(colon + 1), out int lv)) continue;
                _levels[pair.Substring(0, colon)] = CharacterLevelTable.ClampLevel(lv);
            }
        }

        private void Save()
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kv in _levels)
            {
                if (kv.Value <= CharacterLevelTable.MinLevel) continue; // Lv1은 기본값이라 저장 불필요
                if (!first) sb.Append(';');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
                first = false;
            }
            PlayerPrefs.SetString(SaveKey, sb.ToString());
        }
    }
}
