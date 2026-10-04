using System;
using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// 실시간 대전 결과에 따라 오르내리는 트로피(랭크) 점수 관리자. 클래시로얄/랜덤다이스류처럼
    /// "이기면 오르고 지면 내려가는" 단일 스칼라 점수 구조를 씀 - 리그/티어 구간 나누기는
    /// 트로피 숫자 범위로 UI 쪽에서 해석하면 되고(예: 0~999=브론즈 등), 이 매니저는 점수 자체만 책임짐.
    ///
    /// 지금은 PlayerPrefs로 로컬에만 저장함 - 실제로는 서버(UGS 등 클라우드 백엔드)가 매치 결과를
    /// 검증하고 트로피를 갱신해야 신뢰할 수 있는 랭크가 됨(클라이언트가 로컬 값을 직접 조작하면
    /// 치팅이 가능해지므로). ReportMatchResult()는 지금 로컬에서 더미로 가감산만 하고 있고,
    /// 나중에 매치 종료 시 서버 API 호출로 교체하면서 그 응답값으로 CurrentTrophies를 갱신하는
    /// 식으로 바뀔 예정임(네트워킹 파트에서 이어서 작업).
    ///
    /// 가감 폭(DummyWinDelta/DummyLoseDelta)은 고정값 더미임 - 실제로는 상대와의 트로피 격차에
    /// 따라 가변적으로 계산하는 매치메이킹 ELO/글리코 유사 시스템이 보통 쓰이지만, 그 계산은
    /// 서버(매치메이커) 쪽 책임이 될 가능성이 높아서 지금은 클라이언트에 로직을 넣지 않음.
    /// </summary>
    public class RankManager : MonoBehaviour
    {
        public static RankManager Instance { get; private set; }

        private const string TrophyKey = "Rank_Trophies";
        private const int DummyStartTrophies = 0;
        private const int DummyWinDelta = 30;
        private const int DummyLoseDelta = -30;

        public int CurrentTrophies { get; private set; }

        public event Action<int> OnTrophiesChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject); // 메인화면 <-> 인게임 씬 전환 중에도 랭크 값 유지

            CurrentTrophies = PlayerPrefs.GetInt(TrophyKey, DummyStartTrophies);
        }

        /// <summary>
        /// 매치 종료 시 호출. 승패에 따라 트로피를 더미 고정폭으로 가감함.
        /// TODO(네트워킹): 서버 검증 붙으면 이 메서드를 "서버 응답값으로 CurrentTrophies를 그대로
        /// 덮어쓰는" 형태로 교체할 것 - 지금처럼 클라이언트가 스스로 가감하는 구조는 임시임.
        /// </summary>
        public void ReportMatchResult(bool won)
        {
            int delta = won ? DummyWinDelta : DummyLoseDelta;
            CurrentTrophies = Mathf.Max(0, CurrentTrophies + delta);
            PlayerPrefs.SetInt(TrophyKey, CurrentTrophies);
            OnTrophiesChanged?.Invoke(CurrentTrophies);
        }

        /// <summary>무승부 - 트로피 변동 없음(승/패 가감 없이 값만 유지).</summary>
        public void ReportMatchDraw()
        {
            OnTrophiesChanged?.Invoke(CurrentTrophies);
        }

        /// <summary>[디버그 전용] 트로피를 0으로 되돌림 - QA 테스트용. 출시 전 호출부와 함께 제거할 것.</summary>
        [ContextMenu("Debug: Reset Trophies")]
        public void DebugResetTrophies()
        {
            CurrentTrophies = DummyStartTrophies;
            PlayerPrefs.SetInt(TrophyKey, CurrentTrophies);
            OnTrophiesChanged?.Invoke(CurrentTrophies);
        }
    }
}
