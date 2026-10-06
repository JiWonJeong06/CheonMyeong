using System.Collections.Generic;
using UnityEngine;
using TowerDefense.Data;
using TowerDefense.Map;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 2503 남궁진 (지하문, 공속형, 원거리)
    /// 스킬 [눈썰미] : 모든 원거리 캐릭터의 사거리 +1, 공격속도 +n%(남궁진 본인 제외). 필드에 남궁진이 여럿이면 가장 강한(강화 → 별 순) 1명만 적용.  n = auraAttackSpeedPercent
    ///   구현: 가장 강한 남궁진이 0.5초마다 아군 원거리 캐릭터에 1초짜리 버프를 갱신함 → 남궁진이 사라지거나 약해지면 최대 1초 안에 자연히 풀림.
    /// 궁극기 [암살 지령] : 내 보드에서 체력이 가장 높은 몬스터 1마리에 공격력의 n% 피해(보스는 n의 절반).  n = snipeDamagePercent
    /// </summary>
    public sealed class NamgungJinSkill : CharacterSkill
    {
        public const int Code = 2503;
        private const float RefreshInterval = 0.5f;
        private const float AuraDuration = 1f;

        private float _timer;
        private readonly List<TowerUnit> _allies = new(16);

        public override void OnTick(float deltaTime)
        {
            _timer -= deltaTime;
            if (_timer > 0f) return;
            _timer = RefreshInterval;

            Tower.CollectAllies(_allies, true);

            TowerUnit strongest = null;
            for (int i = 0; i < _allies.Count; i++)
            {
                var t = _allies[i];
                if (t.Data.code != Code) continue;
                if (strongest == null || IsStronger(t, strongest)) strongest = t;
            }
            if (strongest != Tower) { _allies.Clear(); return; } // 가장 강한 1명만 오라를 검

            var aura = new StatBuff(attackSpeedPercent: Skill("auraAttackSpeedPercent"), rangeBonus: 1f);
            for (int i = 0; i < _allies.Count; i++)
            {
                var t = _allies[i];
                if (t.Data.code == Code) continue; // 남궁진끼리는 받지 않음
                if (t.Data.attackType != CharacterAttackType.Ranged) continue;
                t.SetBuff(Tower, aura, AuraDuration);
            }
            _allies.Clear();
        }

        private static bool IsStronger(TowerUnit a, TowerUnit b)
        {
            if (a.EnhanceLevel != b.EnhanceLevel) return a.EnhanceLevel > b.EnhanceLevel;
            if (a.Stars != b.Stars) return a.Stars > b.Stars;
            return a.GetInstanceID() < b.GetInstanceID(); // 완전 동률이면 항상 같은 한 명
        }

        protected override void OnUnbind() { } // 버프는 1초 만료라 따로 제거할 필요 없음

        public override bool CanCastUltimate() => AnyEnemyOnBoard();

        public override void CastUltimate()
        {
            MonsterPathFollower best = null;
            int n = Tower.CollectOwnBoardMonsters(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (best == null || m.CurrentHp > best.CurrentHp) best = m;
            }
            Targets.Clear();
            if (best == null) return;

            float percent = Ultimate("snipeDamagePercent");
            if (best.IsBoss) percent *= 0.5f;
            float damage = Tower.AttackPower * percent / 100f;
            Tower.DealDamage(best, damage);
            SkillDebug.Log($"남궁진 암살 지령: 피해 {damage:0.#} ({percent:0}%)");
        }
    }
}
