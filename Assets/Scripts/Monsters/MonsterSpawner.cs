using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

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
    /// </summary>
    public class MonsterSpawner : MonoBehaviour
    {
        [Tooltip("몬스터가 따라갈 경로 - 웨이포인트 순서대로 이동함. NavMesh 도입 여부는 기획 미확정")]
        [SerializeField] private Transform[] waypoints;

        [Tooltip("진행할 웨이브 목록 - 순서대로 재생됨")]
        [SerializeField] private List<WaveDataSO> waves = new();

        // 몬스터 종류별 비활성 인스턴스 큐. 활성화된(현재 전투 중인) 인스턴스는 여기 안 들어있음.
        // 서버에서만 채워지고 쓰임(클라이언트는 스폰을 직접 안 하니까 풀이 필요 없음).
        private readonly Dictionary<MonsterDataSO, Queue<GameObject>> _pool = new();

        private Coroutine _waveRoutine;

        private bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>몬스터가 경로 끝(기지)에 도달했을 때 발생(서버에서만 발생함) - BaseHealth가 구독해서 체력을 깎음.</summary>
        public event Action<MonsterDataSO> OnMonsterReachedEndEvent;

        /// <summary>마지막 웨이브까지 전부 스폰 완료됐을 때 1회 발생(서버에서만 발생함, 아직 살아있는 몬스터가 남아있을 수 있음).</summary>
        public event Action OnAllWavesSpawned;

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
            _waveRoutine = StartCoroutine(RunWaves());
        }

        private IEnumerator RunWaves()
        {
            foreach (var wave in waves)
            {
                if (wave == null) continue;

                yield return new WaitForSeconds(wave.delayBeforeWave);

                foreach (var entry in wave.entries)
                {
                    if (entry.monster == null) continue;

                    for (int i = 0; i < entry.count; i++)
                    {
                        SpawnMonster(entry.monster);
                        yield return new WaitForSeconds(entry.spawnInterval);
                    }
                }
            }

            _waveRoutine = null;
            OnAllWavesSpawned?.Invoke();
        }

        public void SpawnMonster(MonsterDataSO data)
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

            instance.GetComponent<MonsterPathFollower>().Init(data, waypoints, this);
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
        }
    }
}
