using System;

namespace TowerDefense.Data
{
    // 캐릭터 하나가 갖는 스킬 1개 분량. ScriptableObject가 아니라 순수 직렬화 클래스로 둬서
    // CharacterDataSO 안에 List<SkillData>로 그냥 들고 있을 수 있게 함 (스킬마다 애셋 파일을
    // 따로 만들 필요 없음 — 캐릭터당 2개 정도라 이 정도면 충분).
    [Serializable]
    public class SkillData
    {
        public string skillId;
        public string skillName;
        public string description;
        public float cooldown;

        // 데미지량/회복량/버프% 등 스킬마다 의미가 다른 수치 하나.
        // 실제 의미는 skillType으로 구분해서 해석함.
        public float value;

        // 자유 텍스트 태그 (예: Attack / Buff / Debuff). 기획이 아직 종류를 확정 안 했으니
        // enum으로 안 박고 문자열로 둬서, 기획 시트에서 새 타입 추가돼도 코드 재빌드 없이 바로 반영됨.
        public string skillType;
    }
}
