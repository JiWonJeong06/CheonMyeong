using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Monsters
{
    /// <summary>
    /// 웨이브 하나의 구성 - 웨이브 번호별로 에셋을 하나씩 만들어 쓰는 걸 전제함(Wave1.asset,
    /// Wave5.asset...). 각 에셋 자체가 "이 에셋이 몇 번째 웨이브인지"를 soldierBaselineHp/
    /// bossBaselineHp 값으로 표현함.
    ///
    /// [구조 조정 - 천명.pptx(2026-09-28, 최신 기획서) 반영] 기존엔 WaveEntry(몬스터, 개수,
    /// 스폰 간격) 리스트를 순서대로("entries[0] 다 스폰 후 entries[1]...") 스폰하는 구조였는데,
    /// 실제 기획은 "80초(waveDuration) 동안 150마리(ownMonsterCount)를 병사70%/기마병사20%/
    /// 방패병사10% 가중치로 매번 랜덤 뽑기"라서 "고정 순서" 자체가 없음 - 그래서 entries를
    /// monsterPool(가중치는 각 MonsterDataSO.spawnWeight에 있으므로 여기선 후보 목록만 들고
    /// 있음)로 통째로 교체함. 실제 가중치 랜덤 스폰 로직은 MonsterSpawner.RunWaveSpawning
    /// 참고.
    ///
    /// bossPool은 80초 뒤 보스전에서 랜덤으로 뽑힐 9종 보스 후보 목록(기획서: "9종 중 랜덤
    /// · 양쪽 같은 보스" - 양쪽 동일 보스 선택은 서버 권위로 한 번만 굴려야 하는 부분이라 아직
    /// 미구현, 다음 단계에서 작업). soldierBaselineHp/bossBaselineHp는 웨이브별 체력 표
    /// (1웨이브 100/8,600 ~ 20웨이브 1,310/110,300, 표본 5개뿐이라 나머지 웨이브 값은 밸런스
    /// 담당자가 직접 채워야 함 - 여기서 임의로 곡선을 추정해서 채우지 않음)의 그 웨이브 값을
    /// 그대로 넣는 필드. 실제 스폰 체력 = soldierBaselineHp(또는 보스는 bossBaselineHp) ×
    /// MonsterDataSO.hpMultiplier로 계산함(다음 단계에서 MonsterSpawner에 반영 예정 - 지금
    /// 이 커밋에서는 아직 안 함, entries→monsterPool 구조 교체까지만).
    /// </summary>
    [CreateAssetMenu(menuName = "TowerDefense/Wave Data")]
    public class WaveDataSO : ScriptableObject
    {
        public string waveName;

        [Tooltip("이전 웨이브 종료(또는 게임 시작) 후 이 웨이브가 시작되기까지 대기 시간(초)")]
        public float delayBeforeWave = 5f;

        [Tooltip("이 웨이브가 진행되는 시간(초) - 기획서 기준 80초. 이 시간 동안 ownMonsterCount만큼 " +
                  "가중치 랜덤으로 균등 간격 스폰함.")]
        public float waveDuration = 80f;

        [Tooltip("이 웨이브에서 자기 진영이 스폰하는 몬스터 수 - 기획서 기준 150마리(상대에게서 " +
                  "넘어오는 몬스터는 별도로 최대 150마리 추가되어 웨이브당 최대 300마리 - 몬스터 " +
                  "전송 로직은 아직 미구현, 다음 단계에서 작업).")]
        public int ownMonsterCount = 150;

        [Tooltip("일반 몬스터 가중치 랜덤 풀 - 각 MonsterDataSO.spawnWeight 비율대로 뽑힘. " +
                  "isBoss=true인 몬스터는 넣지 말 것(보스는 bossPool 사용).")]
        public List<MonsterDataSO> monsterPool = new();

        [Tooltip("이 웨이브 보스전에서 랜덤으로 뽑힐 보스 후보 목록 - 기획서 기준 9종. " +
                  "isBoss=true인 MonsterDataSO만 넣을 것. (보스전 자체는 아직 미구현)")]
        public List<MonsterDataSO> bossPool = new();

        [Tooltip("[사용 안 함] 체력은 MonsterSpawner.SoldierHpAt(연속 성장 공식)이 계산함. 이전 표 값 보관용. " +
                  "이 웨이브의 병사(기본형 몬스터) 기준 체력 - 표본: 1웨이브 100 / 5웨이브 250 / " +
                  "10웨이브 510 / 15웨이브 880 / 20웨이브 1,310. 나머지 웨이브 값은 기획 쪽에서 " +
                  "보간해서 채울 것.")]
        public float soldierBaselineHp = 100f;

        [Tooltip("[사용 안 함] 보스 체력은 MonsterSpawner가 병사 체력 × 85 × 보스 배율 + 잔여 체력으로 계산함. " +
                  "이 웨이브 보스의 기본 체력(잔여 몬스터 체력 합산 전 값) - 표본: 1웨이브 8,600 / " +
                  "5웨이브 21,200 / 10웨이브 43,100 / 15웨이브 74,200 / 20웨이브 110,300.")]
        public float bossBaselineHp = 8600f;
    }
}
