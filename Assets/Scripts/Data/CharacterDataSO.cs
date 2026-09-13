using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Data
{
    public enum CurrencyType
    {
        Gold,
        Gem
    }

    /// <summary>
    /// 캐릭터 하나의 마스터 데이터. CharacterDataImporter가 CSV를 읽어서 이 애셋들을
    /// 자동으로 생성/갱신함 — 그러니까 이 애셋 필드값은 직접 인스펙터에서 손으로 고치지 말고
    /// 항상 CharacterSheet.csv / CharacterSkills.csv 쪽을 고친 다음 재임포트할 것.
    ///
    /// characterId는 테크트리 쪽(TechTreeManager)이 쓰는 characterTypeId와 동일한 값으로 쓰면 됨 —
    /// 별도 필드를 안 둔 이유는 지금 구조에서 캐릭터 종류당 인스턴스가 1:1이라 중복 필드가 불필요해서.
    /// </summary>
    [CreateAssetMenu(menuName = "TowerDefense/Character Data")]
    public class CharacterDataSO : ScriptableObject
    {
        [Header("식별")]
        public string characterId;
        public string displayName;
        [TextArea] public string description;

        [Header("분류 (서열 없는 태그, 3종 중 하나 — 기획 확정 대기)")]
        public string characterTag;

        [Header("전투 스탯")]
        public float baseDamage;
        public float range;
        public float attackSpeed;
        public int summonCost; // SP 소환 비용

        [Header("상점 (확정 캐릭터 구매)")]
        public int unlockCost;
        public CurrencyType unlockCurrency;

        [Header("비주얼")]
        public Sprite iconSprite;
        public Sprite portraitSprite;

        [Header("스킬 (보통 2개)")]
        public List<SkillData> skills = new();
    }
}
