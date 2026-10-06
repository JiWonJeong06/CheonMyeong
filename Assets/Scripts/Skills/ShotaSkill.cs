using UnityEngine;
using TowerDefense.Map;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 4002 쇼타 (근접)
    /// 스킬 [해일] : 80초마다 내 보드의 모든 몬스터를 경로 뒤로 밀어내고 공격력 n% 피해.  n = tidalDamagePercent
    ///   (밀어내는 거리: 경로 길이의 10% - 시트 미정, 임시값)
    /// 궁극기 [암살] : n% 확률로 상대 보드의 랜덤 캐릭터 1명의 별 -1(1★이면 제거). 상대 보호막이 있으면 보호막만 소모.  n = starDownChancePercent
    /// </summary>
    public sealed class ShotaSkill : CharacterSkill
    {
        private const float TidalCycleSeconds = 80f;
        private const float PushPathPercent = 10f;

        private float _nextTidal;
        private readonly System.Collections.Generic.List<TowerUnit> _enemies = new(16);

        protected override void OnBind() => _nextTidal = Time.time + TidalCycleSeconds;

        public override void OnTick(float deltaTime)
        {
            float now = Time.time;
            if (now < _nextTidal) return;
            _nextTidal = now + TidalCycleSeconds;

            float damage = Tower.AttackPower * Skill("tidalDamagePercent") / 100f;
            int n = Tower.CollectOwnBoardMonsters(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (!m.IsAlive) continue;
                if (Tower.DealDamage(m, damage)) continue; // 막타로 죽으면 밀지 않음
                m.PushBackPathPercent(PushPathPercent);
            }
            Targets.Clear();
            SkillDebug.Log($"쇼타 해일: {n}마리, {Skill("tidalDamagePercent"):0}%");
        }

        public override bool CanCastUltimate() => Tower.CollectEnemyTowers(_enemies) > 0;

        public override void CastUltimate()
        {
            int count = Tower.CollectEnemyTowers(_enemies);
            if (count == 0) return;

            float chance = Ultimate("starDownChancePercent");
            if (Random.value * 100f < chance)
            {
                var victim = _enemies[Random.Range(0, count)];
                if (victim.TryConsumeShield())
                    SkillDebug.Log("쇼타 암살: 보호막에 막힘");
                else if (MatchController.Instance != null)
                {
                    MatchController.Instance.ReduceTowerStar(victim);
                    SkillDebug.Log("쇼타 암살: 성공(별 -1)");
                }
            }
            else SkillDebug.Log($"쇼타 암살: 실패({chance:0}%)");
            _enemies.Clear();
        }
    }
}
