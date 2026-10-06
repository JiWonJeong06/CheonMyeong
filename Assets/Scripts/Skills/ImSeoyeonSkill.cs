using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 4001 임서연 (원거리, 체력 높은 순)
    /// 스킬 [심안] : 레이저 공격력 n%→m%로 10초에 걸쳐 증가. 타겟이 바뀌면 초기화.  n = laserStartPercent, m = laserMaxPercent
    /// 궁극기 [심안 강화] : n초 동안 타겟이 바뀌어도 초기화되지 않음.  n = noResetSeconds
    /// </summary>
    public sealed class ImSeoyeonSkill : CharacterSkill
    {
        private const float RampSeconds = 10f;

        private MonsterPathFollower _lockTarget;
        private int _lockSerial;
        private float _lockStart;
        private float _noResetEnd;

        protected override void OnUnbind() => _lockTarget = null;

        public override bool HandleBasicAttack(MonsterPathFollower target, float damage)
        {
            float now = Time.time;
            bool same = target == _lockTarget && target.SpawnSerial == _lockSerial;
            if (!same)
            {
                // 첫 락온이거나 궁극기 중이 아니면 초기화. 궁극기 중엔 진행도 유지(대상만 교체).
                if (_lockTarget == null || now >= _noResetEnd) _lockStart = now;
                _lockTarget = target;
                _lockSerial = target.SpawnSerial;
            }

            float t = Mathf.Clamp01((now - _lockStart) / RampSeconds);
            float percent = Mathf.Lerp(Skill("laserStartPercent"), Skill("laserMaxPercent"), t);
            Tower.DealDamage(target, damage * percent / 100f);
            return true;
        }

        public override bool CanCastUltimate() => AnyEnemyInRange();

        public override void CastUltimate()
        {
            _noResetEnd = Time.time + Ultimate("noResetSeconds");
            SkillDebug.Log($"임서연 심안 강화: {Ultimate("noResetSeconds"):0.#}초");
        }
    }
}
