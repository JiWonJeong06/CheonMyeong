using System.Collections.Generic;
using TowerDefense.Data;

namespace TowerDefense.Data.Import
{
    // JsonUtility는 최상위가 배열인 JSON을 못 읽기 때문에( "[...]" 바로 파싱 불가 ),
    // 반드시 { "characters": [ ... ] } 형태로 한 번 감싸야 함. 이 파일 하나가 그 감싼 형태.
    [System.Serializable]
    public class CharacterJsonFile
    {
        public List<CharacterJsonEntry> characters;
    }

    // CharacterDataSO와 필드를 1:1로 맞춘 DTO.
    // unlockCurrency는 CurrencyType enum이 아니라 그냥 string으로 받음 —
    // JsonUtility가 enum을 정수로 취급해서 기획/프로그래머가 JSON에 "Gold"/"Gem" 대신
    // 0/1을 써야 하는 실수 유발 지점이 되기 때문. 문자열로 받은 다음
    // CharacterJsonImporter에서 수동으로 enum에 매핑함.
    [System.Serializable]
    public class CharacterJsonEntry
    {
        public string characterId;
        public string displayName;
        public string description;

        public string characterTag;

        public float baseDamage;
        public float range;
        public float attackSpeed;
        public int summonCost;

        public int unlockCost;
        public string unlockCurrency; // "Gold" 또는 "Gem"

        // 스킬은 기존 TowerDefense.Data.SkillData를 그대로 재사용.
        // SkillData 자체가 이미 [Serializable]인 순수 데이터 클래스라서
        // JSON 쪽 스킬 필드명도 skillId/skillName/description/cooldown/value/skillType 그대로 맞추면 됨.
        public List<SkillData> skills;
    }
}
