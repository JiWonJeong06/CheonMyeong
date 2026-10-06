using UnityEngine;
using TowerDefense.Map;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 3502 천희재 (버프형, 근접)
    /// 스킬 [수입] : 30초마다 10초 동안 내 SP 수입 n% 증가.  n = spIncomeBonusPercent
    /// 궁극기 [사기 증가] : 9초 동안 아군 모든 근접 캐릭터 공격력 n% 증가 + 사거리 +1.  n = meleeAttackBonusPercent
    /// </summary>
    public sealed class CheonHuijaeSkill : SupportCharacterSkill
    {
        private const float CycleSeconds = 30f;
        private const float IncomeSeconds = 10f;
        private const float MoraleSeconds = 9f;

        private float _nextIncome;
        private readonly System.Collections.Generic.List<TowerUnit> _allies = new(16);

        protected override void OnBind() => _nextIncome = Time.time + CycleSeconds;

        protected override void OnUnbind()
        {
            var res = Tower != null ? Tower.OwnBoardResources : null;
            if (res != null) res.SetIncomeBonus(Tower, 0f, 0f);
        }

        protected override void OnSupportTick(float deltaTime)
        {
            float now = Time.time;
            if (now < _nextIncome) return;
            _nextIncome = now + CycleSeconds;

            var res = Tower.OwnBoardResources;
            if (res != null) res.SetIncomeBonus(Tower, Skill("spIncomeBonusPercent"), IncomeSeconds);
            GrantSkillUseGauge(CycleSeconds);
        }

        public override void CastUltimate()
        {
            float percent = Ultimate("meleeAttackBonusPercent");
            int n = Tower.CollectAllies(_allies, true);
            int buffed = 0;
            for (int i = 0; i < n; i++)
            {
                var ally = _allies[i];
                if (ally == null || !ally.IsMelee) continue;
                ally.SetBuff(this, new StatBuff(damagePercent: percent, rangeBonus: 1f), MoraleSeconds);
                buffed++;
            }
            _allies.Clear();
            SkillDebug.Log($"천희재 사기 증가: 근접 {buffed}명, 공격력 +{percent:0.#}%, 사거리 +1, {MoraleSeconds}초");
        }
    }
}
