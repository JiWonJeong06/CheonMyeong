using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 4000 강민준 (근접)
    /// 스킬 [수증기 대검] : 공격할 때마다 열기 1스택, 10스택이 되면 폭발 - 대상에 공격력 n%, 주변(반경 1칸)에 n×0.4%.  n = explosionDamagePercent
    /// 궁극기 [수증기 폭발] : 사거리 내 전체에 공격력 n%, 열기 6스택 획득, 12초 동안 공격마다 스택 2씩.  n = steamDamagePercent
    /// </summary>
    public sealed class KangMinjunSkill : CharacterSkill
    {
        private const int ExplodeStacks = 10;
        private const int UltimateStacks = 6;
        private const float UltimateSeconds = 12f;
        private const float SplashRadiusCells = 1f;
        private const float SplashRatio = 0.4f;

        private int _heat;
        private float _ultimateEnd;

        public override void OnAttackStart(MonsterPathFollower target)
        {
            _heat += Time.time < _ultimateEnd ? 2 : 1;
            TryExplode(target);
        }

        private void TryExplode(MonsterPathFollower center)
        {
            if (_heat < ExplodeStacks || center == null || !center.IsAlive) return;
            _heat -= ExplodeStacks;

            float baseDamage = Tower.AttackPower * Skill("explosionDamagePercent") / 100f;
            Vector3 pos = center.Position;
            float radius = SplashRadiusCells * Tower.CellSize;
            float rSqr = radius * radius;

            int n = Tower.CollectOwnBoardMonsters(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (m == center || !m.IsAlive) continue;
                if (SkillGeometry.SqrDistXY(m.Position, pos) <= rSqr) Tower.DealDamage(m, baseDamage * SplashRatio);
            }
            Targets.Clear();
            Tower.DealDamage(center, baseDamage);
            Tower.PlayZoneEffectRpc(pos, radius, 0.3f, new Color(0.9f, 0.9f, 1f, 0.4f));
        }

        public override bool CanCastUltimate() => AnyEnemyInRange();

        public override void CastUltimate()
        {
            float damage = Tower.AttackPower * Ultimate("steamDamagePercent") / 100f;
            MonsterPathFollower last = null;
            int n = Tower.CollectOwnBoardMonstersInRange(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (!m.IsAlive) continue;
                Tower.DealDamage(m, damage);
                if (m.IsAlive) last = m;
            }
            Targets.Clear();
            Tower.PlayZoneEffectRpc(Tower.transform.position, Tower.RangeWorld, 0.4f, new Color(1f, 1f, 1f, 0.35f));

            _ultimateEnd = Time.time + UltimateSeconds;
            _heat += UltimateStacks;
            // 폭발은 다음 공격에서 대상이 정해질 때 처리(스택은 이월). 생존 몬스터가 있으면 바로 처리.
            if (last != null) TryExplode(last);
            SkillDebug.Log($"강민준 수증기 폭발: {Ultimate("steamDamagePercent"):0}%, 열기 {_heat}");
        }
    }
}
