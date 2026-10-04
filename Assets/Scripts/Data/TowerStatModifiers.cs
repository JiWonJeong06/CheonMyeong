namespace TowerDefense.Data
{
    /// <summary>
    /// 타워 하나에 적용되는 "노드 트리 계층" 배율 묶음(서버가 배치 시점에 계산해서 TowerUnit에 넘김).
    /// 최종 피해 = 기본 × 노드 레벨 배율 × (1 + 노드 % 합) × 합성 배율 × SP 강화 배율 (엑셀 규칙) 중
    /// 앞쪽 두 항(노드 레벨 배율 × (1 + 노드 % 합))이 damageMultiplier에 들어 있음. 합성/SP 강화 배율은
    /// 정지원 파트(PART B)라 여기 포함하지 않음.
    /// 공격 간격 = 기본 간격 × 레벨 간격 배율 ÷ (1 + 소속 공속 %) - 공속 증가는 '간격 ÷ (1 + n%)'(엑셀 규칙).
    /// </summary>
    public readonly struct TowerStatModifiers
    {
        public readonly int level;
        public readonly float damageMultiplier;
        public readonly float attackIntervalMultiplier;

        public TowerStatModifiers(int level, float damageMultiplier, float attackIntervalMultiplier)
        {
            this.level = level;
            this.damageMultiplier = damageMultiplier;
            this.attackIntervalMultiplier = attackIntervalMultiplier;
        }

        /// <summary>배율 없음(Lv1, 노드 효과 없음). default(struct)는 배율이 0이라 쓰면 안 됨 - 이걸 쓸 것.</summary>
        public static readonly TowerStatModifiers Identity = new TowerStatModifiers(1, 1f, 1f);

        /// <summary>
        /// 레벨과 노드 % 합으로 배율 계산. 퍼센트는 "5 = +5%" 단위.
        /// </summary>
        public static TowerStatModifiers Compute(int level, float attackPercentSum, float attackSpeedPercentSum)
        {
            level = CharacterLevelTable.ClampLevel(level);
            float damage = CharacterLevelTable.GetAttackMultiplier(level) * (1f + attackPercentSum / 100f);
            float speedDivisor = 1f + attackSpeedPercentSum / 100f;
            float interval = CharacterLevelTable.GetIntervalMultiplier(level) / (speedDivisor > 0.01f ? speedDivisor : 0.01f);
            return new TowerStatModifiers(level, damage, interval);
        }
    }
}
