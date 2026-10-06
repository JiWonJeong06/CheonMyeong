using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 2500 박성훈 (지하문, 공속형, 근접)
    /// 스킬 [기절 강타] : n% 확률로 기본 공격이 1.75초 기절을 부여.  n = stunChancePercent
    /// 궁극기 [주작의 춤] : 맵을 가로지르는 주작 - 내 보드 몬스터 전체에 공격력의 n% 피해 + 화상(3초, 초당 최대 체력 3%, 보스 면역).  n = phoenixDamagePercent
    /// </summary>
    public sealed class ParkSeonghunSkill : CharacterSkill
    {
        private const float StunSeconds = 1.75f;
        private const float BurnSeconds = 3f;
        private const float BurnPercentPerSecond = 3f;

        public override void OnBasicHit(MonsterPathFollower target, float damage)
        {
            float chance = Skill("stunChancePercent");
            if (chance > 0f && Random.value * 100f < chance) target.ApplyStun(StunSeconds);
        }

        public override bool CanCastUltimate() => AnyEnemyOnBoard();

        public override void CastUltimate()
        {
            float damage = Tower.AttackPower * Ultimate("phoenixDamagePercent") / 100f;
            int n = Tower.CollectOwnBoardMonsters(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (!m.IsAlive) continue;
                Tower.DealDamage(m, damage);
                if (m.IsAlive) m.ApplyBurn(Tower, BurnPercentPerSecond, BurnSeconds);
            }
            Targets.Clear();
            SkillDebug.Log($"박성훈 주작의 춤: {n}마리, 피해 {damage:0.#}");
        }
    }
}
