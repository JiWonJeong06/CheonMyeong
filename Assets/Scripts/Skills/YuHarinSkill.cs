using System.Collections.Generic;
using UnityEngine;
using TowerDefense.Map;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 3002 유하린 (풍류단, 버프형, 원거리)
    /// 스킬 [불완전 보호] : n초마다 랜덤한 아군 1명에게 보호막(중첩 X). 보호막은 상대 스킬 효과를 1회 무효화. 본인은 소환 시 보호막을 가짐.  n = shieldIntervalSeconds
    /// 궁극기 [작은 별] : 랜덤한 상대 캐릭터 2명에게 별똥별 - 맞은 캐릭터는 n초 동안 주는 피해가 몬스터 회복으로 전환(상대 보호막이 있으면 보호막만 소모).  n = healConvertSeconds
    /// </summary>
    public sealed class YuHarinSkill : SupportCharacterSkill
    {
        private const int StarCount = 2;

        private float _nextShieldTime;
        private readonly List<TowerUnit> _towers = new(16);

        protected override void OnBind()
        {
            Tower.GrantShield();
            _nextShieldTime = Time.time + Skill("shieldIntervalSeconds");
        }

        protected override void OnSupportTick(float deltaTime)
        {
            float now = Time.time;
            if (now < _nextShieldTime) return;

            // 보호막이 없는 아군 중 랜덤 1명(본인 포함)
            Tower.CollectAllies(_towers, true);
            for (int i = _towers.Count - 1; i >= 0; i--)
            {
                if (_towers[i].HasShield) _towers.RemoveAt(i);
            }

            if (_towers.Count == 0)
            {
                _nextShieldTime = now + 1f; // 모두 보호막이 있으면 1초 뒤 다시 확인
                return;
            }

            _towers[Random.Range(0, _towers.Count)].GrantShield();
            _towers.Clear();

            float interval = Skill("shieldIntervalSeconds");
            _nextShieldTime = now + interval;
            GrantSkillUseGauge(interval);
        }

        public override bool CanCastUltimate() => Tower.CollectEnemyTowers(_towers) > 0;

        public override void CastUltimate()
        {
            int count = Tower.CollectEnemyTowers(_towers);
            float seconds = Ultimate("healConvertSeconds");
            int stars = Mathf.Min(StarCount, count);

            int blocked = 0;
            for (int s = 0; s < stars; s++)
            {
                int idx = Random.Range(0, _towers.Count);
                var enemy = _towers[idx];
                _towers.RemoveAt(idx); // 같은 캐릭터에 두 번 맞지 않게
                if (enemy.TryConsumeShield()) { blocked++; continue; }
                enemy.ApplyHealConvert(seconds);
            }
            _towers.Clear();
            SkillDebug.Log($"유하린 작은 별: {stars}명, 전환 {seconds:0.#}초, 보호막으로 막힘 {blocked}");
        }
    }
}
