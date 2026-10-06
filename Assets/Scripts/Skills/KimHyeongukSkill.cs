using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 3000 김형욱 (풍류단, 공속형, 근접)
    /// 스킬 [내려찍기] : 공격할 때마다 대상 주변(반경 1칸) 적에게 공격력의 n% 피해.  n = splashDamagePercent
    /// 궁극기 [불도끼] : 8초 동안 평타 대상에게 화상(3초, 초당 최대 체력 n%, 보스 면역).  n = burnPercentPerSec
    ///   (화상은 평타로 맞은 대상에게만 부여하고 내려찍기 광역에는 부여하지 않음)
    /// </summary>
    public sealed class KimHyeongukSkill : CharacterSkill
    {
        private const float SplashRadiusCells = 1f;
        private const float UltimateSeconds = 8f;
        private const float BurnSeconds = 3f;

        private float _ultimateEnd;

        public override void OnAttackStart(MonsterPathFollower target)
        {
            float damage = Tower.AttackPower * Skill("splashDamagePercent") / 100f;
            Vector3 center = target.Position;
            float radius = SplashRadiusCells * Tower.CellSize;
            float rSqr = radius * radius;

            int n = Tower.CollectOwnBoardMonsters(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (m == target || !m.IsAlive) continue;
                if (SkillGeometry.SqrDistXY(m.Position, center) <= rSqr) Tower.DealDamage(m, damage);
            }
            Targets.Clear();
        }

        public override void OnBasicHit(MonsterPathFollower target, float damage)
        {
            if (Time.time < _ultimateEnd) target.ApplyBurn(Tower, Ultimate("burnPercentPerSec"), BurnSeconds);
        }

        public override bool CanCastUltimate() => AnyEnemyInRange();

        public override void CastUltimate()
        {
            _ultimateEnd = Time.time + UltimateSeconds;
            SkillDebug.Log($"김형욱 불도끼: {UltimateSeconds}초, 화상 {Ultimate("burnPercentPerSec"):0.#}%/초");
        }
    }
}
