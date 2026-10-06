using TowerDefense.Data;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 캐릭터 코드(엑셀 '코드' = CharacterDataSO.code) → 스킬 클래스 연결표. 캐릭터를 구현할 때 case를 한 줄 추가함.
    /// 여기 없는 캐릭터는 null(= 스킬 없는 기본 공격만 하는 타워 - 게이지가 차도 궁극기는 발동하지 않고 대기).
    /// 스킬 객체는 타워 하나당 하나, 배치 시 한 번만 생성됨(합성/강화로는 새로 만들지 않음).
    /// </summary>
    public static class CharacterSkillFactory
    {
        public static CharacterSkill Create(CharacterDataSO data)
        {
            if (data == null) return null;

            switch (data.code)
            {
                case 1000: return new YuraSkill();      // 이유라
                case 1004: return new HayoungSkill();   // 하영
                case 2500: return new ParkSeonghunSkill();   // 박성훈
                case 2501: return new JinHyeyoungSkill();   // 진혜영
                case 2502: return new JungDaheeSkill();   // 정다희
                case 2503: return new NamgungJinSkill();   // 남궁진
                case 2504: return new HongHanSkill();   // 홍한
                case 3000: return new KimHyeongukSkill();   // 김형욱
                case 3001: return new JeonMinjaeSkill();   // 전민재
                case 3002: return new YuHarinSkill();   // 유하린
                case 3500: return new SeoHyeokhoSkill();   // 서혁호
                case 3501: return new BaekHaeunSkill();   // 백하은
                case 3502: return new CheonHuijaeSkill();   // 천희재
                case 4000: return new KangMinjunSkill();   // 강민준
                case 4001: return new ImSeoyeonSkill();   // 임서연
                case 4002: return new ShotaSkill();   // 쇼타
                default: return null;
            }
        }
    }
}
