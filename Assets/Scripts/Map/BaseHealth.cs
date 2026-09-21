using System;
using UnityEngine;
using Unity.Netcode;
using TowerDefense.Monsters;

namespace TowerDefense.Map
{
    /// <summary>
    /// 플레이어 기지 체력. MonsterSpawner.OnMonsterReachedEndEvent를 구독해서 몬스터가 경로 끝에
    /// 도달할 때마다 그 몬스터의 damageToBase만큼 깎음. 기지 체력이 0이 되면 OnBaseDestroyed를
    /// 발생시킴 - MatchController가 이 이벤트를 구독해서 승패를 판정하고 양쪽 클라이언트에 결과를
    /// 알려줌. 이 스크립트는 "체력 관리"까지만 책임짐(단일 책임).
    ///
    /// [1:1 대전 구조 확정] 보드마다(BoardA/BoardB) 하나씩 존재 - PlayerBoard가 이 컴포넌트를
    /// 자기 보드의 spawner와 함께 들고 있음.
    ///
    /// [네트워킹] 기지 체력도 SP처럼 승패에 직결되는 값이라 서버에서만 실제로 깎임(IsAuthoritative
    /// 가드) - 클라이언트 쪽 화면에 보여줄 숫자는 MatchController의 NetworkVariable을 통해 받음.
    /// </summary>
    public class BaseHealth : MonoBehaviour
    {
        [SerializeField] private MonsterSpawner spawner;

        [Tooltip("더미: 실제 밸런스 확정 전 - 기지 최대 체력")]
        [SerializeField] private int maxHp = 20;

        public int CurrentHp { get; private set; }
        public int MaxHp => maxHp;
        public bool IsDestroyed { get; private set; }

        private bool IsAuthoritative => NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;

        public event Action<int, int> OnHpChanged; // (currentHp, maxHp)
        public event Action OnBaseDestroyed;

        private void Awake()
        {
            CurrentHp = maxHp;
        }

        private void OnEnable()
        {
            if (spawner == null)
            {
                Debug.LogWarning("[BaseHealth] spawner가 연결 안 돼있음 - 인스펙터에서 연결할 것.");
                return;
            }
            spawner.OnMonsterReachedEndEvent += HandleMonsterReachedEnd;
        }

        private void OnDisable()
        {
            if (spawner == null) return;
            spawner.OnMonsterReachedEndEvent -= HandleMonsterReachedEnd; // 구독 해제 누락 시 파괴된 기지 참조 누수 위험
        }

        private void HandleMonsterReachedEnd(MonsterDataSO data)
        {
            if (IsDestroyed) return;

            TakeDamage(data.damageToBase);
        }

        public void TakeDamage(int amount)
        {
            if (!IsAuthoritative) return; // 방어적 체크 - 클라이언트 사본에서 실수로 불려도 무시됨
            if (IsDestroyed || amount <= 0) return;

            CurrentHp = Mathf.Max(0, CurrentHp - amount);
            OnHpChanged?.Invoke(CurrentHp, maxHp);

            if (CurrentHp <= 0)
            {
                IsDestroyed = true;
                OnBaseDestroyed?.Invoke();
            }
        }

        [ContextMenu("Debug: Reset Base HP")]
        public void DebugResetHp()
        {
            CurrentHp = maxHp;
            IsDestroyed = false;
            OnHpChanged?.Invoke(CurrentHp, maxHp);
        }
    }
}
