using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>캐릭터 레벨 표의 한 줄 (엑셀 '캐릭터 레벨' 시트 1행 = Resources/Data/CharacterLevelTable.json 1항목).</summary>
    [Serializable]
    public class CharacterLevelEntry
    {
        public int level;
        public float attackMultiplier;   // Lv1 대비 공격력 배율
        public float intervalMultiplier; // Lv1 대비 공격 간격 배율 (낮을수록 빠름)
        public bool isBreakthrough;      // 돌파 레벨(10/20/30/40) - 다음 레벨로 가려면 금화 + 태극 휘장
        public int goldToNext;           // 이 레벨 → 다음 레벨 금화 (최대 레벨은 0) [엑셀: 임시 제안값]
        public int medalToNext;          // 돌파 레벨에서 필요한 태극 휘장 (돌파 아니면 0) [엑셀: 임시 제안값]
    }

    [Serializable]
    public class CharacterLevelTableFile
    {
        public List<CharacterLevelEntry> entries;
    }

    /// <summary>
    /// 캐릭터 레벨(노드 트리, Lv1~50) 표 조회. 엑셀 수치는 모두 Lv1 기준이고, 레벨이 오르면 공격력은
    /// 올라가고 공격 간격은 줄어듦(Lv50 = 공격력 ×2, 간격 ×0.8 → DPS 2.5배).
    /// 데이터는 첫 조회 때 한 번만 읽어 배열에 보관하고(읽은 TextAsset은 바로 언로드), 이후엔 배열 인덱싱만
    /// 하므로 매 공격마다 불러도 할당이 없음. 값은 변하지 않는 읽기 전용 데이터라 정리할 것도 없음.
    /// </summary>
    public static class CharacterLevelTable
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 50;

        private const string ResourcePath = "Data/CharacterLevelTable"; // 확장자 제외, Resources 기준

        private static CharacterLevelEntry[] _byLevel; // 인덱스 = level - 1
        private static bool _loadAttempted;

        public static int ClampLevel(int level) => Mathf.Clamp(level, MinLevel, MaxLevel);

        public static float GetAttackMultiplier(int level)
        {
            var e = Get(level);
            return e != null ? e.attackMultiplier : 1f;
        }

        public static float GetIntervalMultiplier(int level)
        {
            var e = Get(level);
            return e != null ? e.intervalMultiplier : 1f;
        }

        public static bool IsBreakthroughLevel(int level)
        {
            var e = Get(level);
            return e != null && e.isBreakthrough;
        }

        /// <summary>이 레벨에서 다음 레벨로 올리는 금화 비용. 최대 레벨이거나 표가 없으면 0.</summary>
        public static int GetGoldCostToNext(int level)
        {
            if (ClampLevel(level) >= MaxLevel) return 0;
            var e = Get(level);
            return e != null ? e.goldToNext : 0;
        }

        /// <summary>이 레벨에서 다음 레벨로 올리는 태극 휘장 비용(돌파 레벨에서만 > 0).</summary>
        public static int GetMedalCostToNext(int level)
        {
            if (ClampLevel(level) >= MaxLevel) return 0;
            var e = Get(level);
            return e != null ? e.medalToNext : 0;
        }

        private static CharacterLevelEntry Get(int level)
        {
            EnsureLoaded();
            if (_byLevel == null) return null;
            return _byLevel[ClampLevel(level) - 1];
        }

        private static void EnsureLoaded()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[CharacterLevelTable] {ResourcePath}.json을 Resources에서 못 찾음 - 레벨 배율 없이(전부 ×1) 동작함.");
                return;
            }

            CharacterLevelTableFile parsed;
            try
            {
                parsed = JsonUtility.FromJson<CharacterLevelTableFile>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterLevelTable] 파싱 실패: {e.Message}");
                Resources.UnloadAsset(asset);
                return;
            }
            Resources.UnloadAsset(asset); // 파싱이 끝났으니 원본 텍스트는 메모리에 둘 필요 없음

            if (parsed?.entries == null || parsed.entries.Count != MaxLevel)
            {
                Debug.LogError($"[CharacterLevelTable] 항목 수가 {MaxLevel}개가 아님({parsed?.entries?.Count ?? 0}) - 무시함.");
                return;
            }

            var table = new CharacterLevelEntry[MaxLevel];
            foreach (var entry in parsed.entries)
            {
                if (entry.level < MinLevel || entry.level > MaxLevel)
                {
                    Debug.LogError($"[CharacterLevelTable] 잘못된 레벨 {entry.level} - 표 전체를 무시함.");
                    return;
                }
                table[entry.level - 1] = entry;
            }
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i] == null)
                {
                    Debug.LogError($"[CharacterLevelTable] Lv{i + 1} 항목이 없음 - 표 전체를 무시함.");
                    return;
                }
            }
            _byLevel = table;
        }
    }
}
