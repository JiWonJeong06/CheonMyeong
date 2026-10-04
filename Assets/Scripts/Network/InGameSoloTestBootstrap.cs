#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using Unity.Netcode;

namespace TowerDefense.Network
{
    /// <summary>
    /// [에디터/개발 빌드 전용] InGame 씬을 매칭 없이 단독으로 Play했을 때(예: 에디터에서 MainMenu를
    /// 거치지 않고 바로 InGame.unity를 열고 Play) NetworkManager 자체가 아예 존재하지 않아서
    /// MatchController 등의 [Rpc(...)] 호출이 전부 "Rpc methods can only be invoked after starting
    /// the NetworkManager!" 예외로 죽는 문제를 피하기 위한 솔로 테스트 부트스트랩.
    ///
    /// 정상 경로(MainMenu -> 매칭 -> Relay 연결)로 들어온 경우엔 NetworkManager가 MainMenu 씬에서부터
    /// DontDestroyOnLoad로 이미 살아있으므로(NetworkManager.Singleton != null) 아무것도 안 하고 그대로
    /// 리턴함 - InGame 씬에 미리 넣어둔 비활성 상태의 soloNetworkManager는 계속 비활성 상태로 남아서
    /// 실제 흐름을 절대 방해하지 않음(Netcode는 NetworkManager 인스턴스가 동시에 2개 활성화되는 걸
    /// 허용 안 하므로 이 분기 처리가 필수임).
    ///
    /// [주의] 호스트 혼자 접속하는 거라 두 번째 플레이어가 없어서 MatchController.TryStartMatch()의
    /// "양쪽 보드 모두 배정" 조건이 충족 안 됨 - 웨이브는 시작되지 않음(정상 동작) - 타워 배치/일시정지/
    /// 항복 같은 RPC 호출 자체를 빠르게 확인하는 용도로만 쓸 것. 진짜 2인 대전 흐름 확인은 기존처럼
    /// MainMenu -> 매칭(에디터+빌드 또는 빌드 2개)으로 해야 함.
    ///
    /// 이 파일 전체가 UNITY_EDITOR || DEVELOPMENT_BUILD로만 컴파일되므로 실제 출시 빌드에는 포함되지
    /// 않음 - 출시 전 별도로 제거할 필요 없음(MatchmakingService의 F9 디버그 키와 동일한 방식).
    /// </summary>
    public class InGameSoloTestBootstrap : MonoBehaviour
    {
        [Tooltip("MainMenu 씬의 NetworkManager를 복제해서 InGame 씬에 미리 넣어둔 오브젝트 - 반드시 " +
                 "기본 비활성화 상태여야 함. Tools/Dev Scaffold/Setup InGame Pause UI 실행 시 자동 연결됨")]
        [SerializeField] private GameObject soloNetworkManager;

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                // 정상 매칭 경로로 이미 연결된 상태(MainMenu에서부터 살아있는 NetworkManager) - 아무것도 안 함.
                return;
            }

            if (soloNetworkManager == null)
            {
                Debug.LogWarning("[InGameSoloTestBootstrap] soloNetworkManager가 인스펙터에 연결 안 돼 있어서 " +
                    "솔로 호스트 시작 불가 - Tools/Dev Scaffold/Setup InGame Pause UI를 다시 실행할 것.");
                return;
            }

            Debug.LogWarning("[InGameSoloTestBootstrap] NetworkManager 없이 InGame 씬이 단독으로 열림" +
                "(매칭 없이 씬만 Play한 것으로 보임) - 테스트용 솔로 호스트를 자동 시작함. " +
                "상대가 없어서 웨이브는 시작되지 않음(정상) - 타워 배치/팝업 RPC 테스트 용도로만 쓸 것.");

            soloNetworkManager.SetActive(true);
            NetworkManager.Singleton.StartHost();
        }
    }
}
#endif
