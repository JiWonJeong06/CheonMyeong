using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TowerDefense.Map;
using TowerDefense.Data;

namespace TowerDefense.Monsters
{
    /// <summary>
    /// 웨이브 목록(waves)을 순서대로 진행하며 몬스터를 스폰함. 몬스터 인스턴스는 오브젝트 풀로
    /// 재사용함(몬스터 종류별로 큐를 따로 둠) - Instantiate/Destroy를 웨이브마다 반복하면 GC 스파이크와
    /// 메모리 단편화가 누적되기 쉬워서, 죽거나 경로 끝에 도달한 몬스터는 Destroy 대신 SetActive(false) 후
    /// 풀에 반납하고 다음 스폰 때 재사용함.
    ///
    /// [네트워킹] 몬스터는 두 플레이어 화면에 똑같이 보여야 해서 NetworkObject로 스폰함
    /// (Unity.Netcode.NetworkObject.Spawn()) - 이건 서버에서만 호출 가능한 API라, 이 스크립트의
    /// 스폰/웨이브 진행 로직 전체를 서버(NetworkManager.Singleton.IsServer)에서만 실행되도록 막음.
    /// 클라이언트 쪽에도 이 컴포넌트가 씬에 똑같이 존재하지만(보드가 로컬에도 그려져야 하니까)
    /// 아무 것도 안 하고 가만히 있다가, 서버가 스폰한 NetworkObject가 자동으로 복제돼서 나타나는
    /// 걸 그냥 받아서 보여주기만 함.
    ///
    /// 풀링도 네트워크 스폰 방식에 맞게 바꿨음 - 일반적인 SetActive(false) 대신
    /// NetworkObject.Despawn(destroy: false)로 "네트워크 동기화는 끊되 인스턴스는 유지"한 다음
    /// SetActive(false)로 숨기고, 재사용할 때 SetActive(true) + Spawn()으로 다시 네트워크에 등록함
    /// (Boss Room 등 Unity 공식 샘플에서 쓰는 네트워크 오브젝트 풀링 패턴).
    ///
    /// [구조 조정 - 천명.pptx(2026-09-28, 최신 기획서) 반영] RunWaves()가 예전엔
    /// "WaveDataSO.entries를 몬스터 종류별로 순서대로 다 스폰"했는데, 실제 기획은 "80초 동안
    /// 150마리를 병사70%/기마병사20%/방패병사10% 가중치로 매번 랜덤 뽑아 균등 간격 스폰"이라
    /// RunWaveSpawning()으로 교체함(PickWeightedRandom이 실제 가중치 뽑기 담당). 80초 경과 후
    /// 보스전(잔여 몬스터 체력 합산 + 보스 스폰 + 처치 대기)은 RunBossPhase()가 담당함(아래
    /// 별도 문단 참고) - RunWaves()는 웨이브마다 RunWaveSpawning → RunBossPhase 순으로 진행함.
    ///
    /// [체력 스케일] 스폰 시점에 실제 체력 = wave.soldierBaselineHp × MonsterDataSO.hpMultiplier로
    /// 계산해서 SpawnMonster(data, actualMaxHp)에 넘김(RunWaveSpawning 참고) - data.maxHp는
    /// 더 이상 실제 스폰 체력에 안 쓰임(MonsterDataSO 쪽 주석 참고). SpawnMonster(data) 1개
    /// 인자짜리 오버로드는 웨이브 스케일 계산 없이 data.maxHp를 그대로 쓰는 fallback이라,
    /// 디버그용으로 몬스터 하나만 즉석에서 스폰해볼 때만 쓸 것 - 실제 웨이브 진행 중에는 항상
    /// RunWaveSpawning을 거쳐 2개 인자짜리 SpawnMonster(data, actualMaxHp)가 호출됨.
    ///
    /// [구조 조정 - 몬스터 전송("넘어온 몬스터")] MatchController.TryStartMatch()가 두 보드가
    /// 모두 준비된 시점에 SetOpponentSpawner로 양쪽 스포너를 서로 연결해줌(opponentSpawner
    /// 필드). 이 스포너 쪽에서 몬스터가 죽으면(MonsterPathFollower.Die 참고)
    /// NotifyMonsterKilledForTransfer가 불려서 opponentSpawner.SpawnTransferredMonster로
    /// 전달되고, 상대 스포너가 "같은 종류·풀피" 몬스터 하나를 자기 웨이포인트에 추가로 스폰함
    /// (자기 자신이 스폰한 것과 구분하기 위해 isTransferred=true로 표시 - 전송된 개체가 다시
    /// 죽어도 또 전송되지 않게 하는 가드가 MonsterPathFollower 쪽에 있음). 웨이브당 최대
    /// 150마리 캡은 _transferredThisWave 카운터로 처리하고, 이 카운터는 RunWaves()가 새
    /// 웨이브로 넘어갈 때마다 초기화됨 - 캡 기준값은 "이 스포너가 현재 진행 중인 웨이브"의
    /// ownMonsterCount를 씀(_currentWave, RunWaveSpawning 진입 시 갱신).
    ///
    /// [구조 조정 - 80초 타이머 → 보스 전환] RunWaveSpawning으로 그 웨이브의 자기 진영 몬스터를
    /// 다 스폰한 직후(≈waveDuration 경과 시점) RunBossPhase를 돎 - 그 시점에 이 보드에 남아
    /// 살아있는 몬스터(전송받은 개체 포함, MonsterRegistry에서 Spawner==this로 필터링) 전원의
    /// 잔여 체력을 합산해서 wave.bossBaselineHp에 더하고, 보스 하나를 스폰한 뒤 그 보스가 죽을
    /// 때까지 기다렸다가 다음 웨이브로 넘어감. "양쪽 같은 보스"를 뽑는 실제 랜덤 굴리기는 이
    /// 스포너가 하지 않고 MatchController.Instance.RequestBossForWave(wave)에 위임함 -
    /// MatchController가 서버 권위로 WaveDataSO 하나당 한 번만 굴려서 캐싱하므로, 두 보드의
    /// 스포너가 같은 웨이브 자산(WaveDataSO 레퍼런스가 동일)에 대해 각자 호출해도 항상 같은
    /// 보스가 나옴(양쪽 보드가 같은 WaveDataSO 에셋 리스트를 공유하는 구성을 전제함).
    ///
    /// [구조 조정 - 계절/날씨 시스템] RunWaves()가 새 웨이브를 시작할 때마다
    /// MatchController.Instance.RequestWeatherForWave(wave)로 이 웨이브의 날씨를 확정하고
    /// (_currentWeather), 홍수처럼 "날씨가 뜨는 순간 1회성"인 효과는 여기서 바로 적용함
    /// (ApplyOneTimeKnockbackToMine). 그 외 몬스터 쪽 수치 효과(이동속도/도트/회복/보호막)는
    /// SpawnMonster가 스폰되는 모든 몬스터의 Init에 _currentWeather를 그대로 넘겨서 고정시키고,
    /// 실제 적용은 MonsterPathFollower/WeatherEffectTable이 담당함(단일 책임 유지).
    ///
    /// [체력 공식 변경 - 천명_통합.xlsx(2026-10) 기준] 웨이브 표(WaveDataSO.soldierBaselineHp/
    /// bossBaselineHp)는 더 이상 쓰지 않음. 병사 체력은 "성장 시계"(_growthSeconds, 웨이브 스폰 구간에서만
    /// 흐르고 보스전·웨이브 사이 대기에서는 멈춤) 기준 연속 함수 SoldierHpAt(t) = 120 × 1.52^(t/80)이고,
    /// 같은 웨이브 안에서도 스폰마다 조금씩 올라감. 일반 몬스터 체력 = SoldierHpAt × hpMultiplier,
    /// 보스 체력 = SoldierHpAt(웨이브 스폰 종료 시점) × 85 × 보스 hpMultiplier + 남은 몬스터 체력 합.
    /// 전송받은 몬스터는 원래 체력을 그대로 유지함(NotifyMonsterKilledForTransfer 참고).
    ///
    /// [이동속도 단위] MonsterDataSO.moveSpeed는 "칸/초"임(엑셀 속도값 ÷ 10). 월드 단위 속도는
    /// moveSpeed × moveSpeedMultiplier × 칸 크기(SetCellSize)로 MonsterPathFollower가 계산함.
    /// </summary>
    public class MonsterSpawner : MonoBehaviour
    {
        // 엑셀 몬스터 시트 기준 체력 곡선 상수 - 밸런스 수정 시 여기만 고치면 됨.
        public const float SoldierBaseHp = 120f;        // t=0 병사 체력
        public const float HpGrowthPerPeriod = 1.52f;   // 80초마다 곱해지는 배율
        public const float HpGrowthPeriodSeconds = 80f; // 성장 주기(웨이브 길이와 같지만 별개 상수)
        public const float BossHpSoldierMultiple = 85f; // 보스 기본 체력 = 웨이브 종료 시점 병사 체력 × 85

        /// <summary>성장 시계 t초 시점의 병사 체력 - 스폰마다 1회 계산이라 Mathf.Pow 비용은 무시 가능.</summary>
        public static float SoldierHpAt(float growthSeconds)
        {
            return SoldierBaseHp * Mathf.Pow(HpGrowthPerPeriod, growthSeconds / HpGrowthPeriodSeconds);
        }

        [Tooltip("몬스터가 따라갈 경로 - 웨이포인트 순서대로 이동함. NavMesh 도입 여부는 기획 미확정")]
        [SerializeField] private Transform[] waypoints;

        [Tooltip("진행할 웨이브 목록 - 순서대로 재생됨")]
        [SerializeField] private List<WaveDataSO> waves = new();

        [Tooltip("상대 보드의 스포너 - 몬스터 전송(\"넘어온 몬스터\")용. MatchController.TryStartMatch가 " +
                  "두 보드 모두 준비되면 자동으로 서로 연결해줌 - 인스펙터에서 수동으로 채울 필요 없음.")]
        [SerializeField] private MonsterSpawner opponentSpawner;

        // 몬스터 종류별 비활성 인스턴스 큐. 활성화된(현재 전투 중인) 인스턴스는 여기 안 들어있음.
        // 서버에서만 채워지고 쓰임(클라이언트는 스폰을 직접 안 하니까 풀이 필요 없음).
        private readonly Dictionary<MonsterDataSO, Queue<GameObject>> _pool = new();

        private Coroutine _waveRoutine;
        private WaveDataSO _currentWave; // 몬스터 전송 캡(ownMonsterCount) 계산용 - RunWaveSpawning 진입 시 갱신
        private int _transferredThisWave; // 이번 웨이브에서 상대로부터 받아 스폰한 개체 수 - 매 웨이브 시작 시 0으로 리셋
        private int _waveNumber;
        private float _growthSeconds; // 체력 성장 시계(초) - 웨이브 스폰 구간에서만 증가, 보스전 중 정지
        private float _cellSize = 1f;  // 월드 단위 칸 크기 - MatchController가 SetCellSize로 주입(이동속도 환산용)
        private WeatherType _currentWeather; // 이번 웨이브의 날씨(MatchController.RequestWeatherForWave) - 이 웨이브에서 스폰되는 모든 몬스터에 고정으로 물려줌

        private bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>몬스터가 경로 끝(기지)에 도달했을 때 발생(서버에서만 발생함) - BaseHealth가 구독해서 체력을 깎음.</summary>
        public event Action<MonsterDataSO> OnMonsterReachedEndEvent;

        /// <summary>마지막 웨이브까지 전부 스폰 완료됐을 때 1회 발생(서버에서만 발생함, 아직 살아있는 몬스터가 남아있을 수 있음).</summary>
        public event Action OnAllWavesSpawned;

        /// <summary>처치/보스 도달로 이 보드가 SP를 받을 때 발생(서버 전용) - MatchController가 이 보드의 지갑(AddSP)에 연결함.</summary>
        public event Action<float> OnSpRewardEvent;

        /// <summary>지금 진행 중인 웨이브 번호(1부터, 시작 전 0) - 처치 보상의 웨이브 스케일 기준.</summary>
        public int CurrentWaveNumber => _waveNumber;

        private void OnDisable()
        {
            // 씬이 꺼지는 도중에도 코루틴이 계속 돌면서 파괴된 오브젝트를 참조하는 걸 방지.
            if (_waveRoutine != null)
            {
                StopCoroutine(_waveRoutine);
                _waveRoutine = null;
            }
        }

        // MatchController(서버 권위)가 두 보드 모두 준비됐을 때 동시에 호출함 - "웨이브가 양쪽에서
        // 동시에 온다"는 요구사항이 이 동시 호출로 충족됨. 클라이언트에서 실수로 호출해도 무시됨.
        [ContextMenu("Debug: Start Waves")]
        public void StartWaves()
        {
            if (!IsServer)
            {
                Debug.LogWarning("[MonsterSpawner] 서버가 아닌데 StartWaves가 호출됨 - 무시함.");
                return;
            }
            if (_waveRoutine != null)
            {
                Debug.LogWarning("[MonsterSpawner] 이미 웨이브가 진행 중임.");
                return;
            }
            _growthSeconds = 0f;
            _waveNumber = 0;
            _waveRoutine = StartCoroutine(RunWaves());
        }

        private IEnumerator RunWaves()
        {
            foreach (var wave in waves)
            {
                if (wave == null) continue;

                _transferredThisWave = 0; // 웨이브당 캡이라 새 웨이브 시작 시 리셋
                _waveNumber++;

                // 계절/날씨(천명.pptx 슬라이드 12/13) - 이 웨이브의 날씨를 서버 권위로 확정함
                // (MatchController가 WaveDataSO 레퍼런스 기준으로 캐싱하므로 상대 보드 스포너가
                // 뒤이어 요청해도 항상 같은 날씨를 받아감 - 보스 선택과 동일한 구조).
                _currentWeather = MatchController.Instance != null
                    ? MatchController.Instance.RequestWeatherForWave(wave)
                    : WeatherType.Clear;

                // 홍수: 날씨가 뜨는 순간 1회성으로 이 보드에 살아있는 몬스터 전원을 넉백함(사용자
                // 확인: 지속 효과 아니고 1회성) - 정상적으로는 이전 웨이브 보스가 이미 죽어서
                // 대기(RunBossPhase의 WaitUntil)가 끝난 뒤라 살아있는 몬스터가 거의 없는 시점이지만,
                // 혹시 남아있는 개체가 있다면 여기서 함께 밀려남.
                if (WeatherEffectTable.HasOneTimeKnockbackOnStart(_currentWeather))
                {
                    ApplyOneTimeKnockbackToMine();
                }

                yield return new WaitForSeconds(wave.delayBeforeWave);
                yield return RunWaveSpawning(wave);
                yield return RunBossPhase(wave);
            }

            _waveRoutine = null;
            OnAllWavesSpawned?.Invoke();
        }

        // 웨이브 하나 안에서 waveDuration(기본 80초) 동안 ownMonsterCount(기본 150마리)를
        // monsterPool에서 가중치 랜덤으로 뽑아 균등 간격으로 스폰함 - 예전엔 "몬스터 종류별
        // entries를 순서대로 다 스폰"했는데, 이제는 "매 스폰마다 어떤 종류가 나올지 랜덤"이라
        // 이 방식으로 바꿈(천명.pptx 기준: 병사 70% · 기마병사 20% · 방패병사 10%).
        private IEnumerator RunWaveSpawning(WaveDataSO wave)
        {
            _currentWave = wave; // 몬스터 전송 캡(ownMonsterCount) 계산 기준 - 이 웨이브가 끝날 때까지 유지

            if (wave.monsterPool == null || wave.monsterPool.Count == 0)
            {
                Debug.LogWarning($"[MonsterSpawner] 웨이브 '{wave.waveName}'에 monsterPool이 비어있어서 스폰할 몬스터가 없음.");
                yield break;
            }

            float interval = wave.ownMonsterCount > 0 ? wave.waveDuration / wave.ownMonsterCount : 0f;
            for (int i = 0; i < wave.ownMonsterCount; i++)
            {
                var monster = PickWeightedRandom(wave.monsterPool);
                if (monster != null)
                {
                    float actualHp = SoldierHpAt(_growthSeconds) * monster.hpMultiplier;
                    SpawnMonster(monster, actualHp);
                }
                if (interval > 0f)
                {
                    yield return new WaitForSeconds(interval);
                    _growthSeconds += interval; // 웨이브 길이만큼만 누적됨(보스전/웨이브 사이 대기에서는 안 흐름)
                }
            }
        }

        // MonsterDataSO.spawnWeight(%) 비율대로 하나를 뽑음. 가중치 합이 0 이하면(전부 0으로
        // 잘못 세팅된 경우) 방어적으로 첫 항목을 반환함 - 조용히 스폰이 멈추는 것보단 나음.
        private static MonsterDataSO PickWeightedRandom(List<MonsterDataSO> pool)
        {
            float totalWeight = 0f;
            foreach (var m in pool)
            {
                if (m != null) totalWeight += Mathf.Max(0f, m.spawnWeight);
            }

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("[MonsterSpawner] monsterPool의 spawnWeight 합이 0 이하임 - 첫 항목을 그대로 씀.");
                return pool.Count > 0 ? pool[0] : null;
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (var m in pool)
            {
                if (m == null) continue;
                cumulative += Mathf.Max(0f, m.spawnWeight);
                if (roll <= cumulative) return m;
            }

            return pool[pool.Count - 1];
        }

        // 자기 진영 몬스터(+전송받은 몬스터 포함) 스폰이 끝난 뒤(≈waveDuration 경과) 호출됨.
        // 1) 이 보드에 남아 살아있는 몬스터 전원의 잔여 체력을 합산, 2) MatchController에 이
        // 웨이브의 보스를 요청(서버 권위, 양쪽 동일 보스), 3) 합산 체력을 얹어서 보스 스폰,
        // 4) 그 보스가 죽을 때까지 대기 - 대기가 끝나야 다음 웨이브로 넘어감(RunWaves 참고).
        private IEnumerator RunBossPhase(WaveDataSO wave)
        {
            float leftoverHp = SumAliveHpOfMine();

            var bossData = MatchController.Instance != null
                ? MatchController.Instance.RequestBossForWave(wave)
                : null;

            if (bossData == null)
            {
                Debug.LogWarning($"[MonsterSpawner] 웨이브 '{wave.waveName}' 보스를 정할 수 없어서(bossPool 비어있음/MatchController 없음) 보스전을 건너뜀.");
                yield break;
            }

            float bossHp = SoldierHpAt(_growthSeconds) * BossHpSoldierMultiple * bossData.hpMultiplier + leftoverHp;
            SpawnMonster(bossData, bossHp);

            // 보스는 1마리뿐이라 인스턴스 참조를 직접 들고 대기해도 되지만, 풀링 특성상
            // "이 스포너 소속의 살아있는 보스가 하나도 없다"로 판단하는 쪽이 반납/재사용 타이밍과
            // 더 안전하게 맞물림(인스턴스가 풀로 반납된 뒤에도 낡은 참조를 들고 있을 걱정이 없음).
            yield return new WaitUntil(() => !AnyAliveBossOfMine());
        }

        private float SumAliveHpOfMine()
        {
            float sum = 0f;
            foreach (var follower in MonsterRegistry.GetAll())
            {
                if (follower != null && follower.Spawner == this && follower.IsAlive)
                {
                    sum += follower.CurrentHp;
                }
            }
            return sum;
        }

        // 홍수 날씨 1회성 넉백(RunWaves 참고) - 이 보드 소속으로 등록된 살아있는 몬스터 전원에게
        // MonsterPathFollower.ApplyKnockback을 한 번씩만 호출함(지속 효과가 아니므로 여기서만 호출됨).
        private void ApplyOneTimeKnockbackToMine()
        {
            foreach (var follower in MonsterRegistry.GetAll())
            {
                if (follower != null && follower.Spawner == this && follower.IsAlive)
                {
                    follower.ApplyKnockback();
                }
            }
        }

        private bool AnyAliveBossOfMine()
        {
            foreach (var follower in MonsterRegistry.GetAll())
            {
                if (follower != null && follower.Spawner == this && follower.IsAlive &&
                    follower.Data != null && follower.Data.isBoss)
                {
                    return true;
                }
            }
            return false;
        }

        // 웨이브 스케일 계산 없이 즉석 스폰해볼 때 쓰는 편의 오버로드(디버그용) - data.maxHp를
        // 그대로 씀. 실제 웨이브 진행 중에는 RunWaveSpawning이 항상 아래 2개 인자짜리
        // SpawnMonster(data, actualMaxHp)를 호출함.
        public void SpawnMonster(MonsterDataSO data)
        {
            SpawnMonster(data, data != null ? data.maxHp : 0f);
        }

        public void SpawnMonster(MonsterDataSO data, float actualMaxHp)
        {
            SpawnMonster(data, actualMaxHp, isTransferred: false);
        }

        // isTransferred: 상대 보드에서 전송돼 넘어온 개체면 true(SpawnTransferredMonster 전용 -
        // 자기 웨이브 정상 스폰은 항상 위 2개 인자짜리 오버로드를 거쳐 false로 들어옴).
        private void SpawnMonster(MonsterDataSO data, float actualMaxHp, bool isTransferred)
        {
            if (!IsServer) return;
            if (data == null || data.prefab == null || waypoints == null || waypoints.Length == 0)
            {
                Debug.LogWarning("[MonsterSpawner] 몬스터 데이터/프리팹/웨이포인트가 준비 안 됨.");
                return;
            }

            var instance = GetFromPool(data);
            instance.SetActive(true);

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError($"[MonsterSpawner] {data.displayName} 프리팹에 NetworkObject 컴포넌트가 없음 - 네트워크로 안 보임. 프리팹에 NetworkObject를 추가할 것.");
                return;
            }
            if (!networkObject.IsSpawned)
            {
                networkObject.Spawn(); // 처음 꺼낸 인스턴스거나, 풀에서 재사용하며 Despawn(false)됐던 걸 다시 등록
            }

            instance.GetComponent<MonsterPathFollower>().Init(data, actualMaxHp, waypoints, this, isTransferred, _currentWeather, _cellSize);
        }

        /// <summary>월드 단위 칸 크기 주입(MatchController.TryStartMatch) - moveSpeed(칸/초)를 월드 속도로 환산할 때 씀.</summary>
        public void SetCellSize(float cellSize)
        {
            if (cellSize > 0f) _cellSize = cellSize;
        }

        /// <summary>MatchController.TryStartMatch()가 두 보드 모두 준비된 시점에 서로를 연결해줌 -
        /// 인스펙터 수동 배선 불필요. null을 넣으면(예: 상대 보드 파괴 후 정리) 전송이 조용히 꺼짐.</summary>
        public void SetOpponentSpawner(MonsterSpawner opponent)
        {
            opponentSpawner = opponent;
        }

        /// <summary>MonsterPathFollower.Die()가 부름(서버 전용, 전송받은 개체가 아닐 때만) - 이 스포너
        /// 쪽에서 몬스터 하나가 죽었으니 상대 스포너에 "같은 종류·풀피" 한 마리를 넘겨달라고 알림.</summary>
        public void NotifyMonsterKilledForTransfer(MonsterDataSO data, float fullHp)
        {
            if (!IsServer) return;
            if (opponentSpawner == null) return; // 아직 매치 시작 전(MatchController 배선 전)이거나 상대가 없는 상태
            opponentSpawner.SpawnTransferredMonster(data, fullHp);
        }

        /// <summary>상대 스포너의 NotifyMonsterKilledForTransfer를 통해서만 호출됨 - 웨이브당
        /// ownMonsterCount(기획 기준 150)를 캡으로 두고, 캡을 넘으면 조용히 무시함(전송 실패를
        /// 에러로 취급할 이유가 없음 - 기획상 정상적으로 발생하는 상한).</summary>
        public void SpawnTransferredMonster(MonsterDataSO data, float fullHp)
        {
            if (!IsServer) return;

            int cap = _currentWave != null ? _currentWave.ownMonsterCount : 0;
            if (_transferredThisWave >= cap) return;

            _transferredThisWave++;
            SpawnMonster(data, fullHp, isTransferred: true);
        }

        private GameObject GetFromPool(MonsterDataSO data)
        {
            if (_pool.TryGetValue(data, out var queue) && queue.Count > 0)
            {
                return queue.Dequeue();
            }

            // 서버에서 Instantiate한 인스턴스만 나중에 Spawn()으로 네트워크에 등록될 수 있음.
            var instance = Instantiate(data.prefab, transform);
            return instance;
        }

        // MonsterPathFollower(죽었거나 경로 끝에 도달한 개체, 서버에서만 호출)가 부름 - 밖에서 직접 부를 일은 없음.
        public void ReturnToPool(MonsterDataSO data, GameObject instance)
        {
            if (!IsServer) return;

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(destroy: false); // 네트워크 등록만 해제, 인스턴스는 재사용을 위해 유지
            }
            instance.SetActive(false);

            if (!_pool.TryGetValue(data, out var queue))
            {
                queue = new Queue<GameObject>();
                _pool[data] = queue;
            }
            queue.Enqueue(instance);
        }

        public void OnMonsterReachedEnd(MonsterDataSO data)
        {
            if (!IsServer) return;
            OnMonsterReachedEndEvent?.Invoke(data);

            // 엑셀: 보스는 "못 잡으면 절반" - 기지까지 간 보스는 처치 SP의 50%만 지급함(일반 몬스터는 0).
            if (data != null && data.isBoss)
            {
                float sp = data.GetKillSp(_waveNumber) * 0.5f;
                if (sp > 0f) OnSpRewardEvent?.Invoke(sp);
            }
        }

        /// <summary>MonsterPathFollower.Die()가 부름(서버 전용, 전송받은 개체 포함) - 웨이브 스케일 처치 SP 지급.</summary>
        public void NotifyMonsterKilled(MonsterDataSO data)
        {
            if (!IsServer || data == null) return;
            float sp = data.GetKillSp(_waveNumber);
            if (sp > 0f) OnSpRewardEvent?.Invoke(sp);
        }
    }
}
