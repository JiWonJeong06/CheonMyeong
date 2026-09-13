using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using TowerDefense.Data;

namespace TowerDefense.Data.Import
{
    /// <summary>
    /// Assets/Data/Characters.json 을 읽어서 Assets/Resources/CharacterData/Generated/ 밑에
    /// CharacterDataSO 애셋을 자동으로 생성/갱신하는 에디터 전용 툴.
    ///
    /// 사용법: 상단 메뉴 Tools > Character Data > Import From JSON
    ///
    /// 주의 (CharacterDataSO에 적힌 것과 동일) — 생성된 SO 애셋 필드를 인스펙터에서
    /// 직접 손으로 고치지 말 것. 항상 Characters.json을 고친 다음 재임포트할 것.
    /// </summary>
    public static class CharacterJsonImporter
    {
        private const string JsonPath = "Assets/Data/Characters.json";

        // Resources 폴더 밑에 둬야 빌드된 게임에서 Resources.LoadAll로 런타임에 긁어올 수 있음
        // (에디터 전용인 AssetDatabase 경로는 빌드에 포함 안 되니까 여기 두면 안 됨).
        // CharacterDatabase.cs가 이 경로를 Resources 상대경로("CharacterData/Generated")로 그대로 읽음.
        private const string OutputFolder = "Assets/Resources/CharacterData/Generated";

        [MenuItem("Tools/Character Data/Import From JSON")]
        public static void ImportFromJson()
        {
            if (!File.Exists(JsonPath))
            {
                EditorUtility.DisplayDialog(
                    "Character Data Import",
                    $"{JsonPath} 파일을 찾을 수 없음.",
                    "확인");
                return;
            }

            string jsonText = File.ReadAllText(JsonPath);

            CharacterJsonFile parsed;
            try
            {
                parsed = JsonUtility.FromJson<CharacterJsonFile>(jsonText);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(
                    "Character Data Import",
                    $"JSON 파싱 실패:\n{e.Message}\n\n" +
                    "중괄호/대괄호가 안 맞았거나, 최상위가 {{ \"characters\": [...] }} 형태가 아닐 수 있음.",
                    "확인");
                return;
            }

            if (parsed == null || parsed.characters == null || parsed.characters.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Character Data Import",
                    "characters 배열이 비어있거나 최상위 \"characters\" 키를 찾을 수 없음.",
                    "확인");
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                EnsureFolder(OutputFolder);
            }

            int created = 0;
            int updated = 0;

            foreach (var entry in parsed.characters)
            {
                if (string.IsNullOrWhiteSpace(entry.characterId))
                {
                    Debug.LogWarning("[CharacterJsonImporter] characterId가 빈 항목이 있어서 건너뜀.");
                    continue;
                }

                string assetPath = $"{OutputFolder}/{entry.characterId}.asset";
                var so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(assetPath);

                bool isNew = so == null;
                if (isNew)
                {
                    so = ScriptableObject.CreateInstance<CharacterDataSO>();
                }

                ApplyEntryToAsset(entry, so);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(so, assetPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(so);
                    updated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Character Data Import",
                $"임포트 완료.\n생성: {created}개 / 갱신: {updated}개",
                "확인");
        }

        private static void ApplyEntryToAsset(CharacterJsonEntry entry, CharacterDataSO so)
        {
            so.characterId = entry.characterId;
            so.displayName = entry.displayName;
            so.description = entry.description;
            so.characterTag = entry.characterTag;

            so.baseDamage = entry.baseDamage;
            so.range = entry.range;
            so.attackSpeed = entry.attackSpeed;
            so.summonCost = entry.summonCost;

            so.unlockCost = entry.unlockCost;
            so.unlockCurrency = ParseCurrency(entry.unlockCurrency);

            // 아이콘/포트레이트 스프라이트는 JSON에 안 넣음 (이미지 애셋 참조는 텍스트로 못 담으니
            // 기존처럼 인스펙터에서 직접 드래그해서 연결하는 영역으로 남겨둠 — 재임포트해도 안 건드림).

            so.skills = entry.skills ?? new System.Collections.Generic.List<SkillData>();
        }

        private static CurrencyType ParseCurrency(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                Debug.LogWarning("[CharacterJsonImporter] unlockCurrency가 비어있어서 Gold로 기본 처리함.");
                return CurrencyType.Gold;
            }

            string trimmed = raw.Trim();
            if (trimmed.Equals("Gem", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyType.Gem;
            }
            if (trimmed.Equals("Gold", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyType.Gold;
            }

            Debug.LogWarning($"[CharacterJsonImporter] unlockCurrency 값 \"{raw}\"을(를) 인식 못 해서 Gold로 기본 처리함. " +
                              "\"Gold\" 또는 \"Gem\"만 허용됨.");
            return CurrencyType.Gold;
        }

        private static void EnsureFolder(string fullPath)
        {
            // "Assets/Resources/CharacterData/Generated" 같은 경로를 한 단계씩 생성.
            string[] parts = fullPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
