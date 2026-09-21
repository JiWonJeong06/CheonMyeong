using UnityEngine;
using Unity.Netcode;
using TowerDefense.Monsters;

namespace TowerDefense.Map
{
    /// <summary>
    /// TowerUnit이 발사하는 투사체. 타겟을 향해 직선으로 날아가서 도착하면 데미지를 주고 스스로
    /// 파괴됨(풀링은 안 함 - 투사체는 몬스터/타워보다 수명이 훨씬 짧고 동시 개수도 적어서 지금은
    /// Destroy로 충분하다고 판단함. 나중에 투사체가 많아져 문제되면 몬스터처럼 풀링으로 바꿀 것).
    ///
    /// 타겟이 날아가는 도중에 죽거나 풀에 반납되면(비활성화되면) 예외 없이 스스로 파괴돼야 해서
    /// 매 프레임 타겟의 IsAlive/활성 상태를 확인함 - 그렇지 않으면 파괴된 대상을 참조한 채로
    /// 영원히 허공을 날아다니는(메모리 누수) 투사체가 남을 수 있음.
    ///
    /// [네트워킹] NetworkBehaviour로 바꿈 - 이동/명중 판정은 서버에서만 계산하고(IsServer 가드),
    /// 위치 복제는 프리팹에 붙은 NetworkTransform(인스펙터에서 추가)이 담당함. 파괴도 그냥
    /// Destroy(gameObject) 대신 NetworkObject.Despawn()(기본값 destroy:true)을 써야 두 클라이언트
    /// 화면에서 동시에 사라짐 - 플레인 Destroy만 부르면 스폰한 서버 쪽에서만 사라지고 다른 클라이언트
    /// 화면엔 유령처럼 남을 수 있음.
    /// </summary>
    public class Projectile : NetworkBehaviour
    {
        [Tooltip("초당 이동 속도")]
        [SerializeField] private float speed = 10f;

        [Tooltip("이 거리 안으로 들어오면 명중으로 처리")]
        [SerializeField] private float hitDistance = 0.1f;

        private MonsterPathFollower _target;
        private float _damage;

        public void Launch(MonsterPathFollower target, float damage)
        {
            _target = target;
            _damage = damage;
        }

        private void Update()
        {
            if (!IsServer) return; // 클라이언트 쪽 사본은 NetworkTransform이 넣어주는 위치만 따라감

            // 타겟이 이동 중 죽어서 풀에 반납(비활성화)됐거나 아예 파괴된 경우 - 허공에 남지 않게 즉시 정리.
            if (_target == null || !_target.gameObject.activeInHierarchy || !_target.IsAlive)
            {
                DespawnSelf();
                return;
            }

            Vector3 targetPos = _target.Position;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);

            if (Vector3.Distance(transform.position, targetPos) <= hitDistance)
            {
                _target.TakeDamage(_damage);
                DespawnSelf();
            }
        }

        private void DespawnSelf()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(); // destroy:true(기본값) - 모든 클라이언트에서 같이 파괴됨
            }
            else
            {
                Destroy(gameObject); // 스폰 전에 문제가 생긴 경우를 대비한 안전장치
            }
        }
    }
}
