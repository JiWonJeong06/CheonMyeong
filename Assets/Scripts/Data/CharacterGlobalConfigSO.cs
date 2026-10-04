using System;
using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>버프형 궁극기 충전 수치 (Characters.json의 supportUltimateCharge).</summary>
    [Serializable]
    public class SupportUltimateChargeConfig
    {
        public float perSecond;
        public float perSkillUsePerCooldownSecond;
        public float perAssistKill;
        public float assistMaxPerSecond;
        public float assistWindowSeconds;
    }

    /// <summary>
    /// 캐릭터 전체에 공통인 전역값 (Characters.json 최상위). CharacterJsonImporter가 생성/갱신함 -
    /// 직접 고치지 말고 JSON을 고친 뒤 재임포트할 것. CharacterDatabase.Config로 읽음.
    /// </summary>
    public class CharacterGlobalConfigSO : ScriptableObject
    {
        public string dataVersion;
        public int startSkillLevel = 1;
        public int maxSkillLevel = 5;  // SP 강화 최대 레벨
        public int maxStar = 5;        // 합성 최대 별
        public SupportUltimateChargeConfig supportUltimateCharge = new();
    }
}
