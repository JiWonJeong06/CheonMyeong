namespace TowerDefense.Skills
{
    /// <summary>
    /// 버프형(기본 공격을 하지 않는) 캐릭터용 베이스: 서윤아·윤재훈·진혜영·유하린·천희재.
    /// 버프형 궁극기 충전 규칙(엑셀 기준값): 시간 초당 10 + 스킬 사용 1회(스킬 주기 1초당 1.5, 예: 40초 주기 = +60)
    /// + 어시스트(버프형 효과가 걸린 몬스터가 5초 안에 죽으면 +1, 초당 최대 +5 - 어시스트는 몬스터/TowerUnit이 처리).
    /// 매 프레임 로직은 OnTick 대신 OnSupportTick을 덮어쓸 것(OnTick은 시간 충전을 하므로 봉인됨).
    /// </summary>
    public abstract class SupportCharacterSkill : CharacterSkill
    {
        public const float GaugePerSecond = 10f;
        public const float GaugePerSkillCycleSecond = 1.5f;

        public sealed override void OnTick(float deltaTime)
        {
            Tower.AddGauge(GaugePerSecond * deltaTime);
            OnSupportTick(deltaTime);
        }

        protected virtual void OnSupportTick(float deltaTime) { }

        /// <summary>스킬을 한 번 쓸 때 호출 - 스킬 주기(초)에 비례한 게이지를 채움.</summary>
        protected void GrantSkillUseGauge(float cycleSeconds)
        {
            Tower.AddGauge(cycleSeconds * GaugePerSkillCycleSecond);
        }
    }
}
