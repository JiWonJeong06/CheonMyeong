using UnityEngine;
using Unity.Netcode;

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
    /// </summary>
    public class MonsterPathFollower : NetworkBehaviour
    {
        private MonsterDataSO _data;
        private Transform[] _waypoints;
        private MonsterSpawner _spawner;

        private int _waypointIndex;
        private float _currentHp;

        public bool IsAlive => _currentHp > 0f;
        public Vector3 Position => transform.position;

        // 오브젝트 풀링 특성상 SetActive(false/true)로 반납/재사용되므로, 등록/해제도 OnEnable/OnDisable에
        // 맡기면 Init 호출 누락이나 반납 시 해제 누락 걱정 없이 항상 활성 상태와 레지스트리가 일치함
        // (타워(TowerUnit)가 이 레지스트리로 타겟을 찾음 - MonsterRegistry 참고). 클라이언트 쪽
        // 사본도 등록되긴 하지만 TowerUnit.Update가 서버에서만 도니까 실질적으로 조회되진 않음.
        private void OnEnable() => MonsterRegistry.Register(this);
        private void OnDisable() => MonsterRegistry.Unregister(this);

        // 풀에서 꺼내 재사용할 때마다 스포너(서버)가 호출함 - 생성자 대신 이 방식을 쓰는 이유는
        // 오브젝트 풀링 특성상 Instantiate는 한 번만 일어나고 이후엔 이 Init만 반복 호출되기 때문.
        public void Init(MonsterDataSO data, Transform[] waypoints, MonsterSpawner spawner)
        {
            _data = data;
            _waypoints = waypoints;
            _spawner = spawner;
            _waypointIndex = 0;
            _currentHp = data.maxHp;

            if (_waypoints != null && _waypoints.Length > 0)
            {
                transform.position = _waypoints[0].position;
            }
        }

        private void Update()
        {
            if (!IsServer) return; // 클라이언트는 NetworkTransform이 복제해주는 위치만 따라감
            if (_waypoints == null || _waypointIndex >= _waypoints.Length) return;

            Vector3 target = _waypoints[_waypointIndex].position;
            transform.position = Vector3.MoveTowards(transform.position, target, _data.moveSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, target) < 0.05f)
            {
                _waypointIndex++;
                if (_waypointIndex >= _waypoints.Length)
                {
                    ReachEnd();
                }
            }
        }

        /// <summary>[서버 전용] TowerUnit.Attack에서만 호출됨.</summary>
        public void TakeDamage(float amount)
        {
            if (!IsServer) return;
            if (!IsAlive) return; // 이미 죽고 반납 대기 중인 개체에 중복 데미지가 들어오는 걸 방지

            _currentHp -= amount;
            if (_currentHp <= 0f) Die();
        }

        private void Die()
        {
            // TODO(경제 시스템): 예전엔 여기서 EconomyManager.Instance.AddGold(_data.goldReward)를
            // 호출했었는데, 이 메서드가 서버에서만 실행되는 구조로 바뀌면서 그대로 두면 "호스트를
            // 맡은 플레이어의 로컬 EconomyManager"만 양쪽 보드 킬 보상을 전부 받아가는 버그가 생김
            // (클라이언트 쪽 EconomyManager는 아예 갱신 안 됨). 몬스터 처치 보상을 메타 재화(Gold)로
            // 줄지, 인게임 SP로 줄지(PlayerBoard의 MatchResourceManager.AddSP), 아니면 아예 매치
            // 종료 후 결과 화면에서 한 번에 정산할지는 기획 확정 필요 - 그때까지 보상 지급은 비워둠.
            _spawner.ReturnToPool(_data, gameObject);
        }

        private void ReachEnd()
        {
            _spawner.OnMonsterReachedEnd(_data);
            _spawner.ReturnToPool(_data, gameObject);
        }
    }
}
