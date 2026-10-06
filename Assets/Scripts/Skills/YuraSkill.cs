using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 1000 이유라 (천명회, 공속형, 근접)
    /// 스킬 [처단] : 기본 지속 효과 - 공격력 n% 증가.  n = attackBonusPercent (강화로 오름 → OnProgressionChanged에서 다시 걸음)
    /// 궁극기 [필사즉생] : 12초 동안 이유라가 타격한 대상의 체력이 n% 이하이면 처형(즉사, 보스 제외).  n = executeHpPercent
    /// </summary>
    public sealed class YuraSkill : CharacterSkill
    {
        private const float UltimateDurationSeconds = 12f; // 엑셀 설명에 고정값으로 적힘(강화로 안 변함)

        private float _executeEndTime = -1f; // Time.time 기준 처형 상태 종료 시각

        protected override void OnBind() => ApplyPassive();
        public override void OnProgressionChanged() => ApplyPassive();
        protected override void OnUnbind() => Tower.RemoveBuff(this);

        // 같은 키(this)로 다시 걸면 값만 갱신됨
        private void ApplyPassive() => Tower.SetBuff(this, new StatBuff(damagePercent: Skill("attackBonusPercent")));

        public override void OnBasicHit(MonsterPathFollower target, float damage)
        {
            if (Time.time >= _executeEndTime) return;   // 궁극기 지속 중에만
            if (target.IsBoss) return;                  // 보스 제외

            float threshold = target.MaxHp * Ultimate("executeHpPercent") / 100f;
            if (target.CurrentHp <= threshold) Tower.Execute(target);
        }

        public override void CastUltimate()
        {
            _executeEndTime = Time.time + UltimateDurationSeconds; // 지속 중 다시 발동하면 12초로 갱신
            SkillDebug.Log($"이유라 필사즉생 발동: {UltimateDurationSeconds}초, 처형 체력 {Ultimate("executeHpPercent"):0}% 이하");
        }
    }
}
