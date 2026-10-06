using System.Collections.Generic;
using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 1004 하영 (천명회, 공격력형, 근접)
    /// 패시브 [혼오의 깃] : 타격 시 '까마귀' 스택 1 + n% 확률로 1 더(몬스터당 최대 15).  n = extraStackChancePercent
    /// 궁극기 [혼오의 일격] : 맵 전체(내 보드)에서 스택이 있는 모든 몬스터에게 (스택 × 공격력 × n%) 피해, 모든 스택 제거.  n = stackDamagePercent
    /// 스택이 있는 몬스터가 하나도 없으면 게이지를 채운 채 대기함(엑셀 구현 규칙). 1웨이브 병사는 한 방에 죽어 스택이 안 남음.
    /// </summary>
    public sealed class HayoungSkill : CharacterSkill
    {
        public const int MaxCrowStacks = 15;

        // 궁극기 발동 최소 조건: 스택이 붙은 몬스터가 이 마리 수 이상일 때만 발동. 1 = 한 마리라도 스택이 있으면 발동
        // (보스 대용 - 보스 한 마리에도 써야 하므로 1로 확정). 조건 미달이면 게이지가 가득 찬 채 대기함.
        public const int MinUltimateTargets = 1;

        private readonly List<MonsterPathFollower> _buffer = new(32); // 재사용 - 궁극기마다 할당 없음

        public override void OnBasicHit(MonsterPathFollower target, float damage)
        {
            int add = 1;
            float chance = Skill("extraStackChancePercent");
            if (chance > 0f && Random.value * 100f < chance) add++;
            target.AddStacks(MonsterStackType.Crow, add, MaxCrowStacks);
        }

        // 게이지가 가득 찬 동안만 매 프레임 불림 - 스택이 붙은 몬스터가 MinUltimateTargets마리 이상이면 true
        public override bool CanCastUltimate()
        {
            int n = Tower.CollectOwnBoardMonsters(_buffer);
            int stacked = 0;
            for (int i = 0; i < n; i++)
            {
                if (_buffer[i].GetStacks(MonsterStackType.Crow) > 0 && ++stacked >= MinUltimateTargets)
                {
                    _buffer.Clear();
                    return true;
                }
            }
            _buffer.Clear();
            return false;
        }

        public override void CastUltimate()
        {
            float percent = Ultimate("stackDamagePercent") / 100f;
            float attack = Tower.AttackPower; // 노드 레벨·버프·날씨까지 반영된 현재 공격력

            int n = Tower.CollectOwnBoardMonsters(_buffer);
            int hit = 0;
            for (int i = 0; i < n; i++)
            {
                var m = _buffer[i];
                if (m == null || !m.IsAlive) continue;
                int stacks = m.GetStacks(MonsterStackType.Crow);
                if (stacks <= 0) continue;

                m.ClearStacks(MonsterStackType.Crow); // 피해보다 먼저 - 막타로 반납되기 전에 처리
                Tower.DealDamage(m, stacks * attack * percent);
                hit++;
            }
            _buffer.Clear();

            SkillDebug.Log($"하영 혼오의 일격: {hit}마리, 계수 {percent * 100f:0}%, 공격력 {attack:0.#}");
        }
    }
}
