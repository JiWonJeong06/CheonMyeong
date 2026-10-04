using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

namespace TowerDefense.Network
{
    /// <summary>
    /// UGS(Unity Gaming Services) 초기화 + 로그인을 전담하는 부트스트랩. Lobby/Relay/Matchmaker(전부
    /// com.unity.services.multiplayer 패키지의 Session API로 통합됨)를 쓰려면 반드시 이 두 단계가
    /// 먼저 끝나 있어야 함: UnityServices.InitializeAsync() -> AuthenticationService 로그인.
    ///
    /// 지금은 SignInAnonymouslyAsync()(게스트 로그인)만 씀 - 구글 로그인은 넣지 않기로 함(스팀 출시가
    /// 목표라 불필요한 로그인 단계). 대신 나중에 스팀 앱 등록(Steamworks 파트너 계정 + App ID 발급)이
    /// 끝나면 여기 SignInAnonymouslyAsync() 호출을
    /// AuthenticationService.Instance.SignInWithSteamAsync(steamSessionTicket)로 교체하기만 하면
    /// 됨(com.unity.services.authentication 3.1.0+ 기준 API, 스팀 클라이언트가 이미 로그인돼 있어서
    /// 플레이어가 별도 로그인 화면을 볼 필요가 없어짐) - 그 전까진 게스트 로그인으로 충분함.
    ///
    /// 익명 로그인이어도 기기에 캐시된 세션 토큰으로 재실행 시 같은 플레이어 ID가 유지되므로
    /// (Authentication SDK 자체 동작) 매치 중간에 재접속해도 같은 유저로 인식됨.
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour
    {
        public static NetworkBootstrap Instance { get; private set; }

        // [버그 수정] AuthenticationService.Instance는 UnityServices.InitializeAsync()가 끝나기 전에
        // 접근하면 null이 아니라 ServicesInitializationException("Singleton is not initialized...")을
        // 던짐(com.unity.services.authentication 공식 API 문서로 확인함) - 그래서 예전처럼
        // "AuthenticationService.Instance != null"로 먼저 방어하는 건 소용없었음(그 접근 자체가
        // 예외를 던지니까). UnityServices.State를 먼저 확인해서 &&의 단락 평가로 아예 접근을 안
        // 하게 만들어야 안전함.
        public bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        // 초기화 흐름이 여러 곳(예: 메인메뉴에서 미리, 매치메이킹 시작 시 또 한 번)에서 동시에
        // 호출돼도 InitializeAsync/SignIn을 중복 실행하지 않도록 진행 중인 Task를 캐싱해서 재사용함.
        private Task _signInTask;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject); // 메인메뉴 -> 매치메이킹 -> 인게임 씬 전환 내내 로그인 상태 유지
        }

        /// <summary>
        /// UGS 초기화 + 로그인이 끝날 때까지 대기함. 이미 로그인돼 있으면 즉시 반환.
        /// Matchmaking 등 UGS 서비스를 쓰는 모든 코드는 실제 호출 전에 이걸 await해야 함.
        /// </summary>
        public Task EnsureSignedInAsync()
        {
            if (IsSignedIn) return Task.CompletedTask;

            // 이미 진행 중인 로그인 시도가 있으면 그걸 그대로 반환(중복 로그인 요청 방지).
            _signInTask ??= SignInInternalAsync();
            return _signInTask;
        }

        private async Task SignInInternalAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"[NetworkBootstrap] 게스트 로그인 완료. PlayerId={AuthenticationService.Instance.PlayerId}");
                }
            }
            catch (Exception e)
            {
                // 실패 시 다음 시도에서 다시 초기화하도록 캐시를 비움 - 안 그러면 한 번 실패한 Task를
                // 계속 재사용해서 영원히 실패 상태로 남는 버그가 생김.
                _signInTask = null;
                Debug.LogError($"[NetworkBootstrap] 로그인 실패: {e.Message}");
                throw;
            }
        }
    }
}
