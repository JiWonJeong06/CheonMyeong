using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 3500 서혁호 (근접, 전방 우선)
    /// 스킬 [환경 적응] : 사거리 내 적 1명당 피해 n% 증가(최대 5명).  n = damagePerEnemyPercent
    /// 궁극기 [지반 붕괴] : 사거리 내 "전방"(현재 타겟 방향 반원)의 적에게 공격력의 n% 피해 + 30% 감속 5초.  n = groundDamagePercent
    /// </summary>
    public sealed class SeoHyeokhoSkill : CharacterSkill
    {
        private const int MaxCounted = 5;
        private const float RefreshInterval = 0.2f;
        private const int SlowKey = 3500;
        private const float SlowPercent = 30f;
        private const float SlowSeconds = 5f;

        private float _nextRefresh;
        private int _lastCount = -1;

        protected override void OnUnbind() => Tower.RemoveBuff(this);

        public override void OnProgressionChanged() => _lastCount = -1; // 수치가 바뀌었으니 다음 갱신에서 다시 걺

        public override void OnTick(float deltaTime)
        {
            float now = Time.time;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshInterval;

            int count = Mathf.Min(MaxCounted, Tower.CollectOwnBoardMonstersInRange(Probe));
            Probe.Clear();
            if (count == _lastCount) return;
            _lastCount = count;

            if (count == 0) Tower.RemoveBuff(this);
            else Tower.SetBuff(this, new StatBuff(damagePercent: Skill("damagePerEnemyPercent") * count));
        }

        public override bool CanCastUltimate() => AnyEnemyInRange();

        public override void CastUltimate()
        {
            var front = Tower.FindTarget();
            if (front == null) return;

            Vector2 dir = SkillGeometry.DirectionXY(Tower.transform.position, front.Position);
            Vector3 origin = Tower.transform.position;
            float damage = Tower.AttackPower * Ultimate("groundDamagePercent") / 100f;

            int n = Tower.CollectOwnBoardMonstersInRange(Targets);
            int hit = 0;
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (!m.IsAlive) continue;
                Vector2 to = new Vector2(m.Position.x - origin.x, m.Position.y - origin.y);
                if (Vector2.Dot(to, dir) < 0f) continue; // 전방 반원만
                m.ApplySlow(SlowKey, SlowPercent, SlowSeconds);
                Tower.DealDamage(m, damage);
                hit++;
            }
            Targets.Clear();
            Tower.PlayZoneEffectRpc(origin, Tower.RangeWorld, 0.4f, new Color(0.55f, 0.4f, 0.25f, 0.45f));
            SkillDebug.Log($"서혁호 지반 붕괴: {hit}마리, {Ultimate("groundDamagePercent"):0}%");
        }
    }
}
