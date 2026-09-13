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
    /// </summary>
    public class PopupManager : MonoBehaviour
    {
        public static PopupManager Instance { get; private set; }

        private readonly Stack<IPopupPanel> _openPopups = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
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
            popup.Hide();
            if (_openPopups.Count > 0 && _openPopups.Peek() == popup)
                _openPopups.Pop();
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
