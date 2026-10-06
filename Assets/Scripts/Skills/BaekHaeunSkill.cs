using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 3501 백하은 (원거리)
    /// 스킬 [관통하는 화살] : 화살이 일직선상 모든 적을 관통, 지나갈 때마다 피해 n% 감소.  n = pierceFalloffPercent
    /// 궁극기 [화살비] : 6초 동안 사거리 내 적에게 초당 공격력 n% + 20% 감속.  n = arrowRainDamagePercent
    ///   (0.5초마다 절반씩 피해, 경로 위 사거리 내 몬스터가 대상)
    /// </summary>
    public sealed class BaekHaeunSkill : CharacterSkill
    {
        private const float RayHalfWidthCells = 0.3f;
        private const float RainSeconds = 6f;
        private const float RainTick = 0.5f;
        private const int SlowKey = 3501;
        private const float SlowPercent = 20f;

        private float _rainEnd;
        private float _nextTick;
        private readonly System.Collections.Generic.List<float> _proj = new(32);
        private readonly System.Collections.Generic.List<MonsterPathFollower> _hits = new(32);

        public override bool HandleBasicAttack(MonsterPathFollower target, float damage)
        {
            Vector3 origin = Tower.transform.position;
            Vector2 dir = SkillGeometry.DirectionXY(origin, target.Position);
            Vector3 end = origin + (Vector3)(dir * Tower.RangeWorld);
            float half = RayHalfWidthCells * Tower.CellSize;
            float halfSqr = half * half;

            int n = Tower.CollectOwnBoardMonstersInRange(Targets);
            // 직선에 걸린 몬스터를 타워에서 가까운 순(투영 거리)으로 삽입 정렬 - 소수라 충분
            _hits.Clear();
            _proj.Clear();
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (!m.IsAlive) continue;
                if (SkillGeometry.SqrDistanceToSegmentXY(m.Position, origin, end) > halfSqr) continue;
                float t = (m.Position.x - origin.x) * dir.x + (m.Position.y - origin.y) * dir.y;
                int j = _hits.Count;
                while (j > 0 && _proj[j - 1] > t) j--;
                _hits.Insert(j, m);
                _proj.Insert(j, t);
            }
            float keep = 1f - Skill("pierceFalloffPercent") / 100f;
            float mult = 1f;
            for (int k = 0; k < _hits.Count; k++)
            {
                var m = _hits[k];
                if (m.IsAlive) Tower.DealDamage(m, damage * mult);
                mult *= keep;
            }
            _hits.Clear();
            Targets.Clear();
            _proj.Clear();
            return true;
        }

        public override void OnTick(float deltaTime)
        {
            float now = Time.time;
            if (now >= _rainEnd || now < _nextTick) return;
            _nextTick = now + RainTick;

            float damage = Tower.AttackPower * Ultimate("arrowRainDamagePercent") / 100f * RainTick;
            int n = Tower.CollectOwnBoardMonstersInRange(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (!m.IsAlive) continue;
                m.ApplySlow(SlowKey, SlowPercent, RainTick + 0.3f); // 틱 사이 끊기지 않게 겹침
                Tower.DealDamage(m, damage);
            }
            Targets.Clear();
        }

        public override bool CanCastUltimate() => AnyEnemyInRange();

        public override void CastUltimate()
        {
            _rainEnd = Time.time + RainSeconds;
            _nextTick = 0f;
            Tower.PlayZoneEffectRpc(Tower.transform.position, Tower.RangeWorld, RainSeconds, new Color(0.4f, 0.7f, 1f, 0.25f));
            SkillDebug.Log($"백하은 화살비: {RainSeconds}초, 초당 {Ultimate("arrowRainDamagePercent"):0}%");
        }
    }
}
