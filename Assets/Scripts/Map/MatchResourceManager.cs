using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace TowerDefense.Map
{
    /// <summary>
    /// 인게임(대전) 전용 자원인 SP(소환 포인트)를 관리함. EconomyManager의 골드/보석과는 완전히
    /// 별개 자원임 - EconomyManager 자체 doc comment에도 "SP는 여기서 다루지 않음"이라고 명시돼
    /// 있어서 이 매니저를 새로 둠. CharacterDataSO.summonCost가 이 SP를 기준으로 함.
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
        // 기존 CharacterDataSO 더미 데이터(char_001 등)의 summonCost가 이미 50 단위 스케일로
        // 들어가 있어서(Assets/Data/Characters.json 참고), 여기 최대치/충전량도 그 스케일에 맞춤.
        [Tooltip("더미: 실제 밸런스 확정 전 - SP 최대치 (캐릭터 summonCost 스케일에 맞춤)")]
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
