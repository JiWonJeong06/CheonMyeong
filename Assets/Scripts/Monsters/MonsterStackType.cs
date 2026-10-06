namespace TowerDefense.Monsters
{
    /// <summary>
    /// 몬스터에 쌓이는 스킬 스택 종류. MonsterPathFollower가 종류별 정수 하나씩 들고 있음(풀링 재사용 때 0으로 초기화).
    /// 스택을 쓰는 캐릭터를 만들 때 여기에 종류를 추가하고, 반드시 Count는 맨 마지막에 둘 것(배열 크기로 쓰임).
    /// </summary>
    public enum MonsterStackType
    {
        Crow,   // 하영 - 까마귀 (몬스터당 최대 15)
        Count   // 항상 마지막 - 종류 개수
    }

    internal static class MonsterStackTypeInfo
    {
        public const int Count = (int)MonsterStackType.Count;
    }
}
