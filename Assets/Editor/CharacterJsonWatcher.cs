using UnityEditor;
using UnityEngine;

namespace TowerDefense.Data.Import
{
    /// <summary>
    /// Characters.json이 (git pull 등으로) 바뀌어서 유니티가 재임포트할 때마다
    /// 자동으로 감지해서 "지금 바로 재임포트할지" 물어보는 훅.
    /// CI 없이 로컬 에디터에서만 동작함 — 유니티 에디터에 포커스가 돌아오는 순간 트리거됨.
    /// </summary>
    public class CharacterJsonWatcher : AssetPostprocessor
    {
        private const string WatchedPath = "Assets/Data/Characters.json";

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                if (path == WatchedPath)
                {
                    bool reimport = EditorUtility.DisplayDialog(
                        "Character Data",
                        "Characters.json이 변경된 걸 감지함.\n지금 바로 캐릭터 데이터를 재임포트할까?",
                        "재임포트",
                        "나중에");

                    if (reimport)
                    {
                        CharacterJsonImporter.ImportFromJson();
                    }
                    return;
                }
            }
        }
    }
}
