using System.Collections.Generic;

namespace TowerDefense.Data
{
    /// <summary>
    /// 캐릭터 레벨 + 노드 트리 효과로 "노드 계층" 배율(TowerStatModifiers)을 계산하는 공용 계산기.
    /// 서버(MatchController - 배치한 플레이어의 PlayerLoadout 기준)와 UI(노드 상세창 - 이 기기 상태 기준)가
    /// 같은 식을 쓰도록 한 곳으로 모음 - 식이 두 군데 있으면 화면 숫자와 실제 전투 숫자가 어긋남.
    /// 무소속(4000~)은 소속 효과를 절대 받지 않고 전체 공격력 효과만 받음(엑셀 구현 규칙).
    /// </summary>
    public static class NodeBonusCalculator
    {
        /// <param name="unlockedOverride">null이면 이 기기의 TechTreeManager 해금 상태, 아니면 그 집합(예: 서버가 받은 로드아웃) 기준.</param>
        public static TowerStatModifiers Compute(CharacterDataSO character, int level, HashSet<string> unlockedOverride = null)
        {
            var tree = TechTreeManager.Instance;
            if (character == null || tree == null) return TowerStatModifiers.Compute(level, 0f, 0f);

            float attackPercent = tree.GetBonusPercent(NodeEffectType.GlobalAttackPercent, null, unlockedOverride);
            float speedPercent = 0f;
            if (NodeTreeLines.ReceivesLineEffect(character.code))
            {
                string line = NodeTreeLines.GetLine(character.code);
                attackPercent += tree.GetBonusPercent(NodeEffectType.LineAttackPercent, line, unlockedOverride);
                speedPercent += tree.GetBonusPercent(NodeEffectType.LineAttackSpeedPercent, line, unlockedOverride);
            }
            return TowerStatModifiers.Compute(level, attackPercent, speedPercent);
        }
    }
}
