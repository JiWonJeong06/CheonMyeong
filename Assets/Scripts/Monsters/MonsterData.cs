using UnityEngine;

namespace TowerDefense.Monsters
{
    /// <summary>
    /// 몬스터 한 종류의 마스터 데이터. CharacterDataSO(캐릭터 쪽)와 별개 스키마임 - 몬스터는
    /// 인게임 전투 전용이라 상점/해금 관련 필드가 필요 없고, 대신 웨이브 스포너가 곧바로 쓸 수
    /// 있는 인게임 프리팹 참조를 갖고 있음.
    /// </summary>
    [CreateAssetMenu(menuName = "TowerDefense/Monster Data")]
    public class MonsterDataSO : ScriptableObject
    {
        [Header("식별")]
        public string monsterId;
        public string displayName;

        [Header("전투 스탯")]
        public float maxHp = 10f;
        public float moveSpeed = 2f;

        [Tooltip("이 몬스터를 처치했을 때 지급되는 골드 - 더미값, 밸런스 확정 전")]
        public int goldReward = 5;

        [Tooltip("이 몬스터가 경로 끝(플레이어 기지)까지 도달했을 때 기지에 주는 피해량")]
        public int damageToBase = 1;

        [Header("비주얼")]
        public GameObject prefab;
        public Sprite iconSprite;
    }
}
