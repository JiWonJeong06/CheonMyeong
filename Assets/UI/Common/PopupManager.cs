using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TowerDefense.UI
{
    /// <summary>
    /// 팝업(모달) 창들을 한 곳에서 관리하는 싱글턴.
    /// 배경 딤 처리는 각 팝업 UXML의 popup-root 클래스(반투명 배경, Common.uss)가 스스로 담당하고,
    /// 여기서는 "지금 뭐가 열려있는지"만 스택으로 추적한다.
    /// 스택으로 관리하는 이유: 설정 창 안에서 확인창을 여는 것처럼 팝업 위에 팝업이 뜨는 경우를 대응하기 위함.
    ///
    /// DontDestroyOnLoad가 아닌 씬 로컬 싱글턴임 - 씬(MainMenu/InGame)마다 각자 GameObject를 두고
    /// Awake에서 Instance를 갱신하는 방식. 씬 전환 시 이전 씬의 팝업들이 씬과 함께 통째로 파괴되므로
    /// 스택 정리도 자연히 같이 됨(따로 비워줄 필요 없음). MainMenu/InGame 양쪽 씬에서 공용으로 씀.
    ///
    /// [버그 수정 - 2026-09-29] "결과 화면이 안 뜸" 버그의 실제 원인이 여기 있었음(Player.log로
    /// 확인함): Netcode의 NetworkSceneManager는 LoadSceneMode.Single로 씬을 전환해도 내부적으로는
    /// 새 씬을 먼저 additive로 로드해서 NetworkObject 동기화를 마친 뒤에야 이전 씬을 언로드함(NGO
    /// 공식 동작) - 그래서 InGame 씬의 PopupManager.Awake()가 실행되는 시점엔 아직 MainMenu 씬의
    /// PopupManager가 안 죽어있음. 예전 코드는 "Instance가 이미 있으면 나는 자멸"하는 first-wins
    /// 패턴이었는데, 그러면 InGame 쪽 매니저가 그 순간 자기 자신을 Destroy하고 리턴해버려서 Instance가
    /// 영원히 "곧 파괴될 MainMenu 쪽 오브젝트"를 가리키게 됨. 그 낡은 오브젝트의 _openPopups 스택엔
    /// 매칭 대기 중 열렸던 MatchmakingOverlayController가 그대로 남아있었고, 나중에 매치가 끝나서
    /// CloseAll()이 그 이미 파괴된 팝업의 Hide()를 부르다가 MissingReferenceException을 던짐 - 이
    /// 예외가 MatchResultController.HandleMatchEnded 중간에서 터지는 바람에 결과 라벨/화면 표시
    /// 코드(그 아래 줄)까지 아예 못 갔던 것. 그래서 first-wins를 last-wins로 바꿈 - 새로 깨어난 씬의
    /// 매니저가 항상 Instance를 가져가고, 이전 씬 오브젝트는 그 씬이 언로드되면서 자연히 파괴됨(더
    /// 이상 아무도 참조 안 하므로 문제 없음). 추가로 CloseAll/RequestClose에도 파괴된 팝업을 건너뛰는
    /// 방어 코드를 넣어서, 혹시 남아있는 유사 사례에서도 예외로 전체 로직이 끊기지 않게 함.
    /// </summary>
    public class PopupManager : MonoBehaviour
    {
        public static PopupManager Instance { get; private set; }

        private readonly Stack<IPopupPanel> _openPopups = new();

        /// <summary>지금 열려있는 팝업이 하나라도 있는지 - TowerPlacementController가 팝업이 화면을
        /// 덮고 있는 동안 그 아래 보드로 클릭이 그대로 새는 걸 막는 데 씀(Assets/UI/Common/InGamePointerOverUI.cs 참고).</summary>
        public bool HasOpenPopups => _openPopups.Count > 0;

        private void Awake()
        {
            // [버그 수정] 예전엔 "Instance가 이미 있으면 자멸"하는 first-wins였음 - 위 클래스 doc의
            // 버그 수정 설명 참고. 이 컴포넌트가 DontDestroyOnLoad가 아닌 씬 로컬 싱글턴인 이상,
            // 나중에 깨어나는(=나중에 로드되는) 씬의 인스턴스가 항상 이기는 게 맞음.
            Instance = this;
        }

        public void Open(IPopupPanel popup)
        {
            popup.Show();
            _openPopups.Push(popup);
        }

        // 팝업 스스로가 닫힐 때 호출한다 (닫기 버튼, 확인/취소 버튼, ESC 등).
        public void RequestClose(IPopupPanel popup)
        {
            if (IsDestroyedPopup(popup))
            {
                // 이미 파괴된 팝업이 스스로 닫기를 요청할 일은 정상 흐름에선 없지만, 방어적으로
                // 스택에서만 제거하고 Hide() 호출은 건너뜀(MissingReferenceException 방지).
                if (_openPopups.Count > 0 && ReferenceEquals(_openPopups.Peek(), popup))
                    _openPopups.Pop();
                return;
            }

            popup.Hide();
            if (_openPopups.Count > 0 && _openPopups.Peek() == popup)
                _openPopups.Pop();
        }

        /// <summary>
        /// 지금 열려있는 팝업을 전부 강제로 닫음(스택에 없는 순서라도 전부 Hide()). 매치 종료
        /// (MatchResultController.HandleMatchEnded)처럼 "이 화면이 뜨는 순간 다른 팝업은 전부
        /// 무의미해지는" 상황에서 씀 - 예: 일시정지 메뉴를 열어둔 채로 매치가 끝나도 결과 화면
        /// 아래에 파묻히지 않고 깔끔하게 정리됨.
        /// </summary>
        public void CloseAll()
        {
            while (_openPopups.Count > 0)
            {
                var popup = _openPopups.Pop();
                // 이전 씬에서 열렸다가 씬 전환으로 이미 파괴된 팝업을 걸러냄 - 방치하면
                // MissingReferenceException이 CloseAll() 전체를 끊어버려서(호출자가
                // MatchResultController.HandleMatchEnded처럼 그 뒤에 중요한 로직을 잇는 경우) 그
                // 이후 코드가 실행 자체가 안 되는 심각한 부작용이 생김(실제로 결과 화면 버그의
                // 원인이었음 - 클래스 doc 참고).
                if (IsDestroyedPopup(popup)) continue;
                popup.Hide();
            }
        }

        // IPopupPanel은 인터페이스라 순수 C# null 체크(== null)로는 "Unity가 파괴한 오브젝트"를
        // 못 걸러냄(관리되는 래퍼 자체는 안 죽었으니까) - UnityEngine.Object로 캐스팅해서 오버로드된
        // == 연산자를 타야 정확히 걸러짐.
        private static bool IsDestroyedPopup(IPopupPanel popup)
        {
            return popup is UnityEngine.Object unityObj && unityObj == null;
        }

        private void Update()
        {
            // 프로젝트가 새 Input System 단독(Active Input Handling = "Input System Package (New)")이라
            // 레거시 Input.GetKeyDown 대신 Keyboard.current로 확인함.
            // Keyboard.current는 키보드가 인식되기 전(예: 포커스 밖)엔 null일 수 있어서 null 체크 필수.
            if (_openPopups.Count > 0 && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                RequestClose(_openPopups.Peek());
            }
        }
    }
}
