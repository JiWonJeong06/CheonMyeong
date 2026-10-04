using UnityEngine;

namespace TowerDefense.Monsters
{
    /// <summary>
    /// 몬스터 한 종류의 마스터 데이터. CharacterDataSO(캐릭터 쪽)와 별개 스키마임 - 몬스터는
    /// 인게임 전투 전용이라 상점/해금 관련 필드가 필요 없고, 대신 웨이브 스포너가 곧바로 쓸 수
    /// 있는 인게임 프리팹 참조를 갖고 있음.
    ///
    /// [구조 조정 - 천명.pptx(2026-09-28, 최신 기획서) 반영] 기존엔 maxHp가 몬스터 하나당
    /// 고정값이었는데, 실제 기획은 "웨이브가 오를수록 병사 기준 체력이 오르고, 그 위에 몬스터
    /// 타입별 배율(병사×1 / 기마병사×0.5 / 방패병사×5)을 곱한 값"이라 고정값 하나로는 표현이
    /// 안 됨. 그래서 maxHp는 "타입 자체의 기준값" 의미로 남겨두고 실제 스폰 체력은
    /// WaveDataSO.soldierBaselineHp * hpMultiplier로 계산함(스폰 시점 계산 로직은
    /// MonsterSpawner 쪽 - 다음 단계에서 작업). 이 방식이면 웨이브별 표(1/5/10/15/20웨이브
    /// 값)가 바뀌어도 몬스터 SO 자체는 안 건드려도 됨 - 밸런스 담당자가 웨이브 에셋 값만
    /// 조정하면 되는 구조.
    ///
    /// spawnWeight/isBoss는 MonsterSpawner가 "70/20/10% 가중치로 랜덤 뽑기"(일반 몬스터,
    /// WaveDataSO.monsterPool)와 "9종 중 랜덤 하나"(보스, WaveDataSO.bossPool) 두 가지 다른
    /// 뽑기 풀을 구분하는 데 씀 - 보스는 일반 몬스터 풀에 안 들어가므로 spawnWeight는 보스 SO
    /// 에서는 실질적으로 안 쓰임(0으로 둬도 무방).
    /// </summary>
    [CreateAssetMenu(menuName = "TowerDefense/Monster Data")]
    public class MonsterDataSO : ScriptableObject
    {
        [Header("식별")]
        public string monsterId;
        public string displayName;

        [Header("전투 스탯")]
        [Tooltip("이 몬스터 타입의 기준 체력값. 일반 몬스터는 이 필드를 직접 안 쓰고 " +
                  "WaveDataSO.soldierBaselineHp(그 웨이브의 병사 기준 체력) × hpMultiplier로 " +
                  "실제 스폰 체력을 계산함 - 보스는 WaveDataSO.bossBaselineHp × hpMultiplier(보통 1)로 " +
                  "계산함. 이 필드 자체는 계산에 안 쓰이는 웨이브 밖 참고용 기본값으로 남겨둠" +
                  "(예: 인스펙터에서 대략적인 체감 확인용, 프리뷰/디버그 스폰 등).")]
        public float maxHp = 10f;

        [Tooltip("병사(기본형) 기준 체력 배율 - 기획서 기준 병사=1, 기마병사=0.5, 방패병사=5. " +
                  "보스는 보통 1(웨이브별 보스 기본 체력을 그대로 씀).")]
        public float hpMultiplier = 1f;

        [Tooltip("병사(기본형) 기준 이동속도 배율 - 기획서 기준 병사=1(보통 속도), " +
                  "기마병사=1.6(빠름), 방패병사=0.6(느림). MonsterPathFollower.Update()에서 " +
                  "moveSpeed × moveSpeedMultiplier로 실제 이동속도를 계산함.")]
        public float moveSpeedMultiplier = 1f;

        public float moveSpeed = 2f;

        [Tooltip("웨이브 내 일반 몬스터 가중치 랜덤 스폰 비율(%) - 기획서 기준 병사=70, " +
                  "기마병사=20, 방패병사=10. 보스(isBoss=true)는 이 값을 안 씀 - 9종 중 균등 " +
                  "랜덤이라 WaveDataSO.bossPool에서 개수 기준으로 뽑힘.")]
        public float spawnWeight = 0f;

        [Tooltip("체크하면 보스 몬스터로 취급 - MonsterSpawner의 일반 몬스터 가중치 풀 " +
                  "(WaveDataSO.monsterPool)이 아니라 보스 풀(WaveDataSO.bossPool)에서만 뽑힘. " +
                  "통과 시 라이프 피해(damageToBase)도 보통 일반 몬스터(1)와 다르게 2로 잡음.")]
        public bool isBoss;

        [Tooltip("[사용 안 함] 예전 골드 보상 필드 - 처치 보상은 killSp(SP)로 대체됨.")]
        public int goldReward = 5;

        [Header("처치 보상 (SP)")]
        [Tooltip("1웨이브 기준 처치 SP - 엑셀: 병사 15 / 기마병사 9 / 방패병사 45 / 보스 450. 0이면 보상 없음.")]
        public float killSp = 0f;

        [Tooltip("웨이브당 증가량(절대값) - 일반 몬스터는 killSp × 0.4(엑셀 +40%/웨이브, 1웨이브 기준 선형), 보스는 150.")]
        public float killSpPerWave = 0f;

        /// <summary>wave(1부터)번째 웨이브의 처치 SP = killSp + killSpPerWave × (wave-1).</summary>
        public float GetKillSp(int wave)
        {
            return killSp + killSpPerWave * Mathf.Max(0, wave - 1);
        }

        [Tooltip("이 몬스터가 경로 끝(플레이어 기지)까지 도달했을 때 기지에 주는 피해량 - " +
                  "기획서 기준 일반 몬스터 1, 보스 2")]
        public int damageToBase = 1;

        [Header("비주얼")]
        public GameObject prefab;
        public Sprite iconSprite;

        [Header("보스 전용 - 참고용 (실제 스킬 로직 미구현)")]
        [Tooltip("보스 기믹 이름 - 천명.pptx 슬라이드 18(ENEMIES) 원문 그대로. 일반 몬스터는 비워둠. " +
                  "실제 스킬 발동/효과 코드는 아직 없음 - 여기 텍스트는 순수 설계 참고용 메모이고, " +
                  "MonsterSpawner/MonsterPathFollower 어디에서도 이 값을 읽어서 뭘 하지 않음.")]
        public string abilityName;

        [Tooltip("보스 기믹 설명 - 천명.pptx 슬라이드 18 원문 그대로. abilityName과 마찬가지로 아직 " +
                  "구현과 연결 안 된 참고용 텍스트임 - 실제 기믹 구현은 별도 스킬 시스템 설계/작업 필요.")]
        public string abilityDescription;
    }
}
