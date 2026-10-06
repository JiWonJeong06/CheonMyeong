using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 2502 정다희 (지하문, 공속형, 원거리)
    /// 스킬 [표창] : 평타 = 일자로 날아가는 표창 3개, 각각 공격력의 n%.  n = shurikenDamagePercent (단일 대상에 3타 - 패시브 딜 배율 1.5 = 3 × 50%)
    /// 궁극기 [부채꼴] : 11초 동안 평타가 부채꼴로 날아가는 표창 6개, 각각 공격력의 n%.  n = fanShurikenDamagePercent
    ///   표창 하나는 자기 직선(폭 0.3칸) 위의 몬스터 전부에 명중하며, 한 몬스터가 여러 표창에 맞을 수 있음.
    /// </summary>
    public sealed class JungDaheeSkill : CharacterSkill
    {
        private const int ShurikenCount = 3;
        private const int FanCount = 6;
        private const float FanAngleStep = 15f;
        private const float RayHalfWidthCells = 0.3f;
        private const float UltimateSeconds = 11f;

        private float _fanEnd;

        public override bool HandleBasicAttack(MonsterPathFollower target, float damage)
        {
            if (UnityEngine.Time.time < _fanEnd)
            {
                FanAttack(target, damage);
                return true;
            }

            float perShuriken = damage * Skill("shurikenDamagePercent") / 100f;
            for (int i = 0; i < ShurikenCount; i++)
            {
                if (!target.IsAlive) break;
                Tower.DealDamage(target, perShuriken);
            }
            return true;
        }

        private void FanAttack(MonsterPathFollower target, float damage)
        {
            UnityEngine.Vector3 origin = Tower.transform.position;
            UnityEngine.Vector2 baseDir = SkillGeometry.DirectionXY(origin, target.Position);
            float range = Tower.RangeWorld;
            float halfWidth = RayHalfWidthCells * Tower.CellSize;
            float halfWidthSqr = halfWidth * halfWidth;
            float perShuriken = damage * Ultimate("fanShurikenDamagePercent") / 100f;

            int n = Tower.CollectOwnBoardMonstersInRange(Targets);
            for (int r = 0; r < FanCount; r++)
            {
                float angle = (r - (FanCount - 1) * 0.5f) * FanAngleStep;
                UnityEngine.Vector2 dir = SkillGeometry.Rotate(baseDir, angle);
                UnityEngine.Vector3 end = origin + (UnityEngine.Vector3)(dir * range);
                for (int i = 0; i < n; i++)
                {
                    var m = Targets[i];
                    if (!m.IsAlive) continue;
                    if (SkillGeometry.SqrDistanceToSegmentXY(m.Position, origin, end) <= halfWidthSqr)
                        Tower.DealDamage(m, perShuriken);
                }
            }
            Targets.Clear();
        }

        public override bool CanCastUltimate() => AnyEnemyInRange();

        public override void CastUltimate()
        {
            _fanEnd = UnityEngine.Time.time + UltimateSeconds;
            SkillDebug.Log($"정다희 부채꼴: {UltimateSeconds}초, 표창당 {Ultimate("fanShurikenDamagePercent"):0}%");
        }
    }
}
