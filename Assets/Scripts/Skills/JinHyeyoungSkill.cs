using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 2501 진혜영 (지하문, 버프형, 근접)
    /// 스킬 [먹] : 사거리 안 랜덤 위치에 반경 1칸 먹 장판을 던짐. 장판은 m초 유지, 위의 몬스터 이동속도 n% 감소.
    ///   장판이 사라진 뒤 k초가 지나면 다시 던짐(장판이 있는 동안은 대기 시간이 흐르지 않음).
    ///   n = inkSlowPercent, m = inkDurationSeconds, k = inkCooldownSeconds
    /// 궁극기 [붓] : 10초 동안 상대 필드 몬스터 이동속도 +n%(중첩 가능, 합계 최대 30%).  n = enemyMoveSpeedPercent
    /// </summary>
    public sealed class JinHyeyoungSkill : SupportCharacterSkill
    {
        private const float InkRadiusCells = 1f;
        private const float SlowRefreshInterval = 0.2f; // 장판 위 몬스터 감속을 이 간격으로 갱신(감속 지속은 간격의 2.5배)
        private const float BrushSeconds = 10f;
        private static readonly Color InkColor = new Color(0.15f, 0.1f, 0.3f);

        private bool _zoneActive;
        private Vector3 _zoneCenter;
        private float _zoneEnd;
        private float _nextThrowTime;
        private float _nextSlowTick;

        protected override void OnBind() => _nextThrowTime = Time.time;

        protected override void OnSupportTick(float deltaTime)
        {
            float now = Time.time;
            if (_zoneActive)
            {
                if (now >= _zoneEnd)
                {
                    _zoneActive = false;
                    _nextThrowTime = now + Skill("inkCooldownSeconds"); // 장판이 사라진 순간부터 대기 시간 시작
                }
                else if (now >= _nextSlowTick)
                {
                    _nextSlowTick = now + SlowRefreshInterval;
                    ApplyZoneSlow();
                }
            }
            else if (now >= _nextThrowTime)
            {
                Throw(now);
            }
        }

        private void Throw(float now)
        {
            // 사거리 원 안의 균등 랜덤 위치
            float angle = Random.value * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Random.value) * Tower.RangeWorld;
            _zoneCenter = Tower.transform.position + new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);

            float duration = Skill("inkDurationSeconds");
            _zoneActive = true;
            _zoneEnd = now + duration;
            _nextSlowTick = now;

            Tower.PlayZoneEffectRpc(_zoneCenter, InkRadiusCells * Tower.CellSize, duration, InkColor);
            GrantSkillUseGauge(duration + Skill("inkCooldownSeconds")); // 스킬 주기 = 장판 유지 + 대기
        }

        private void ApplyZoneSlow()
        {
            float percent = Skill("inkSlowPercent");
            float radius = InkRadiusCells * Tower.CellSize;
            float rSqr = radius * radius;
            int key = Tower.GetInstanceID();

            int n = Tower.CollectOwnBoardMonsters(Targets);
            for (int i = 0; i < n; i++)
            {
                var m = Targets[i];
                if (SkillGeometry.SqrDistXY(m.Position, _zoneCenter) > rSqr) continue;
                m.ApplySlow(key, percent, SlowRefreshInterval * 2.5f);
                m.MarkSupportEffect(Tower);
            }
            Targets.Clear();
        }

        public override bool CanCastUltimate() => Tower.OpponentSpawner != null;

        public override void CastUltimate()
        {
            var opponent = Tower.OpponentSpawner;
            if (opponent == null) return;
            opponent.AddMoveSpeedBonus(Ultimate("enemyMoveSpeedPercent"), BrushSeconds);
            SkillDebug.Log($"진혜영 붓: 상대 필드 이동속도 +{Ultimate("enemyMoveSpeedPercent"):0.#}% / {BrushSeconds}초 (현재 합계 {opponent.MoveSpeedBonusPercent:0.#}%)");
        }
    }
}
