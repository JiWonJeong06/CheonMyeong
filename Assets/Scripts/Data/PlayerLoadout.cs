using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// PvP 매치에서 서버가 "이 플레이어의 캐릭터 레벨/노드 해금 상태"를 알아야 해서(레벨 차이는 PvP에 그대로
    /// 반영 - 엑셀 구현 규칙) 클라이언트가 매치 시작 때 서버로 보내는 스냅샷. 문자열 한 줄로 직렬화함:
    /// "char_1000:5;char_1004:12|nodeA,nodeB".
    /// [보안 메모] 지금은 클라이언트가 자기 값을 직접 보내는 임시 구조임 - 서버 저장/검증이 붙으면 서버가 계정의
    /// 실제 값을 직접 읽도록 교체할 것(RankManager와 같은 처지). 그래서 Unpack에서 레벨은 1~50으로 clamp함.
    /// </summary>
    public class PlayerLoadout
    {
        private const int MaxPackedLength = 8192; // 비정상적으로 큰 입력 방어

        public readonly Dictionary<string, int> levels = new();
        public readonly HashSet<string> unlockedNodeIds = new();

        public int GetLevel(string characterId)
        {
            return characterId != null && levels.TryGetValue(characterId, out int lv)
                ? CharacterLevelTable.ClampLevel(lv)
                : CharacterLevelTable.MinLevel;
        }

        /// <summary>이 기기의 현재 상태(CharacterLevelManager/TechTreeManager)로 스냅샷 생성. 매니저가 없으면 빈 값(전부 Lv1).</summary>
        public static PlayerLoadout CaptureLocal()
        {
            var loadout = new PlayerLoadout();
            CharacterLevelManager.Instance?.CopyLevelsTo(loadout.levels);
            TechTreeManager.Instance?.CopyUnlockedStatNodeIdsTo(loadout.unlockedNodeIds);
            return loadout;
        }

        public string Pack()
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kv in levels)
            {
                if (!first) sb.Append(';');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
                first = false;
            }
            sb.Append('|');
            first = true;
            foreach (string id in unlockedNodeIds)
            {
                if (!first) sb.Append(',');
                sb.Append(id);
                first = false;
            }
            return sb.ToString();
        }

        public static PlayerLoadout Unpack(string packed)
        {
            var loadout = new PlayerLoadout();
            if (string.IsNullOrEmpty(packed) || packed.Length > MaxPackedLength) return loadout;

            int bar = packed.IndexOf('|');
            string levelPart = bar >= 0 ? packed.Substring(0, bar) : packed;
            string nodePart = bar >= 0 ? packed.Substring(bar + 1) : string.Empty;

            foreach (string pair in levelPart.Split(';'))
            {
                int colon = pair.LastIndexOf(':');
                if (colon <= 0) continue;
                string id = pair.Substring(0, colon);
                if (!int.TryParse(pair.Substring(colon + 1), out int lv)) continue;
                loadout.levels[id] = CharacterLevelTable.ClampLevel(lv);
            }

            foreach (string id in nodePart.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(id)) loadout.unlockedNodeIds.Add(id);
            }
            return loadout;
        }
    }
}
