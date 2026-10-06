using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 2504 홍한 (지하문, 공속형, 근접)
    /// 스킬 [연타] : 공격할 때마다 공격속도 +3%, 최대 n중첩, 3초간 공격하지 않으면 초기화.  n = maxStacks
    /// 궁극기 [무골권] : n초 동안 공격속도 +100%, 한 번의 공격에 두 번 공격(두 번째 타격은 같은 공격력, 게이지는 평타 1회분만).  n = durationSeconds
    /// </summary>
    public sealed class HongHanSkill : CharacterSkill
    {
        private const float PerStackSpeedPercent = 3f;
        private const float ResetSeconds = 3f;
        private const float UltimateSpeedPercent = 100f;

        private int _stacks;
        private float _lastAttackTime;
        private float _ultimateEnd;
        private readonly object _ultimateKey = new object();

        protected override void OnBind() => ApplyStackBuff();
        protected override void OnUnbind()
        {
            Tower.RemoveBuff(this);
            Tower.RemoveBuff(_ultimateKey);
        }

        public override void OnProgressionChanged()
        {
            int max = Mathf.RoundToInt(Skill("maxStacks"));
            if (_stacks > max) _stacks = max;
            ApplyStackBuff();
        }

        public override void OnTick(float deltaTime)
        {
            if (_stacks > 0 && Time.time - _lastAttackTime > ResetSeconds)
            {
                _stacks = 0;
                ApplyStackBuff();
            }
        }

        public override void OnAttackStart(MonsterPathFollower target)
        {
            float now = Time.time;
            if (now - _lastAttackTime > ResetSeconds) _stacks = 0;
            _lastAttackTime = now;

            int max = Mathf.RoundToInt(Skill("maxStacks"));
            if (_stacks < max) _stacks++;
            ApplyStackBuff();

            // 무골권: 한 번 더 타격(OnBasicHit 훅도 정상 발동)
            if (now < _ultimateEnd && target.IsAlive) Tower.ResolveBasicHit(target, Tower.AttackPower);
        }

        private void ApplyStackBuff() => Tower.SetBuff(this, new StatBuff(attackSpeedPercent: _stacks * PerStackSpeedPercent));

        public override bool CanCastUltimate() => AnyEnemyInRange();

        public override void CastUltimate()
        {
            float duration = Ultimate("durationSeconds");
            _ultimateEnd = Time.time + duration;
            Tower.SetBuff(_ultimateKey, new StatBuff(attackSpeedPercent: UltimateSpeedPercent), duration);
            SkillDebug.Log($"홍한 무골권: {duration:0.#}초");
        }
    }
}
