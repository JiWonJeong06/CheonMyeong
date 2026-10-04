using UnityEngine;
using Unity.Netcode;
using TowerDefense.Data;
using TowerDefense.Monsters;

namespace TowerDefense.Map
{
    /// <summary>
    /// 배치된 타워(=소환된 캐릭터) 하나의 전투 로직. CharacterDataSO의 baseDamage/range/attackSpeed를
    /// 그대로 가져다 씀 - 캐릭터 스키마는 관리 범위 밖이라 이 스크립트 쪽에서 값을 해석만 하고
    /// 필드를 새로 추가하진 않음(제네릭 비주얼 프리팹 하나에 이 컴포넌트를 붙이고, 캐릭터별 전용
    /// 프리팹을 따로 만들지 않는 이유도 동일함 - CharacterDataSO에 프리팹 참조 필드가 없어서).
    ///
    /// 타겟팅은 MonsterRegistry(정적 레지스트리)에서 살아있는 몬스터 전체를 순회하며 range 안에서
    /// 가장 가까운 대상을 고르는 단순 구현임 - 몬스터 수가 아주 많아지면(수백 이상) 공간 분할(그리드/
    /// 쿼드트리)로 바꿔야 할 수 있지만, 타워 디펜스 특성상 화면에 동시에 그렇게 많은 몬스터가 있을
    /// 일은 드물어서 지금은 이 정도로 충분함. Distance 대신 sqrMagnitude로 비교해서 제곱근 연산을 피함.
    ///
    /// [네트워킹] NetworkBehaviour로 바꿈 - 타워는 위치가 안 바뀌니 NetworkTransform은 필요 없고
    /// (MatchController가 NetworkObject.Spawn() 할 때 넘긴 위치 그대로 고정), 전투 판단(타겟팅/
    /// 데미지 적용)만 서버 권위로 실행함(IsServer 가드). MonsterRegistry에 등록된 몬스터도 서버
    /// 쪽 사본이 IsAlive 등 진짜 상태를 갖고 있으니 서버에서 계산하는 이 로직이 항상 정확함.
    ///
    /// [구조 조정 - 계절/날씨 시스템] 공격속도/공격력/사거리에 MatchController.CurrentWeather
    /// (서버가 웨이브마다 갱신하는 전역 NetworkVariable)를 매번 최신값으로 읽어 적용함 - 타워는
    /// 몬스터와 달리 웨이브 경계를 넘어 계속 존재하므로 스폰 시점 고정이 아니라 실시간 조회가
    /// 맞음. 실제 수치는 TowerDefense.Data.WeatherEffectTable에 모아둠(단일 진실 공급원).
    /// </summary>
    public class TowerUnit : NetworkBehaviour
    {
        [Tooltip("비워두면 즉시 히트스캔 공격, 지정하면 이 프리팹을 발사하는 투사체 공격")]
        [SerializeField] private Projectile projectilePrefab;

        [Tooltip("투사체 공격일 때 발사 시작 위치 - 비워두면 이 오브젝트의 transform을 그대로 씀")]
        [SerializeField] private Transform muzzle;

        private CharacterDataSO _data;
        private float _cooldownRemaining;

        // [버그 수정 - 2026-09-29] 1:1 대전인데 타겟팅이 MonsterRegistry(양쪽 보드 몬스터가 전부
        // 섞여 들어가는 전역 정적 레지스트리)를 보드 구분 없이 그냥 "가장 가까운 놈"으로만 뒤졌음.
        // 천명 맵(태극 모양)은 두 보드의 몬스터 경로가 화면 중앙 부근에서 실제로 겹치거나 거의
        // 붙어있어서(InGame.unity 웨이포인트 좌표 확인 결과 일부는 완전히 동일 좌표), 중앙 근처에
        // 배치된 타워는 상대 보드 몬스터까지 사거리에 들어와 공격해버릴 수 있었음 - "내 타워 딜이
        // 왜 이렇게 약하지" 체감의 실제 원인 중 하나(상대 몬스터를 때리면 그 데미지는 내 기지 방어에
        // 아무 도움이 안 됨). 그래서 이 타워가 어느 보드 소속인지(_ownBoardSpawner)를 배치 시점에
        // 받아서, 그 보드의 MonsterSpawner가 스폰한 몬스터(MonsterPathFollower.Spawner로 판별)만
        // 타겟팅하도록 필터링함.
        private MonsterSpawner _ownBoardSpawner;

        // [사거리 단위 - 2026-10-04] CharacterDataSO.range는 "월드 유닛"이 아니라 "타일맵 칸 수"
        // (사거리 1 = 인접 1칸)로 해석함. 칸 크기가 Grid마다 다를 수 있어(현재 0.55) 배치 시점에
        // 그 보드의 칸 크기(월드 유닛)를 받아 두고, 타겟팅 때 range * 칸 크기로 환산함.
        private float _cellSize = 1f;
        private TowerStatModifiers _mods = TowerStatModifiers.Identity; // default(struct)는 배율 0이라 반드시 Identity로 초기화
        private const float RangeToleranceCells = 0.25f; // 몬스터가 웨이포인트 주변에서 흔들려(0.02~0.1) 인접 칸 중심 거리가 사거리 경계를 넘는 것 방지 (칸 크기 비례)

        // [임시 시각 구분 - 2026-09-29] CharacterDataSO 5종 전부 iconSprite/전용 프리팹이 없어서
        // (제네릭 비주얼 프리팹 하나 재사용 - 이 클래스 doc 참고) 배치된 타워가 전부 똑같은 흰색
        // 스프라이트로 보여 "뭐가 뭔지 모르겠다"는 문제가 있었음. 실제 캐릭터별 아트가 나오기
        // 전까지 임시로 characterId 해시 기반 고정 색상을 입혀서 최소한 "이건 1번 캐릭터, 저건
        // 3번 캐릭터"처럼 구분은 가능하게 함(같은 캐릭터는 항상 같은 색). 나중에 CharacterDataSO에
        // 실제 iconSprite/프리팹이 채워지면 이 로직은 자연히 안 쓰게 되므로(스프라이트 자체가
        // 바뀌면 굳이 단색 틴트가 필요 없어짐) 되돌릴 코드가 따로 필요 없음.
        //
        // [네트워킹] SpriteRenderer.color를 서버에서 직접 바꾸면 호스트 화면에만 반영되고 순수
        // 클라이언트 화면엔 절대 안 보임(NetworkTransform처럼 자동 복제되는 필드가 아님) - 그래서
        // MatchController.CurrentWeather와 같은 패턴으로 NetworkVariable을 씀. RPC 1회성 브로드
        // 캐스트(PlayHitFlashRpc 같은) 대신 NetworkVariable을 고른 이유: 이 색은 "한 번 반짝이고
        // 끝"이 아니라 타워가 존재하는 내내 유지돼야 하는 상태라서, 나중에 재연결/늦은 옵저버가
        // 생겨도 자동으로 현재 값이 동기화되는 NetworkVariable 쪽이 더 안전함.
        private readonly NetworkVariable<Color> _tintColor =
            new NetworkVariable<Color>(Color.white);
        private SpriteRenderer _spriteRenderer;

        public CharacterDataSO Data => _data;

        // [5단계 - 합성/강화] 합성 별(1~5, 서버가 쓰고 전원이 읽음 - 화면 표시용)과 SP 강화 레벨(서버 전용 캐시).
        // 합성·강화를 반영한 기본 공격력/간격은 RefreshStats()가 _damage/_interval에 캐싱함(공격마다 재계산 안 함).
        private readonly NetworkVariable<int> _stars = new NetworkVariable<int>(1);
        private int _enhanceLevel = 1;
        private float _damage;
        private float _interval = 1f;
        private TextMesh _starLabel;

        public int Stars => _stars.Value;
        public int EnhanceLevel => _enhanceLevel;
        public MonsterSpawner OwnBoardSpawner => _ownBoardSpawner;

        // 필드의 모든 타워(서버/클라이언트 공통, 스폰/디스폰 시 등록/해제). 클라이언트는 PlacementGrid 점유 정보가
        // 없어서(서버만 MarkOccupied) "이 칸에 타워가 있나"를 이 목록으로 조회함(드래그 합성 입력용).
        private static readonly System.Collections.Generic.List<TowerUnit> s_all = new();
        public static System.Collections.Generic.IReadOnlyList<TowerUnit> All => s_all;

        // 도메인 리로드를 끈 에디터 설정에서 플레이 세션 사이에 정적 목록이 남는 것을 방지함.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_all.Clear();

        /// <summary>월드 좌표가 이 그리드의 해당 칸에 속하는 타워를 찾음(없으면 null). 서버/클라이언트 모두 동작.</summary>
        public static TowerUnit FindAtCell(PlacementGrid grid, Vector3Int cell)
        {
            if (grid == null) return null;
            for (int i = 0; i < s_all.Count; i++)
            {
                var t = s_all[i];
                if (t != null && grid.WorldToCell(t.transform.position) == cell) return t;
            }
            return null;
        }

        public override void OnNetworkSpawn()
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _tintColor.OnValueChanged += OnTintChanged;
            _stars.OnValueChanged += OnStarsChanged;
            s_all.Add(this);
            ApplyTint(_tintColor.Value); // 스폰 시점에 이미 값이 채워져 있을 수 있어 한 번 즉시 반영
            UpdateStarLabel(_stars.Value);
        }

        public override void OnNetworkDespawn()
        {
            _tintColor.OnValueChanged -= OnTintChanged;
            _stars.OnValueChanged -= OnStarsChanged;
            s_all.Remove(this);
        }

        private void OnTintChanged(Color previous, Color newColor) => ApplyTint(newColor);
        private void OnStarsChanged(int previous, int newValue) => UpdateStarLabel(newValue);

        /// <summary>[서버 전용] 합성 별/SP 강화 레벨을 반영해 스탯을 다시 계산함(합성·강화 직후, 배치 직후 호출).</summary>
        public void ApplyProgression(int stars, int enhanceLevel)
        {
            if (!IsServer || _data == null) return;
            _enhanceLevel = Mathf.Clamp(enhanceLevel, 1, TowerProgression.MaxEnhanceLevel);
            _stars.Value = Mathf.Clamp(stars, 1, TowerProgression.MaxStars);
            RefreshStats();
        }

        private void RefreshStats()
        {
            TowerProgression.ComputeBaseStats(_data, _stars.Value, _enhanceLevel, out _damage, out _interval);
        }

        // 임시 별 표시: 2★ 이상일 때 칸 우하단에 숫자를 띄움(전용 아트/UI가 나오면 교체). 한 번 만들어 재사용하고
        // 타워 오브젝트의 자식이라 디스폰 때 같이 파괴됨.
        private void UpdateStarLabel(int stars)
        {
            if (stars <= 1)
            {
                if (_starLabel != null) _starLabel.gameObject.SetActive(false);
                return;
            }

            if (_starLabel == null)
            {
                if (_spriteRenderer == null) _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
                var go = new GameObject("StarLabel");
                go.transform.SetParent(transform, false);

                Vector3 center = transform.position;
                float cell = 0.55f;
                if (_spriteRenderer != null)
                {
                    center = _spriteRenderer.bounds.center;
                    cell = Mathf.Max(0.1f, _spriteRenderer.bounds.size.x);
                }
                go.transform.position = center + new Vector3(cell * 0.35f, -cell * 0.35f, 0f);

                _starLabel = go.AddComponent<TextMesh>();
                _starLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _starLabel.fontSize = 32;
                _starLabel.characterSize = cell * 0.125f / Mathf.Max(0.0001f, transform.lossyScale.x);
                _starLabel.anchor = TextAnchor.MiddleCenter;
                _starLabel.color = Color.yellow;

                var meshRenderer = go.GetComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = _starLabel.font.material;
                if (_spriteRenderer != null)
                {
                    meshRenderer.sortingLayerID = _spriteRenderer.sortingLayerID;
                    meshRenderer.sortingOrder = _spriteRenderer.sortingOrder + 1;
                }
            }

            _starLabel.gameObject.SetActive(true);
            _starLabel.text = stars.ToString();
        }

        private void ApplyTint(Color color)
        {
            if (_spriteRenderer != null) _spriteRenderer.color = color;
        }

        // characterId 문자열을 안정적인 해시로 색상화함 - 같은 characterId면 언제(서버/클라, 세션
        // 재시작 포함) 어디서 계산해도 항상 같은 색이 나옴(런타임 랜덤이 아니라 결정적 계산이라
        // 재현 가능함). 채도/명도는 고정하고 색상(Hue)만 바꿔서 5종 모두 눈에 띄게 다른 색이 되고
        // 흰색/검은색처럼 배경과 안 어울리는 극단값이 안 나오게 함.
        private static Color ColorFromCharacterId(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return Color.white;

            int hash = 17;
            unchecked
            {
                foreach (char c in characterId) hash = hash * 31 + c;
            }
            float hue = (hash & 0xFFFF) / 65536f;
            return Color.HSVToRGB(hue, 0.65f, 1f);
        }

        // 배치 직후 MatchController(서버)가 스폰과 함께 호출함 - CharacterDataSO 하나만 넘기면 이
        // 타워가 스스로 자기 스탯을 해석해서 공격하는 구조라, 나중에 캐릭터 종류가 늘어나도 이
        // 스크립트는 고칠 필요 없음(데이터 주도 설계). ownBoardSpawner: 이 타워가 배치된 보드의
        // MonsterSpawner(PlayerBoard.Spawner) - 타겟팅을 그 보드 소속 몬스터로만 한정하는 데 씀.
        public void Init(CharacterDataSO data, MonsterSpawner ownBoardSpawner, float cellSize = 1f)
        {
            Init(data, ownBoardSpawner, cellSize, TowerStatModifiers.Identity);
        }

        // modifiers: 노드 트리 계층(캐릭터 레벨 + 소속/전체 공격력·공속 노드) 배율 - MatchController가 배치한
        // 플레이어의 PlayerLoadout으로 계산해서 넘김. 합성/SP 강화 배율은 정지원 파트라 여기엔 없음.
        public void Init(CharacterDataSO data, MonsterSpawner ownBoardSpawner, float cellSize, TowerStatModifiers modifiers)
        {
            _data = data;
            _mods = modifiers;
            _ownBoardSpawner = ownBoardSpawner;
            _cellSize = cellSize > 0f ? cellSize : 1f;
            _cooldownRemaining = 0f;
            _enhanceLevel = 1;
            RefreshStats(); // 합성 1★/강화 Lv1 기준 - 이후 MatchController가 ApplyProgression으로 실제 강화 레벨을 반영함

            // NetworkVariable 쓰기는 서버 권위 - Init 자체가 RequestPlaceTowerRpc(서버 전용
            // 실행 경로) 안에서만 호출되지만, 혹시 모를 오용에 대비해 방어적으로 한 번 더 확인함.
            if (IsServer) _tintColor.Value = ColorFromCharacterId(data.characterId);

        }

        private void Update()
        {
            if (!IsServer) return; // 클라이언트 쪽 사본은 그냥 서 있는 모습만 보여주면 됨
            if (_data == null) return;
            if (_data.isSupport) return; // 버프형은 기본 공격을 하지 않음(스킬·궁극기만 사용 - 엑셀 구현 규칙)

            _cooldownRemaining -= Time.deltaTime;
            if (_cooldownRemaining > 0f) return;

            var target = FindNearestMonsterInRange();
            if (target == null)
            {
                return;
            }

            Attack(target);

            // attackInterval = 공격 간격(초, 낮을수록 빠름 - 엑셀 규칙). 공속 증가는 '간격 ÷ (1 + n%)'라서
            // 날씨 공속 배율(1.2 = 20% 증가, 0.9 = 10% 감소)로 나누면 같은 결과임. 0 이하로 잘못
            // 설정된 데이터가 들어와도 무한루프 없이 안전하게 막아둠.
            // 계절/날씨(미세먼지 10% 감소, 바람 20% 증가)를 곱함 - 타워는 웨이브 경계를 넘어 계속
            // 존재하므로(몬스터와 달리 스폰 시점 고정이 아니라) MatchController.CurrentWeather를
            // 매번 최신값으로 읽음(WeatherEffectTable 참고).
            float weatherAttackSpeedMult = CurrentWeatherMultiplierSafe(WeatherEffectTable.AttackSpeedMultiplier);
            _cooldownRemaining = _interval > 0f && weatherAttackSpeedMult > 0f
                ? _interval * _mods.attackIntervalMultiplier / weatherAttackSpeedMult
                : 1f;
        }

        // MatchController.Instance는 매치 시작 전(로비/씬 전환 중)엔 아직 null일 수 있어서, 그럴 땐
        // 배율 없음(Clear 취급)으로 안전하게 처리함 - TowerUnit은 배치 직후부터 공격을 시도할 수
        // 있으니 매 프레임 null 체크를 여기 한 곳으로 모아둠.
        private static float CurrentWeatherMultiplierSafe(System.Func<WeatherType, float> lookup)
        {
            var mc = MatchController.Instance;
            var weather = mc != null ? (WeatherType)mc.CurrentWeather.Value : WeatherType.Clear;
            return lookup(weather);
        }

        private MonsterPathFollower FindNearestMonsterInRange()
        {
            MonsterPathFollower best = null;
            float bestPrimary = 0f;
            float bestProgress = 0f;
            var priority = _data.targetPriority;

            // 계절/날씨(안개 사거리 1 감소)를 반영한 유효 사거리 - 음수로 내려가지 않게 0에서 clamp함.
            var mc = MatchController.Instance;
            var weather = mc != null ? (WeatherType)mc.CurrentWeather.Value : WeatherType.Clear;
            float effectiveRange = Mathf.Max(0f, _data.range + WeatherEffectTable.RangeDelta(weather));

            // 칸 단위 사거리 → 월드 유닛 환산 (_cellSize 필드 주석 참고)
            float effectiveRangeWorld = (effectiveRange + RangeToleranceCells) * _cellSize;
            float rangeSqr = effectiveRangeWorld * effectiveRangeWorld;
            Vector3 myPos = transform.position;

            foreach (var monster in MonsterRegistry.GetAll())
            {
                if (monster == null || !monster.IsAlive) continue;
                // 보드 필터 - 위 _ownBoardSpawner 필드 주석 참고. _ownBoardSpawner가 아직 null인
                // 비정상 상태(Init 호출 누락)라면 안전하게 아무도 타겟팅하지 않음(잘못 채워진 채로
                // 상대 보드를 공격하는 것보다 "그 프레임에 공격 안 함"이 훨씬 안전한 실패 방식).
                if (_ownBoardSpawner == null || monster.Spawner != _ownBoardSpawner) continue;

                // 2D 프로젝트라 z 차이는 무시함 - 몬스터/타워의 z 오프셋(웨이포인트 z 등)이 3D 거리를 부풀려
                // 화면상 인접해도 사거리 밖으로 판정되는 문제를 막음.
                float dx = monster.Position.x - myPos.x;
                float dy = monster.Position.y - myPos.y;
                float sqrDist = dx * dx + dy * dy;
                if (sqrDist > rangeSqr) continue;

                // 우선순위(엑셀 F열): 맨 앞 = 경로 진행도가 가장 큰(기지에 가장 가까운) 적, 체력 높은/낮은 적 = 현재 체력 기준.
                // 동점이면 경로 진행도가 큰 쪽. 사거리 안 후보를 한 번만 훑어 처리함(할당 없음).
                float progress = monster.PathProgress;
                float primary = priority switch
                {
                    TargetPriority.HighestHp => monster.CurrentHp,
                    TargetPriority.LowestHp => -monster.CurrentHp,
                    _ => progress,
                };

                if (best == null || primary > bestPrimary || (primary == bestPrimary && progress > bestProgress))
                {
                    best = monster;
                    bestPrimary = primary;
                    bestProgress = progress;
                }
            }

            return best;
        }

        private void Attack(MonsterPathFollower target)
        {
            // 계절/날씨(폭염 15% 증가)를 반영한 실제 공격력 - 히트스캔/투사체 두 경로 모두 이 값을 씀.
            float damage = _damage * _mods.damageMultiplier * CurrentWeatherMultiplierSafe(WeatherEffectTable.AttackDamageMultiplier);


            if (projectilePrefab == null)
            {
                // [버그 수정 - 2026-09-29] "RpcException: The NetworkBehaviour must be spawned
                // before calling this method." - 예전엔 TakeDamage를 먼저 부르고 그다음에
                // PlayHitFlashRpc를 불렀는데, TakeDamage가 이 공격으로 몬스터를 죽이면
                // MonsterPathFollower.Die() → ReturnToPool() 안에서 NetworkObject.Despawn이
                // 그 자리에서(동기적으로) 실행됨 - 그러면 바로 다음 줄의 PlayHitFlashRpc가 이미
                // 스폰 해제된 NetworkBehaviour에 RPC를 보내려다 예외를 던짐(막타를 칠 때마다 매번
                // 발생). 순서를 뒤집어서 "아직 살아있는(스폰된) 상태"일 때 히트 플래시 RPC부터
                // 먼저 보내고, 그다음에 데미지를 적용(그 결과로 죽어서 반납되는 건 상관없음 -
                // 이 시점엔 이미 RPC가 전송된 뒤라서 안전함)하도록 고침.
                target.PlayHitFlashRpc();
                // 히트스캔: 사거리 안이면 바로 데미지 적용(투사체 이동 시간 없음).
                target.TakeDamage(damage);
                return;
            }

            Vector3 spawnPos = muzzle != null ? muzzle.position : transform.position;
            var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

            var networkObject = projectile.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[TowerUnit] projectilePrefab에 NetworkObject 컴포넌트가 없음 - 상대 화면에 안 보임.");
                Destroy(projectile.gameObject);
                return;
            }
            networkObject.Spawn();

            projectile.Launch(target, damage);
        }
    }
}
