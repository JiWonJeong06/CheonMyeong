using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace TowerDefense.Map
{
    /// <summary>
    /// 인게임(대전) 전용 자원인 SP(소환 포인트)를 관리함. EconomyManager의 골드/보석과는 완전히
    /// 별개 자원임 - EconomyManager 자체 doc comment에도 "SP는 여기서 다루지 않음"이라고 명시돼
    /// 있어서 이 매니저를 새로 둠.
    ///
    /// [기획 확정 - 소환 비용 고정] 원래는 CharacterDataSO.summonCost(캐릭터별 개별 값)를 SP 차감
    /// 기준으로 썼는데, 기획 쪽에서 "소환할 때마다 전체 SP 소모가 고정 10 증가"로 확정하면서 캐릭터별
    /// 개별 SP 비용이 불필요해짐 - 그래서 실제 차감량은 이 클래스의 SummonSpCost 상수 하나로
    /// 통일함(TowerPlacementController의 로컬 사전 체크, MatchController.RequestPlaceTowerRpc의 서버
    /// 차감/환불이 전부 이 상수를 씀). CharacterDataSO.summonCost 필드 자체는 그대로 남겨뒀지만
    /// (다른 용도로 나중에 재활용될 수 있어 JSON 스키마를 건드리지 않음) 지금은 SP 차감 계산 어디에도
    /// 안 쓰임 - 혼동하지 말 것.
    ///
    /// [1:1 대전 구조 확정에 따른 변경] 예전엔 Instance 싱글턴이었는데, 보드가 두 개(내 보드/상대
    /// 보드) 동시에 존재하니 싱글턴을 없앰 - PlayerBoard가 자기 보드의 인스턴스를 직접 들고 있음.
    ///
    /// [네트워킹 변경] SP는 승패에 영향을 주는 값이라 클라이언트가 스스로 계산한 값을 믿으면
    /// 치팅에 취약함(자기 SP를 조작해서 무한 소환 등) - 그래서 이 컴포넌트는 "서버에서 실행 중인
    /// 사본만" 진짜 값을 계산함(NetworkManager.Singleton.IsServer 체크). 클라이언트 쪽 사본은
    /// 그냥 비활성 상태로 존재만 하고, 실제로 화면에 보여줄 SP 숫자는 MatchController가 갖고 있는
    /// NetworkVariable(서버가 이 컴포넌트의 OnSPChanged를 받아서 그쪽에 복사함)에서 읽어야 함.
    /// 소환 시도(TrySpendSP)도 클라이언트가 직접 호출하는 게 아니라, MatchController의
    /// RequestPlaceTowerRpc를 통해 서버에서만 호출되는 구조로 바뀜(TowerPlacementController 참고).
    /// </summary>
    public class MatchResourceManager : MonoBehaviour
    {
        /// <summary>소환 1회당 고정 SP 소모량 - 기획 확정값(캐릭터 종류와 무관하게 항상 10).
        /// TowerPlacementController(로컬 사전 체크)와 MatchController.RequestPlaceTowerRpc(서버 실제
        /// 차감/환불), InGameDeckController(카드 흐림 표시 + "SP 소모" 누적 카운터)가 전부 이 값
        /// 하나만 참조함 - 여러 곳에 값이 흩어지면 나중에 밸런스 바뀔 때 어긋나기 쉬워서 한 곳으로 모음.</summary>
        public const float SummonSpCost = 10f;

        [Tooltip("더미: 실제 밸런스 확정 전 - SP 최대치")]
        [SerializeField] private float maxSP = 100f;

        [Tooltip("더미: 실제 밸런스 확정 전 - 초당 자동 충전량")]
        [SerializeField] private float spPerSecond = 5f;

        [Tooltip("더미: 시작 SP")]
        [SerializeField] private float startingSP = 50f;

        public float CurrentSP { get; private set; }
        public float MaxSP => maxSP;

        public event Action<float> OnSPChanged;

        private Coroutine _regenRoutine;

        // 서버가 아닌 사본(클라이언트 쪽에 존재하는 상대/내 보드 컴포넌트)에서는 아무 것도 계산하지
        // 않음 - NetworkManager.Singleton이 아직 안 붙어있는 극초반 타이밍 대비 null 체크도 같이 함.
        private bool IsAuthoritative => NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;

        private void Awake()
        {
            CurrentSP = startingSP;
        }

        private void OnEnable()
        {
            if (!IsAuthoritative) return;
            _regenRoutine = StartCoroutine(RegenRoutine());
        }

        private void OnDisable()
        {
            // 씬 전환/파괴 도중 코루틴이 파괴된 오브젝트를 계속 참조하며 도는 걸 방지.
            if (_regenRoutine != null)
            {
                StopCoroutine(_regenRoutine);
                _regenRoutine = null;
            }
        }

        private IEnumerator RegenRoutine()
        {
            // 매 프레임 Invoke하면 이벤트가 과도하게 자주 불려서, 0.1초 간격으로만 충전/갱신함.
            var wait = new WaitForSeconds(0.1f);
            while (true)
            {
                yield return wait;
                if (CurrentSP < maxSP)
                {
                    AddSP(spPerSecond * 0.1f);
                }
            }
        }

        /// <summary>[서버 전용] MatchController.RequestPlaceTowerRpc 안에서만 호출돼야 함.</summary>
        public bool TrySpendSP(float amount)
        {
            if (!IsAuthoritative) return false; // 방어적 체크 - 클라이언트가 실수로 직접 호출해도 무시됨
            if (amount <= 0f || CurrentSP < amount) return false;
            CurrentSP -= amount;
            OnSPChanged?.Invoke(CurrentSP);
            return true;
        }

        public void AddSP(float amount)
        {
            if (!IsAuthoritative) return;
            if (amount <= 0f) return;
            CurrentSP = Mathf.Min(maxSP, CurrentSP + amount);
            OnSPChanged?.Invoke(CurrentSP);
        }
    }
}
