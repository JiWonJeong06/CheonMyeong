namespace TowerDefense.Data
{
    /// <summary>
    /// 노드 트리 효과 종류 (엑셀 '노드 트리' 시트 A44~A47). 수치는 기획자가 노드를 배치한 뒤 정해지므로
    /// 지금은 효과 종류만 있고 값은 TechTreeData.json 노드의 effectValue(퍼센트)에 채워 넣으면 됨.
    /// 캐릭터 해금/레벨업은 별도 시스템(CharacterUnlockManager/CharacterLevelManager)이라 여기 없음.
    /// </summary>
    public enum NodeEffectType
    {
        None = 0,
        LineAttackPercent,           // 소속 공격력 +n%   - 그 라인 소속 캐릭터(무소속 제외)
        LineAttackSpeedPercent,      // 소속 공격속도 +n% - 그 라인 소속 캐릭터(무소속 제외)
        LineEnhancePriceDiscountPercent, // 소속 SP 강화 가격 -n% - 그 라인 소속 캐릭터(무소속 제외)
        GlobalAttackPercent          // 전체 공격력 +n%   - 모든 캐릭터(무소속 포함)
    }
}
