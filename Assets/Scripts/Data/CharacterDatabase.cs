using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// 런타임에서 전체 캐릭터 목록을 가져오는 창구. CharacterJsonImporter가 만드는 애셋들이
    /// Resources/CharacterData/Generated 밑에 있어야 여기서 찾을 수 있음(에디터의 AssetDatabase는
    /// 빌드에 안 들어가서 못 씀). 보관함, 테크트리 등 "전체 캐릭터 목록이 필요한 화면"은
    /// 전부 이 클래스를 통해서만 캐릭터 데이터를 가져올 것 — Resources 경로를 직접 하드코딩하지 말 것.
    /// </summary>
    public static class CharacterDatabase
    {
        private const string ResourcesFolder = "CharacterData/Generated";

        private static List<CharacterDataSO> _cache;

        public static IReadOnlyList<CharacterDataSO> GetAll()
        {
            if (_cache == null)
            {
                var loaded = Resources.LoadAll<CharacterDataSO>(ResourcesFolder);
                _cache = new List<CharacterDataSO>(loaded);
                _cache.Sort((a, b) => string.CompareOrdinal(a.characterId, b.characterId));

                if (_cache.Count == 0)
                {
                    Debug.LogWarning("[CharacterDatabase] Resources/CharacterData/Generated 밑에서 캐릭터를 하나도 못 찾음 - " +
                                      "Tools > Character Data > Import From JSON을 먼저 실행했는지 확인할 것.");
                }
            }
            return _cache;
        }

        public static CharacterDataSO GetById(string characterId)
        {
            foreach (var data in GetAll())
            {
                if (data.characterId == characterId) return data;
            }
            return null;
        }

        // 에디터에서 재임포트한 뒤 플레이 모드를 다시 들어가지 않고 바로 반영해보고 싶을 때 쓰는 캐시 무효화.
        // 런타임 빌드에서는 딱히 호출할 일 없음(씬 로드마다 static 캐시가 초기화되진 않으니 필요하면 직접 호출).
        public static void InvalidateCache() => _cache = null;
    }
}
