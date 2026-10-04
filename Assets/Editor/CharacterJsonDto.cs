using System.Collections.Generic;
using TowerDefense.Data;

namespace TowerDefense.Data.Import
{
    // JsonUtility는 최상위가 배열인 JSON을 못 읽으므로 { "characters": [ ... ] } 형태로 감싼 파일을 받음.
    // 필드 이름은 Characters.json(엑셀 '캐릭터 스탯' + '스킬 밸런싱'에서 내보낸 파일)과 1:1로 같아야 함 -
    // JsonUtility는 이름이 다른 필드를 조용히 무시하므로 이름 오타가 나면 값이 0/빈 값으로 들어옴.
    // enum은 JsonUtility가 문자열을 못 읽어서(정수로 취급) 전부 string으로 받고 임포터에서 파싱함.
    [System.Serializable]
    public class CharacterJsonFile
    {
        public string version;
        public string source;
        public int startSkillLevel;
        public int maxSkillLevel;
        public int maxStar;
        public SupportUltimateChargeConfig supportUltimateCharge;
        public List<CharacterJsonEntry> characters;
    }

    [System.Serializable]
    public class CharacterJsonEntry
    {
        public int code;
        public string name;
        public string weapon;
        public string attackType;      // "Melee" / "Ranged"
        public bool isSupport;

        public float attackPower;
        public float attackInterval;
        public float range;
        public string targetPriority;  // "Front" / "HighestHp" / "LowestHp"
        public string mergeType;       // "Power" / "Speed"
        public float starAttackPower;
        public float starAttackInterval;

        public int gaugePerHit;
        public string killType;        // "Normal" / "Area" / "Sniper"
        public int gaugePerKill;
        public int ultimateGauge;

        public SkillDefinition skill;
        public SkillDefinition ultimate;
    }
}
