namespace TowerDefense.Skills
{
    /// <summary>
    /// 타워 하나에 거는 스탯 버프 묶음. 퍼센트는 "8 = +8%" 단위(엑셀 표기 그대로).
    /// - damagePercent: 공격력 n% 증가(여러 개는 %끼리 더함)
    /// - attackSpeedPercent: 공격속도 n% 증가/감소. 간격 ÷ (1 + n%)로 계산하며 여러 개는 %끼리 더해 한 번에 적용,
    ///   감소는 최대 -50%(엑셀 구현 규칙 '공격속도 %')
    /// - rangeBonus: 사거리 +n칸(실수)
    /// </summary>
    public readonly struct StatBuff
    {
        public readonly float damagePercent;
        public readonly float attackSpeedPercent;
        public readonly float rangeBonus;

        public StatBuff(float damagePercent = 0f, float attackSpeedPercent = 0f, float rangeBonus = 0f)
        {
            this.damagePercent = damagePercent;
            this.attackSpeedPercent = attackSpeedPercent;
            this.rangeBonus = rangeBonus;
        }
    }
}
