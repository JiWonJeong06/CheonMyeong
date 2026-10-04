using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using TowerDefense.Data;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace TowerDefense.Network
{
    /// <summary>
    /// 실시간 자동 매칭 전담 스크립트. com.unity.services.multiplayer(Multiplayer Services SDK)의
    /// Session API를 씀 - 예전처럼 Lobby/Relay/Matchmaker를 각각 따로 SDK 호출하는 게 아니라,
    /// "Session"이라는 개념 하나로 로비 탐색/릴레이 연결/네트코드 연결까지 전부 묶어서 처리해줌
    /// (Unity 공식 통합 SDK, 2026년 기준 이게 신규 프로젝트 권장 방식임 - 예전 개별 패키지 방식 아님).
    ///
    /// 매칭 방식은 Quick Join API를 씀(MultiplayerService.Instance.MatchmakeSessionAsync에
    /// QuickJoinOptions를 넘기는 오버로드) - Lobby 기반으로 기존 세션을 검색하다가 조건에 맞는 게
    /// 없으면 자동으로 새 세션을 만들어줌. 트로피(RankManager.CurrentTrophies) 기준 ±TrophyRange
    /// 범위 안에 있는 세션만 찾도록 NumberIndex1 필터를 걸어서 클래시로얄류의 "비슷한 실력끼리
    /// 매칭" 느낌을 냄 - 다만 이건 Lobby 세션 검색 필터를 이용한 근사치이고, Unity가 제공하는
    /// 진짜 스킬 기반 매치메이커(Advanced Matchmaking/MatchmakerOptions 오버로드)는 Unity Cloud
    /// 대시보드에서 큐/규칙(Rules Config)을 별도로 설정해야 하는 더 무거운 구조라, 캡스톤 규모에는
    /// 이 Quick Join 방식이 설정 부담 없이 바로 쓸 수 있는 실용적인 선택임.
    ///
    /// [알려진 제약] MatchmakeSessionAsync(QuickJoinOptions, ...) 오버로드는 CancellationToken을
    /// 지원 안 함(MatchmakerOptions 쓰는 고급 매치메이커 오버로드만 지원) - 그래서 매칭 중 취소를
    /// 누르면 진행 중인 검색 자체를 중간에 끊을 순 없고, 검색이 끝난 직후 바로 LeaveAsync()로
    /// 나가는 방식으로 처리함(CancelMatchmaking 참고).
    ///
    /// SessionOptions.WithRelayNetwork()를 쓰면 세션이 성사되는 순간 Relay 연결 + Netcode for
    /// GameObjects의 NetworkManager 연결까지 SDK가 알아서 처리함(수동으로 NetworkManager.
    /// StartHost/StartClient를 호출할 필요 없음) - 연결 완료 감지는 Netcode 표준 콜백
    /// (NetworkManager.Singleton.OnClientConnectedCallback)을 그대로 쓰면 됨.
    /// </summary>
    public class MatchmakingService : MonoBehaviour
    {
        public static MatchmakingService Instance { get; private set; }

        [Tooltip("더미: 밸런스 확정 전 - 내 트로피 기준 이 범위(±) 안의 세션만 찾음")]
        [SerializeField] private int trophyRange = 50;

        [Tooltip("검색 타임아웃(초) - 이 시간 안에 맞는 세션이 없으면 새 세션을 만들고 상대를 기다림")]
        [SerializeField] private float searchTimeoutSeconds = 10f;

        [Tooltip("매칭 성사 후 이동할 인게임 씬 이름 - Build Settings에 등록된 씬 이름과 정확히 같아야 함")]
        [SerializeField] private string inGameSceneName = "InGame";

        public bool IsMatchmaking { get; private set; }
        public ISession CurrentSession { get; private set; }

        public event Action OnMatchmakingStarted;
        public event Action<ISession> OnMatchFound;
        public event Action<string> OnMatchmakingFailed;

        private bool _cancelRequested;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject); // 매칭 성사 후 인게임 씬으로 넘어가도 세션 참조를 계속 들고 있어야 함
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("디버그 전용 - Development Build 체크 안 하고 뽑은 릴리즈 빌드에선 이 블록 자체가 컴파일 안 됨")]
        [Tooltip("빌드된 실행 파일은 인스펙터가 없어서 [ContextMenu]를 못 씀 - 대신 이 키로 매치메이킹을 바로 시작할 수 있게 해둠. 스테이지 선택 UI가 완성되면 그쪽에서 StartMatchmaking()을 직접 호출하면 되고, 이 키는 그 전까지 테스트용")]
        [SerializeField] private Key debugStartMatchmakingKey = Key.F9;

        private void Update()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current[debugStartMatchmakingKey].wasPressedThisFrame)
            {
                StartMatchmaking();
            }
        }
#endif

        [ContextMenu("Debug: Start Matchmaking")]
        public async void StartMatchmaking()
        {
            if (IsMatchmaking)
            {
                Debug.LogWarning("[MatchmakingService] 이미 매칭 진행 중임.");
                return;
            }

            IsMatchmaking = true;
            _cancelRequested = false;
            OnMatchmakingStarted?.Invoke();

            try
            {
                if (NetworkBootstrap.Instance == null)
                {
                    throw new InvalidOperationException("NetworkBootstrap이 씬에 없음 - 로그인 처리가 안 된 상태.");
                }
                await NetworkBootstrap.Instance.EnsureSignedInAsync();

                int myTrophies = RankManager.Instance != null ? RankManager.Instance.CurrentTrophies : 0;

                var sessionOptions = new SessionOptions
                {
                    MaxPlayers = 2,
                    // 세션을 새로 만들게 될 경우(맞는 상대가 없을 때) 내 트로피를 Number1 인덱스에
                    // 태그해둬야, 뒤이어 들어오는 다른 플레이어가 이 세션을 트로피 필터로 찾을 수 있음.
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        ["trophies"] = new SessionProperty(myTrophies.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.Number1)
                    }
                }.WithRelayNetwork();

                var quickJoinOptions = new QuickJoinOptions
                {
                    Filters = new List<FilterOption>
                    {
                        new(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual),
                        new(FilterField.NumberIndex1, (myTrophies - trophyRange).ToString(), FilterOperation.GreaterOrEqual),
                        new(FilterField.NumberIndex1, (myTrophies + trophyRange).ToString(), FilterOperation.LessOrEqual)
                    },
                    Timeout = TimeSpan.FromSeconds(searchTimeoutSeconds),
                    CreateSession = true // 맞는 세션이 없으면 새로 만들고 상대를 기다림(호스트가 됨)
                };

                var session = await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoinOptions, sessionOptions);

                if (_cancelRequested)
                {
                    // 검색이 끝나기 전에 취소 요청이 들어온 경우 - CancellationToken을 지원 안 해서
                    // 검색 자체는 끝까지 진행됐지만, 결과로 받은 세션에서 바로 나가는 걸로 취소 처리함.
                    await session.LeaveAsync();
                    IsMatchmaking = false;
                    return;
                }

                CurrentSession = session;
                IsMatchmaking = false;
                Debug.Log($"[MatchmakingService] 매칭 완료. SessionId={session.Id}, IsHost={session.IsHost}, PlayerCount={session.PlayerCount}");
                OnMatchFound?.Invoke(session);

                // 세션이 성사되면 Relay+Netcode 연결까진 SDK가 알아서 해주지만, "인게임 씬으로
                // 실제로 이동"은 별도임 - 호스트만 씬을 로드하면 됨(Netcode의 NetworkSceneManager가
                // 씬 로드를 자동으로 접속된 다른 클라이언트에도 전파해줌 - 클라이언트가 각자
                // SceneManager.LoadScene을 부르면 안 됨, 그럼 서버-클라이언트 씬이 따로 놀게 됨).
                //
                // [버그 수정] QuickJoin은 "맞는 세션이 없어서 내가 새로 세션을 만듦(=호스트가 됨)"
                // 시점에 바로 Task가 끝남 - 상대가 실제로 들어올 때까지 기다려주지 않음. 그래서
                // 예전 코드처럼 "IsHost==true면 무조건 씬 로드"를 하면, 1:1 대전인데 상대 없이 혼자
                // 인게임으로 넘어가버리는 문제가 있었음(실제로 재현됨). PlayerCount가 MaxPlayers에
                // 도달했을 때만 씬을 로드하고, 아직이면 session.PlayerJoined를 구독해서 상대가
                // 들어오는 순간 로드하도록 고침.
                if (session.IsHost)
                {
                    if (session.PlayerCount >= session.MaxPlayers)
                    {
                        LoadInGameScene();
                    }
                    else
                    {
                        Debug.Log($"[MatchmakingService] 세션 생성함(호스트) - 상대 대기 중 ({session.PlayerCount}/{session.MaxPlayers}).");

                        void OnWaitingPlayerJoined(string joinedPlayerId)
                        {
                            if (session.PlayerCount < session.MaxPlayers) return;
                            session.PlayerJoined -= OnWaitingPlayerJoined;
                            LoadInGameScene();
                        }

                        session.PlayerJoined += OnWaitingPlayerJoined;
                    }
                }
            }
            catch (Exception e)
            {
                IsMatchmaking = false;
                Debug.LogError($"[MatchmakingService] 매칭 실패: {e.Message}");
                OnMatchmakingFailed?.Invoke(e.Message);
            }
        }

        private void LoadInGameScene()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(inGameSceneName, LoadSceneMode.Single);
            }
            else
            {
                Debug.LogError("[MatchmakingService] NetworkManager.Singleton이 없어서 인게임 씬으로 이동 못 함 - 씬에 NetworkManager가 있는지, Scene Management가 켜져 있는지 확인할 것.");
            }
        }

        /// <summary>매칭 중 취소 버튼 등에서 호출. 위 클래스 doc comment의 [알려진 제약] 참고.</summary>
        public void CancelMatchmaking()
        {
            if (!IsMatchmaking) return;
            _cancelRequested = true;
        }

        /// <summary>매치가 끝났을 때(승패 결정 후) 세션에서 나감 - Relay/Netcode 연결도 같이 정리됨.</summary>
        public async Task LeaveMatchAsync()
        {
            if (CurrentSession == null) return;

            try
            {
                await CurrentSession.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MatchmakingService] 세션 나가기 실패(이미 끊겼을 수 있음): {e.Message}");
            }
            finally
            {
                CurrentSession = null;
            }
        }
    }
}
