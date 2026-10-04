using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TowerDefense.Data;

namespace TowerDefense.Data.Import
{
    /// <summary>
    /// Assets/Data/Characters.json 을 읽어서 Assets/Resources/CharacterData/Generated/ 밑에
    /// CharacterDataSO 애셋을, Assets/Resources/CharacterData/ 밑에 전역 설정(CharacterGlobalConfigSO)을
    /// 자동으로 생성/갱신하는 에디터 전용 툴.
    ///
    /// 사용법: 상단 메뉴 Tools > Character Data > Import From JSON
    ///
    /// - 이미 있는 애셋은 새로 만들지 않고 값만 덮어씀(iconSprite/portraitSprite 연결은 유지됨).
    /// - JSON에 없는데 Generated 폴더에 남아 있는 애셋(예전 더미 등)은 목록을 보여주고 삭제 여부를 물음 -
    ///   그냥 두면 CharacterDatabase.GetAll()에 섞여서 보관함에 계속 나옴.
    /// - 데이터 필드는 인스펙터에서 손으로 고치지 말 것. 항상 JSON을 고친 뒤 재임포트.
    /// </summary>
    public static class CharacterJsonImporter
    {
        private const string JsonPath = "Assets/Data/Characters.json";

        // Resources 폴더 밑에 둬야 빌드된 게임에서 Resources.LoadAll로 읽을 수 있음.
        // CharacterDatabase.cs가 이 경로를 Resources 상대경로로 그대로 읽음.
        private const string OutputFolder = "Assets/Resources/CharacterData/Generated";
        private const string ConfigPath = "Assets/Resources/CharacterData/CharacterGlobalConfig.asset";

        // 시작 캐릭터 5명(기획: 하영·이유라 / 김도현 / 윤재훈 / 박성훈) - 처음부터 보유.
        private static readonly HashSet<int> StarterCodes = new() { 1000, 1004, 1500, 2000, 2500 };

        // [임시] 비시작 캐릭터 해금 비용. 해금 구조(노드 트리)가 정해지면 교체/삭제.
        private const int PlaceholderUnlockCost = 100;

        [MenuItem("Tools/Character Data/Import From JSON")]
        public static void ImportFromJson()
        {
            if (!File.Exists(JsonPath))
            {
                EditorUtility.DisplayDialog("Character Data Import", $"{JsonPath} 파일을 찾을 수 없음.", "확인");
                return;
            }

            CharacterJsonFile parsed;
            try
            {
                parsed = JsonUtility.FromJson<CharacterJsonFile>(File.ReadAllText(JsonPath));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Character Data Import",
                    $"JSON 파싱 실패:\n{e.Message}\n\n중괄호/대괄호가 안 맞았거나, 최상위가 {{ \"characters\": [...] }} 형태가 아닐 수 있음.", "확인");
                return;
            }

            if (parsed == null || parsed.characters == null || parsed.characters.Count == 0)
            {
                EditorUtility.DisplayDialog("Character Data Import",
                    "characters 배열이 비어있거나 최상위 \"characters\" 키를 찾을 수 없음.", "확인");
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder)) EnsureFolder(OutputFolder);

            int expectedLevels = parsed.maxSkillLevel > 0 ? parsed.maxSkillLevel : 5;
            var importedIds = new HashSet<string>();
            int created = 0, updated = 0, warnings = 0;

            foreach (var entry in parsed.characters)
            {
                if (entry.code <= 0)
                {
                    Debug.LogWarning($"[CharacterJsonImporter] code가 없는 항목('{entry.name}')이 있어서 건너뜀.");
                    warnings++;
                    continue;
                }

                string id = $"char_{entry.code}";
                if (!importedIds.Add(id))
                {
                    Debug.LogWarning($"[CharacterJsonImporter] code {entry.code}가 JSON에 중복돼 있어서 두 번째 항목은 건너뜀.");
                    warnings++;
                    continue;
                }

                string assetPath = $"{OutputFolder}/{id}.asset";
                var so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(assetPath);
                bool isNew = so == null;
                if (isNew) so = ScriptableObject.CreateInstance<CharacterDataSO>();

                warnings += ApplyEntryToAsset(entry, id, expectedLevels, so);

                if (isNew) { AssetDatabase.CreateAsset(so, assetPath); created++; }
                else { EditorUtility.SetDirty(so); updated++; }
            }

            UpsertGlobalConfig(parsed);

            // JSON에 없는 예전 애셋 정리(삭제 전에 반드시 확인).
            int removed = HandleStaleAssets(importedIds);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            CharacterDatabase.InvalidateCache();

            EditorUtility.DisplayDialog("Character Data Import",
                $"임포트 완료 (데이터 버전 {parsed.version}).\n생성: {created}개 / 갱신: {updated}개 / 삭제: {removed}개\n경고: {warnings}건" +
                (warnings > 0 ? " (Console 확인)" : ""), "확인");
        }

        // 반환값: 경고 수
        private static int ApplyEntryToAsset(CharacterJsonEntry e, string id, int expectedLevels, CharacterDataSO so)
        {
            int warn = 0;

            so.code = e.code;
            so.characterId = id;
            so.displayName = e.name;
            so.faction = FactionFromCode(e.code);
            so.weapon = e.weapon;
            so.attackType = ParseEnum(e.attackType, CharacterAttackType.Melee, id, "attackType", ref warn);
            so.isSupport = e.isSupport;
            so.mergeType = ParseEnum(e.mergeType, MergeType.Power, id, "mergeType", ref warn);
            so.targetPriority = ParseEnum(e.targetPriority, TargetPriority.Front, id, "targetPriority", ref warn);

            so.baseDamage = e.attackPower;
            so.attackInterval = e.attackInterval;
            so.range = e.range;
            so.starAttackPower = e.starAttackPower;
            so.starAttackInterval = e.starAttackInterval;

            so.gaugePerHit = e.gaugePerHit;
            so.killType = ParseEnum(e.killType, KillType.Normal, id, "killType", ref warn);
            so.gaugePerKill = e.gaugePerKill;
            so.ultimateGauge = e.ultimateGauge;

            so.skill = e.skill ?? new SkillDefinition();
            so.ultimate = e.ultimate ?? new SkillDefinition();

            if (e.attackInterval <= 0f)
            {
                Debug.LogWarning($"[CharacterJsonImporter] {id}({e.name}) attackInterval이 0 이하({e.attackInterval}) - 공격이 안 나감.");
                warn++;
            }

            warn += CheckLevels(id, e.name, "skill", so.skill, expectedLevels);
            warn += CheckLevels(id, e.name, "ultimate", so.ultimate, expectedLevels);

            // 해금: 시작 캐릭터는 보유, 나머지는 [임시] 비용.
            so.isStarter = StarterCodes.Contains(e.code);
            so.unlockCost = so.isStarter ? 0 : PlaceholderUnlockCost;
            so.unlockCurrency = CurrencyType.Gold;

            // iconSprite/portraitSprite는 JSON에 없으므로 건드리지 않음(인스펙터 연결 유지).
            return warn;
        }

        private static int CheckLevels(string id, string charName, string label, SkillDefinition def, int expected)
        {
            int warn = 0;
            if (def.parameters == null) return 0;
            foreach (var p in def.parameters)
            {
                int len = p.values != null ? p.values.Length : 0;
                if (len != expected)
                {
                    Debug.LogWarning($"[CharacterJsonImporter] {id}({charName}) {label} 변수 '{p.key}' 값이 {len}개 (기대 {expected}개 = 강화 Lv1~{expected}).");
                    warn++;
                }
            }
            return warn;
        }

        private static void UpsertGlobalConfig(CharacterJsonFile parsed)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<CharacterGlobalConfigSO>(ConfigPath);
            bool isNew = cfg == null;
            if (isNew) cfg = ScriptableObject.CreateInstance<CharacterGlobalConfigSO>();

            cfg.dataVersion = parsed.version;
            cfg.startSkillLevel = parsed.startSkillLevel > 0 ? parsed.startSkillLevel : 1;
            cfg.maxSkillLevel = parsed.maxSkillLevel > 0 ? parsed.maxSkillLevel : 5;
            cfg.maxStar = parsed.maxStar > 0 ? parsed.maxStar : 5;
            cfg.supportUltimateCharge = parsed.supportUltimateCharge ?? new SupportUltimateChargeConfig();

            if (isNew) AssetDatabase.CreateAsset(cfg, ConfigPath);
            else EditorUtility.SetDirty(cfg);
        }

        // Generated 폴더에서 JSON에 없는 CharacterDataSO를 찾아 삭제 여부를 물음. 반환값: 삭제 수.
        private static int HandleStaleAssets(HashSet<string> importedIds)
        {
            var stalePaths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterDataSO", new[] { OutputFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(path);
                if (so != null && !importedIds.Contains(so.characterId)) stalePaths.Add(path);
            }
            if (stalePaths.Count == 0) return 0;

            bool delete = EditorUtility.DisplayDialog("Character Data Import",
                $"JSON에 없는 예전 캐릭터 애셋 {stalePaths.Count}개가 남아 있음:\n" +
                string.Join("\n", stalePaths) +
                "\n\n삭제할까? (남겨두면 보관함 등에 계속 표시됨)", "삭제", "남겨두기");
            if (!delete) return 0;

            int removed = 0;
            foreach (string p in stalePaths)
            {
                if (AssetDatabase.DeleteAsset(p)) removed++;
            }
            return removed;
        }

        // 엑셀 코드 체계(GDD 1.3 / 엑셀 '캐릭터' 시트): 1000대 천명회 ~ 3500대 화랑회, 4000~ 무소속.
        private static string FactionFromCode(int code)
        {
            if (code >= 4000) return "무소속";
            if (code >= 3500) return "화랑회";
            if (code >= 3000) return "풍류단";
            if (code >= 2500) return "지하문";
            if (code >= 2000) return "비선청";
            if (code >= 1500) return "흑연회";
            return "천명회";
        }

        private static T ParseEnum<T>(string raw, T fallback, string id, string field, ref int warn) where T : struct, Enum
        {
            if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse(raw.Trim(), true, out T result)) return result;
            Debug.LogWarning($"[CharacterJsonImporter] {id} {field} 값 \"{raw}\"을(를) 인식 못 해서 {fallback}로 처리함.");
            warn++;
            return fallback;
        }

        private static void EnsureFolder(string fullPath)
        {
            string[] parts = fullPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
