using System.Collections;
using UnityEngine;
using Unity.Netcode;
using TowerDefense.Data;

namespace TowerDefense.Monsters
{
    /// <summary>
    /// 몬스터 프리팹에 붙는 컴포넌트 - 웨이포인트를 따라 이동하고, 체력이 0이 되거나 경로 끝에
    /// 도달하면 스포너(MonsterSpawner)에게 반납됨(오브젝트 풀링). 타워(TowerUnit)가 TakeDamage를
    /// 호출해서 공격함.
    ///
    /// [네트워킹] NetworkBehaviour로 바꿈 - 이동/전투 시뮬레이션은 서버에서만 계산하고
    /// (IsServer 가드), 실제 위치는 프리팹에 붙은 NetworkTransform 컴포넌트(Netcode 기본 제공,
    /// 인스펙터에서 추가해야 함)가 알아서 클라이언트로 복제해줌 - 그래서 클라이언트 쪽 사본은
    /// Update에서 아무 것도 안 하고 NetworkTransform이 넣어주는 위치를 그대로 따라가기만 함.
    /// 이렇게 하면 두 플레이어 화면에 항상 같은 몬스터 위치가 보임(서버 권위 - 클라이언트가
    /// 이동 속도 등을 조작해도 실제 판정엔 영향 없음).
    ///
    /// [구조 조정 - 천명.pptx(2026-09-28, 최신 기획서) 반영] 웨이브가 오를수록 체력이 오르는
    /// 기획이라, Init에 실제 스폰 체력(actualMaxHp - MonsterSpawner가 그 웨이브의
    /// soldierBaselineHp/bossBaselineHp × MonsterDataSO.hpMultiplier로 미리 계산해서 넘겨줌)을
    /// 추가로 받음 - data.maxHp를 그대로 쓰지 않게 됨(data.maxHp는 이제 웨이브 밖 참고용
    /// 기본값일 뿐, MonsterDataSO 쪽 주석 참고).
    ///
    /// [구조 조정 - 몬스터 전송("넘어온 몬스터")] 한쪽 보드에서 몬스터가 죽으면 상대 보드에
    /// 같은 종류·풀피(死 당시 최대 체력 기준)로 하나가 넘어가서 스폰됨(기획서 기준, 웨이브당
    /// 최대 150마리 - 캡 처리는 MonsterSpawner.SpawnTransferredMonster 참고). 넘어온 몬스터가
    /// 다시 죽었을 때 또 넘어가서 무한 증식하는 걸 막아야 해서 _isTransferred 플래그를 둠 -
    /// Die()에서 이 플래그가 true면 전송 트리거 자체를 안 함(ReachEnd로 기지에 닿아 대미지를
    /// 주는 것과는 별개 - 전송은 오직 "죽었을 때"만 발생, 기지 도달로는 전송 안 됨).
    ///
    /// [구조 조정 - 계절/날씨 시스템] Init이 스폰 시점의 날씨(_spawnWeather)를 한 번만 받아 고정함.
    /// 이동속도 배율(눈)은 Update()에서, 회복(꽃가루)/도트(우박)는 ApplyPollenHealIfNeeded/
    /// ApplyHailDotIfNeeded가, 보호막(비)은 Init에서 _shield로 부여하고 TakeDamage가 먼저 소모함,
    /// 넉백(홍수)은 날씨가 뜨는 순간 MonsterSpawner가 호출하는 ApplyKnockback이 1회성으로 처리함.
    /// 실제 수치는 전부 TowerDefense.Data.WeatherEffectTable에 모아둠(단일 진실 공급원).
    /// </summary>
    public class MonsterPathFollower : NetworkBehaviour
    {
        private MonsterDataSO _data;
        private Transform[] _waypoints;
        private MonsterSpawner _spawner;
        private float _cellSize = 1f; // moveSpeed(칸/초) → 월드 단위 환산용
        private float _actualMaxHp; // 스폰 당시 풀피 - 죽을 때 상대 보드로 넘길 "풀피" 값으로 재사용
        private bool _isTransferred; // true면 이미 상대 쪽에서 넘어온 개체 - 죽어도 재전송 안 함

        // 계절/날씨(천명.pptx 슬라이드 12/13) - 스폰 시점(Init)에 그 웨이브의 날씨를 한 번만 받아
        // 고정해서 씀(_spawnWeather). 몬스터는 웨이브 하나 안에서만 살아있으므로(다음 웨이브는 이전
        // 웨이브 보스가 죽어야 시작됨, MonsterSpawner.RunBossPhase 참고) 스폰 시점 고정과 매 프레임
        // 전역값 조회가 실질적으로 항상 같은 결과라, 더 가벼운 스폰 시점 고정 방식을 씀.
        private WeatherType _spawnWeather;
        private float _shield; // 비 날씨: 스폰 시 최대체력 10%만큼 부여, TakeDamage에서 먼저 소모됨
        private bool _pollenHealed; // 꽃가루 날씨: 체력 50% 미만 1회 회복 - 중복 회복 방지 플래그

        private int _waypointIndex;
        private float _currentHp;

        // [버그 수정 - 2026-09-29] 히트스캔 공격(TowerUnit.Attack, projectilePrefab 없는 경로)이
        // TakeDamage를 직접 불러서 데미지만 조용히 반영하고 아무 시각 효과도 없었음 - 그래서 실제로는
        // 정상적으로 공격/피해가 들어가고 있어도 화면상으로는 "타워가 몬스터를 안 잡는 것처럼" 보임
        // (실제 몬스터 색이 배경과 비슷해 눈에 잘 안 띄는 문제와 겹쳐서 더 그렇게 느껴짐). 그래서
        // 실제로 피해가 적용될 때마다(보호막에 전부 흡수되는 경우는 제외) 클라이언트에 짧은 흰색
        // 플래시를 브로드캐스트함 - 순수 시각 연출이라 IsServer 판정/HP 계산엔 영향 없음.
        private SpriteRenderer _spriteRenderer;
        private Color _baseColor;
        private Coroutine _hitFlashRoutine;

        // [전용 아트 적용 - 2026-10-04] SpriteFrameAnimator(프레임 애니메이션)가 붙은 프리팹은 실제
        // 아트를 쓰는 중이라, 임시 타입 색 틴트를 입히면 아트 색이 망가지므로 틴트를 흰색(=원본색)으로
        // 고정함. 흰색 원본 위에선 "흰색 플래시"가 안 보이므로 히트 플래시도 붉은 계열로 바꿈.
        private bool _hasArt;
        private static readonly Color ArtHitFlashColor = new Color(1f, 0.35f, 0.35f, 1f);

        // [임시 시각 구분 - 2026-09-29] 몬스터 종류가 병사/기마병사/방패병사/보스 9종 다 합쳐도
        // 전용 아트가 없어서 지금은 Monster_Placeholder.prefab 하나(공용 스프라이트)를 그대로
        // 재사용 중 - 그래서 종류가 달라도 화면에서 전혀 구분이 안 됨("뭐가 뭔지 모름" 피드백).
        // 실제 아트 나오기 전까지 monsterId 해시 기반 고정 색을 입혀서 "이 색은 항상 이 몬스터
        // 종류"처럼 최소한의 구분은 되게 함(TowerUnit.cs의 캐릭터별 틴트와 완전히 같은 패턴).
        // 몬스터는 오브젝트 풀링으로 재사용되며 매번 다른 MonsterDataSO로 Init될 수 있어서, 타입
        // 색은 Init 때마다 다시 계산해서 NetworkVariable에 씀 - _baseColor(히트 플래시 복원 기준색)도
        // 이 타입 색으로 같이 갱신됨(ApplyTypeTint 참고).
        //
        // [네트워킹] SpriteRenderer.color를 서버에서 직접 바꾸면 호스트 화면에만 반영되고 순수
        // 클라이언트 화면엔 안 보임 - MatchController.CurrentWeather와 같은 NetworkVariable
        // 패턴을 씀(PlayHitFlashRpc처럼 "한 번 반짝이고 끝"이 아니라 몬스터가 살아있는 내내
        // 유지돼야 하는 상태라 RPC 1회성 브로드캐스트보다 NetworkVariable이 더 맞음 - 늦게
        // 옵저버가 붙어도 자동으로 현재 값이 동기화됨).
        private readonly NetworkVariable<Color> _typeTintColor =
            new NetworkVariable<Color>(Color.white);

        public bool IsAlive => _currentHp > 0f;
        public Vector3 Position => transform.position;

        // 교전 시스템(80초 타이머 → 보스 전환) - MonsterSpawner가 "이 몬스터가 내 보드 소속인지"
        // 판단(Spawner==this로 필터링)하고 "잔여 체력 합산"에 쓰기 위해 공개함. Data는 isBoss
        // 판별(보스 처치 대기 폴링)에도 씀.
        public MonsterDataSO Data => _data;
        public MonsterSpawner Spawner => _spawner;
        public float CurrentHp => _currentHp;

        /// <summary>경로 진행도 - 클수록 기지에 가까움(타워 우선순위 "맨 앞" 판정용). 웨이포인트 인덱스가 주, 다음
        /// 웨이포인트까지 남은 거리가 부(가까울수록 큼). 매 호출 할당 없음.</summary>
        public float PathProgress
        {
            get
            {
                if (_waypoints == null || _waypointIndex >= _waypoints.Length) return _waypointIndex * 1000f;
                Vector3 d = _waypoints[_waypointIndex].position - transform.position;
                return _waypointIndex * 1000f - Mathf.Sqrt(d.x * d.x + d.y * d.y);
            }
        }

        // 오브젝트 풀링 특성상 SetActive(false/true)로 반납/재사용되므로, 등록/해제도 OnEnable/OnDisable에
        // 맡기면 Init 호출 누락이나 반납 시 해제 누락 걱정 없이 항상 활성 상태와 레지스트리가 일치함
        // (타워(TowerUnit)가 이 레지스트리로 타겟을 찾음 - MonsterRegistry 참고). 클라이언트 쪽
        // 사본도 등록되긴 하지만 TowerUnit.Update가 서버에서만 도니까 실질적으로 조회되진 않음.
        private void Awake()
        {
            // Instantiate 시점에 딱 한 번만 캐싱함(풀링으로 재사용돼도 Awake는 다시 안 불림) -
            // 매번 GetComponent를 부르는 것보다 가벼움. _baseColor는 일단 프리팹 기본색으로
            // 채워두고, 실제 타입 색은 Init()에서 _typeTintColor를 통해 갱신됨(ApplyTypeTint 참고).
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _hasArt = GetComponent<SpriteFrameAnimator>() != null;
            if (_spriteRenderer != null) _baseColor = _spriteRenderer.color;

            // 구독은 반드시 여기(Awake, 이 오브젝트 생애 전체에 딱 한 번만 실행됨)에서만 함 -
            // OnNetworkSpawn에서 구독하면 오브젝트 풀링으로 Despawn(destroy:false)→재Spawn을
            // 반복할 때마다 매번 새로 구독이 추가되어 콜백이 중복 호출되는 누수가 생김(짝 맞춰
            // OnNetworkDespawn에서 해제해도 되지만, Awake에서 한 번만 구독하는 쪽이 더 단순하고
            // 실수할 여지가 없음 - NetworkVariable 자체는 스폰 여부와 무관하게 존재하는 일반
            // C# 필드라 Awake 시점에 구독해도 아무 문제 없음).
            _typeTintColor.OnValueChanged += (_, newColor) => ApplyTypeTint(newColor);
        }

        // NetworkVariable 값이 바뀔 때마다(서버에서 로컬로 즉시, 클라이언트는 동기화 수신 시)
        // 호출됨 - _baseColor(히트 플래시 복원 기준색)도 함께 갱신해야, 타입이 다른 몬스터로
        // 재사용됐을 때 히트 플래시가 끝나고 "이전 타입 색"이 아니라 "지금 타입 색"으로 돌아옴.
        private void ApplyTypeTint(Color color)
        {
            if (_hasArt) color = Color.white; // 전용 아트는 원본 색 유지(_hasArt 주석 참고)
            _baseColor = color;
            if (_spriteRenderer != null) _spriteRenderer.color = color;
        }

        // monsterId 문자열을 안정적인 해시로 색상화함(TowerUnit.ColorFromCharacterId와 같은 방식) -
        // 같은 monsterId면 언제 계산해도 항상 같은 색이 나옴. 채도/명도는 고정하고 Hue만 바꿔서
        // 몬스터 종류가 늘어나도 서로 눈에 띄게 다른 색이 나오게 함.
        private static Color ColorFromMonsterId(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId)) return Color.white;

            int hash = 17;
            unchecked
            {
                foreach (char c in monsterId) hash = hash * 31 + c;
            }
            float hue = (hash & 0xFFFF) / 65536f;
            return Color.HSVToRGB(hue, 0.65f, 1f);
        }

        private void OnEnable() => MonsterRegistry.Register(this);
        private void OnDisable() => MonsterRegistry.Unregister(this);

        // 풀에서 꺼내 재사용할 때마다 스포너(서버)가 호출함 - 생성자 대신 이 방식을 쓰는 이유는
        // 오브젝트 풀링 특성상 Instantiate는 한 번만 일어나고 이후엔 이 Init만 반복 호출되기 때문.
        // actualMaxHp: 이번에 스폰될 실제 체력(웨이브 스케일 반영 완료된 값) - MonsterSpawner가
        // 계산해서 넘겨줌(웨이브 스케일 계산 자체는 이 클래스 책임이 아님 - 단일 책임 유지).
        // isTransferred: 상대 보드에서 넘어온 개체로 스폰되는 경우 true(MonsterSpawner.
        // SpawnTransferredMonster 참고) - 기본값 false라 기존 SpawnMonster(data, actualMaxHp)
        // 호출부(자기 웨이브 정상 스폰)는 그대로 둬도 됨.
        // weather: 스폰되는 시점의 웨이브 날씨(MonsterSpawner._currentWeather) - 기본값 Clear라
        // 기존 디버그 전용 SpawnMonster(data) 오버로드 호출부도 그대로 컴파일됨.
        public void Init(MonsterDataSO data, float actualMaxHp, Transform[] waypoints, MonsterSpawner spawner, bool isTransferred = false, WeatherType weather = WeatherType.Clear, float cellSize = 1f)
        {
            _data = data;
            _waypoints = waypoints;
            _spawner = spawner;
            _waypointIndex = 0;
            _currentHp = actualMaxHp;
            _actualMaxHp = actualMaxHp;
            _isTransferred = isTransferred;
            _cellSize = cellSize > 0f ? cellSize : 1f;

            // Init은 MonsterSpawner의 IsServer 가드 안에서만 호출되므로 사실상 항상 서버지만,
            // NetworkVariable 쓰기 권한 문서화 차원에서 방어적으로 한 번 더 확인함. 이 한 줄이
            // ApplyTypeTint를 즉시(서버 로컬) 호출해서 _baseColor까지 새 타입 색으로 갱신함 -
            // 아래 히트 플래시 잔여 코루틴 방어 코드보다 먼저 와야 그 방어 코드가 "이전 타입
            // 색"이 아니라 "지금 타입 색"으로 되돌리게 됨.
            if (IsServer) _typeTintColor.Value = ColorFromMonsterId(data.monsterId);

            _spawnWeather = weather;
            _shield = actualMaxHp * WeatherEffectTable.ShieldFractionOnSpawn(weather); // 비 날씨가 아니면 0
            _pollenHealed = false;

            // [방어 코드] 풀에서 재사용되기 직전에 히트 플래시 코루틴이 진행 중이었다면(SetActive(false)로
            // 즉시 끊기므로 흰색으로 복원하는 마지막 줄까지 못 가고 멈출 수 있음) 색이 흰색으로 남은 채
            // 다음 스폰에 재사용될 수 있어서, 매번 Init할 때 원래 색으로 명시적으로 되돌림.
            if (_spriteRenderer != null) _spriteRenderer.color = _baseColor;
            if (_hitFlashRoutine != null)
            {
                StopCoroutine(_hitFlashRoutine);
                _hitFlashRoutine = null;
            }

            if (_waypoints != null && _waypoints.Length > 0)
            {
                transform.position = _waypoints[0].position;
            }
        }

        private void Update()
        {
            if (!IsServer) return; // 클라이언트는 NetworkTransform이 복제해주는 위치만 따라감
            if (!IsAlive) return; // 날씨 상태 효과(도트 등)로 죽은 프레임에 아래 이동 로직까지 도는 걸 방지

            // 계절/날씨 상태 효과 - 이동보다 먼저 적용함(우박 도트로 이 프레임에 죽을 수 있어서,
            // 죽은 뒤에는 이동 계산을 할 필요가 없음 - 위 IsAlive 가드가 다음 줄부터를 막아줌).
            ApplyPollenHealIfNeeded();
            ApplyHailDotIfNeeded();
            if (!IsAlive) return; // 우박 도트로 이번 프레임에 죽었으면(Die()가 이미 반납 처리함) 더 진행 안 함

            if (_waypoints == null || _waypointIndex >= _waypoints.Length) return;

            // moveSpeedMultiplier 반영(기획 기준: 병사=1, 기마병사=1.6, 방패병사=0.6) - 예전엔 이
            // 배율이 계산에서 빠져있었음(MonsterDataSO에 필드만 추가되고 실제 이동 계산엔 안 쓰임).
            // 날씨 이동속도 배율(WeatherEffectTable.MoveSpeedMultiplier)도 함께 곱함 - 눈 날씨 15% 증가.
            Vector3 target = _waypoints[_waypointIndex].position;
            // moveSpeed는 "칸/초" 단위(엑셀 속도값 ÷ 10) - 월드 속도 = 칸/초 × 칸 크기.
            float speed = _data.moveSpeed * _data.moveSpeedMultiplier * _cellSize * WeatherEffectTable.MoveSpeedMultiplier(_spawnWeather);
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

            if (Vector3.Distance(transform.position, target) < 0.05f)
            {
                _waypointIndex++;
                if (_waypointIndex >= _waypoints.Length)
                {
                    ReachEnd();
                }
            }
        }

        /// <summary>[서버 전용] TowerUnit.Attack과 ApplyHailDotIfNeeded에서 호출됨. 비 날씨로 부여된
        /// 보호막(_shield)이 남아있으면 먼저 흡수하고, 남은 데미지만 체력에 적용함.</summary>
        public void TakeDamage(float amount)
        {
            if (!IsServer) return;
            if (!IsAlive) return; // 이미 죽고 반납 대기 중인 개체에 중복 데미지가 들어오는 걸 방지

            if (_shield > 0f)
            {
                float absorbed = Mathf.Min(_shield, amount);
                _shield -= absorbed;
                amount -= absorbed;
                if (amount <= 0f) return; // 보호막이 전부 흡수함
            }

            _currentHp -= amount;
            if (_currentHp <= 0f) Die();
        }

        // 꽃가루 날씨: 체력이 최대체력의 50% 미만이면 즉시 풀피로 1회만 회복시킴(_pollenHealed로
        // 중복 회복 방지). 이 날씨가 아니면 WeatherEffectTable.HasHealBelowHalfEffect가 false라
        // 매 프레임 아무 것도 안 하고 바로 리턴함(비용 거의 없음).
        private void ApplyPollenHealIfNeeded()
        {
            if (_pollenHealed) return;
            if (!WeatherEffectTable.HasHealBelowHalfEffect(_spawnWeather)) return;
            if (_currentHp >= _actualMaxHp * 0.5f) return;

            _currentHp = _actualMaxHp;
            _pollenHealed = true;
        }

        // 우박 날씨: 초당 최대체력의 3%를 지속 데미지로 입힘(TakeDamage를 그대로 거쳐서 보호막(비
        // 날씨와 동시 발생은 기획상 없지만) 처리 로직을 공유함). 이 날씨가 아니면 DotPercentPerSecond가
        // 0이라 TakeDamage(0)이 되고, TakeDamage 쪽에서 amount<=0 처리로 안전하게 끝남.
        private void ApplyHailDotIfNeeded()
        {
            float dotPercent = WeatherEffectTable.DotPercentPerSecond(_spawnWeather);
            if (dotPercent <= 0f) return;

            TakeDamage(_actualMaxHp * dotPercent * Time.deltaTime);
        }

        /// <summary>홍수 날씨가 뜨는 순간 MonsterSpawner.ApplyOneTimeKnockbackToMine이 1회성으로
        /// 호출함(지속 효과 아님, 사용자 확인). 정확한 넉백 거리가 기획서에 없어 웨이포인트 1개
        /// 뒤로 목표 지점을 되돌리는 것으로 잠정 구현함 - 이후 Update()의 MoveTowards가 알아서
        /// 그 이전 웨이포인트 쪽으로 되돌아가는 이동을 보여줌(밸런스팀 확정 필요, WeatherEffectTable 참고).</summary>
        public void ApplyKnockback()
        {
            if (!IsServer) return;
            if (_waypointIndex > 0) _waypointIndex--;
        }

        /// <summary>[서버 전용 호출] 타워의 실제 공격(TowerUnit.Attack 히트스캔, Projectile 명중)이
        /// 명중했을 때만 서버가 호출함 - 우박 도트처럼 매 프레임 반복되는 데미지에는 안 붙여서(그건
        /// TakeDamage를 직접 호출) 계속 깜빡이는 걸 방지함. 순수 시각 연출용 RPC라 IsServer 판정이나
        /// HP 계산과는 완전히 분리돼있음 - 클라이언트/호스트 화면 양쪽에 짧게 흰색으로 번쩍였다가
        /// 원래 색으로 돌아옴("타워가 실제로 맞히고 있다"는 걸 눈으로 확인할 수 있게 함).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void PlayHitFlashRpc()
        {
            if (_spriteRenderer == null) return;
            if (_hitFlashRoutine != null) StopCoroutine(_hitFlashRoutine);
            _hitFlashRoutine = StartCoroutine(HitFlashCoroutine());
        }

        private IEnumerator HitFlashCoroutine()
        {
            _spriteRenderer.color = _hasArt ? ArtHitFlashColor : Color.white;
            yield return new WaitForSeconds(0.08f);
            if (_spriteRenderer != null) _spriteRenderer.color = _baseColor;
            _hitFlashRoutine = null;
        }

        private void Die()
        {
            _spawner.NotifyMonsterKilled(_data); // 처치 SP 지급(이 몬스터가 속한 보드의 지갑으로)

            // TODO(경제 시스템): 예전엔 여기서 EconomyManager.Instance.AddGold(_data.goldReward)를
            // 호출했었는데, 이 메서드가 서버에서만 실행되는 구조로 바뀌면서 그대로 두면 "호스트를
            // 맡은 플레이어의 로컬 EconomyManager"만 양쪽 보드 킬 보상을 전부 받아가는 버그가 생김
            // (클라이언트 쪽 EconomyManager는 아예 갱신 안 됨). 몬스터 처치 보상을 메타 재화(Gold)로
            // 줄지, 인게임 SP로 줄지(PlayerBoard의 MatchResourceManager.AddSP), 아니면 아예 매치
            // 종료 후 결과 화면에서 한 번에 정산할지는 기획 확정 필요 - 그때까지 보상 지급은 비워둠.

            // 몬스터 전송("넘어온 몬스터") - 이미 상대 쪽에서 넘어온 개체(_isTransferred)는 다시
            // 넘기지 않음(안 그러면 두 보드가 서로 계속 몬스터를 주고받으며 무한 증식함). 풀피
            // 기준으로 넘기는 이유: 기획서상 "같은 종류·풀피"로 넘어가는 거지, 죽기 직전 잔여
            // 체력(0 이하)을 그대로 넘기면 상대 쪽에 체력 0짜리가 스폰되는 꼴이라 의미가 없음.
            // 보스(_data.isBoss)는 전송 대상에서 제외함 - "넘어온 몬스터"는 일반 병사 기획이고,
            // 보스는 웨이브당 각자 보드에서 별도로(잔여 체력 합산 포함) 뜨는 개체라 전송하면
            // 상대 보드에 의도치 않은 두 번째 보스가 생김.
            if (!_isTransferred && (_data == null || !_data.isBoss))
            {
                _spawner.NotifyMonsterKilledForTransfer(_data, _actualMaxHp);
            }

            _spawner.ReturnToPool(_data, gameObject);
        }

        private void ReachEnd()
        {
            _spawner.OnMonsterReachedEnd(_data);
            _spawner.ReturnToPool(_data, gameObject);
        }
    }
}
