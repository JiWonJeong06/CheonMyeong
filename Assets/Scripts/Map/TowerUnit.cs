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
    /// </summary>
    public class TowerUnit : NetworkBehaviour
    {
        [Tooltip("비워두면 즉시 히트스캔 공격, 지정하면 이 프리팹을 발사하는 투사체 공격")]
        [SerializeField] private Projectile projectilePrefab;

        [Tooltip("투사체 공격일 때 발사 시작 위치 - 비워두면 이 오브젝트의 transform을 그대로 씀")]
        [SerializeField] private Transform muzzle;

        private CharacterDataSO _data;
        private float _cooldownRemaining;

        public CharacterDataSO Data => _data;

        // 배치 직후 MatchController(서버)가 스폰과 함께 호출함 - CharacterDataSO 하나만 넘기면 이
        // 타워가 스스로 자기 스탯을 해석해서 공격하는 구조라, 나중에 캐릭터 종류가 늘어나도 이
        // 스크립트는 고칠 필요 없음(데이터 주도 설계).
        public void Init(CharacterDataSO data)
        {
            _data = data;
            _cooldownRemaining = 0f;
        }

        private void Update()
        {
            if (!IsServer) return; // 클라이언트 쪽 사본은 그냥 서 있는 모습만 보여주면 됨
            if (_data == null) return;

            _cooldownRemaining -= Time.deltaTime;
            if (_cooldownRemaining > 0f) return;

            var target = FindNearestMonsterInRange();
            if (target == null) return;

            Attack(target);

            // attackSpeed = 초당 공격 횟수로 해석함(예: 1.5면 1초에 1.5회 공격) - 0 이하로 잘못
            // 설정된 데이터가 들어와도 무한루프/0으로 나누기 없이 안전하게 막아둠.
            _cooldownRemaining = _data.attackSpeed > 0f ? 1f / _data.attackSpeed : 1f;
        }

        private MonsterPathFollower FindNearestMonsterInRange()
        {
            MonsterPathFollower nearest = null;
            float nearestSqrDist = _data.range * _data.range;
            Vector3 myPos = transform.position;

            foreach (var monster in MonsterRegistry.GetAll())
            {
                if (monster == null || !monster.IsAlive) continue;

                float sqrDist = (monster.Position - myPos).sqrMagnitude;
                if (sqrDist <= nearestSqrDist)
                {
                    nearestSqrDist = sqrDist;
                    nearest = monster;
                }
            }

            return nearest;
        }

        private void Attack(MonsterPathFollower target)
        {
            if (projectilePrefab == null)
            {
                // 히트스캔: 사거리 안이면 바로 데미지 적용(투사체 이동 시간 없음).
                target.TakeDamage(_data.baseDamage);
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

            projectile.Launch(target, _data.baseDamage);
        }
    }
}
