using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using TowerDefense.Map;

namespace TowerDefense.UI
{
    /// <summary>
    /// 인게임 HUD - 내 보드/상대 보드의 기지 체력(HP)과 SP를 실시간으로 보여줌. 현재값은
    /// MatchController(서버 권위, Assets/Scripts/Map/MatchController.cs)의 NetworkVariable
    /// (BoardHpA/B, BoardSpA/B)을 구독해서 받고, 최대값(MaxHp/MaxSP)은 씬에 이미 배치된
    /// PlayerBoard의 로컬 컴포넌트 값을 그대로 씀 - 둘 다 클라이언트/서버 씬 파일에 동일하게
    /// 박혀있는 인스펙터 값이라 따로 네트워크 동기화가 필요 없음.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InGameHUDController : MonoBehaviour
    {
        private UIDocument _document;

        private VisualElement _myHpFill;
        private Label _myHpText;
        private VisualElement _mySpFill;
        private Label _mySpText;

        private VisualElement _enemyHpFill;
        private Label _enemyHpText;
        private VisualElement _enemySpFill;
        private Label _enemySpText;

        private PlayerBoard _myBoard;
        private PlayerBoard _enemyBoard;

        // NetworkVariable<T>.OnValueChanged는 event가 아니라 일반 델리게이트 필드라 +=로 구독 가능함
        // (com.unity.netcode.gameobjects 2.13.2 실제 설치된 패키지 소스로 확인함 - 짐작 아님).
        // 다만 구독 해제 시 정확히 같은 델리게이트 인스턴스로 -=해야 해서 람다를 필드에 저장해둠
        // (안 그러면 매번 새 람다 인스턴스가 생겨서 -=가 안 먹고 구독이 계속 쌓이는 누수가 생김).
        private NetworkVariable<int>.OnValueChangedDelegate _myHpHandler;
        private NetworkVariable<int>.OnValueChangedDelegate _enemyHpHandler;
        private NetworkVariable<float>.OnValueChangedDelegate _mySpHandler;
        private NetworkVariable<float>.OnValueChangedDelegate _enemySpHandler;

        private NetworkVariable<int> _boundMyHpVar;
        private NetworkVariable<int> _boundEnemyHpVar;
        private NetworkVariable<float> _boundMySpVar;
        private NetworkVariable<float> _boundEnemySpVar;

        private Coroutine _bindRoutine;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _myHpFill = root.Q<VisualElement>("my-hp-fill");
            _myHpText = root.Q<Label>("my-hp-text");
            _mySpFill = root.Q<VisualElement>("my-sp-fill");
            _mySpText = root.Q<Label>("my-sp-text");

            _enemyHpFill = root.Q<VisualElement>("enemy-hp-fill");
            _enemyHpText = root.Q<Label>("enemy-hp-text");
            _enemySpFill = root.Q<VisualElement>("enemy-sp-fill");
            _enemySpText = root.Q<Label>("enemy-sp-text");

            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            Unbind();
        }

        // MatchController.Instance / 보드 배정(RegisterBoard, AssignBoardOwner)은 씬 로드 직후
        // 네트워크 스폰·RPC 타이밍에 걸쳐 비동기로 끝나서, 준비될 때까지 한 프레임씩 기다렸다가
        // 구독함(PlayerBoard.RegisterWhenReady()와 같은 패턴 - 매치당 한 번뿐이라 비용 걱정 없음).
        private IEnumerator BindWhenReady()
        {
            while (MatchController.Instance == null) yield return null;

            PlayerBoard local = null;
            while (local == null)
            {
                local = MatchController.Instance.GetLocalBoard();
                yield return null;
            }
            _myBoard = local;

            PlayerBoard enemy = null;
            while (enemy == null)
            {
                enemy = FindObjectsByType<PlayerBoard>(FindObjectsSortMode.None)
                    .FirstOrDefault(b => b != _myBoard);
                yield return null;
            }
            _enemyBoard = enemy;

            Bind();
            _bindRoutine = null;
        }

        private void Bind()
        {
            var mc = MatchController.Instance;
            bool myBoardIsA = _myBoard.BoardIndex == 0;

            _boundMyHpVar = myBoardIsA ? mc.BoardHpA : mc.BoardHpB;
            _boundEnemyHpVar = myBoardIsA ? mc.BoardHpB : mc.BoardHpA;
            _boundMySpVar = myBoardIsA ? mc.BoardSpA : mc.BoardSpB;
            _boundEnemySpVar = myBoardIsA ? mc.BoardSpB : mc.BoardSpA;

            _myHpHandler = (_, hp) => UpdateBar(_myHpFill, _myHpText, hp, _myBoard.Health.MaxHp);
            _enemyHpHandler = (_, hp) => UpdateBar(_enemyHpFill, _enemyHpText, hp, _enemyBoard.Health.MaxHp);
            _mySpHandler = (_, sp) => UpdateBar(_mySpFill, _mySpText, sp, _myBoard.Resources.MaxSP);
            _enemySpHandler = (_, sp) => UpdateBar(_enemySpFill, _enemySpText, sp, _enemyBoard.Resources.MaxSP);

            _boundMyHpVar.OnValueChanged += _myHpHandler;
            _boundEnemyHpVar.OnValueChanged += _enemyHpHandler;
            _boundMySpVar.OnValueChanged += _mySpHandler;
            _boundEnemySpVar.OnValueChanged += _enemySpHandler;

            // OnValueChanged는 "변경될 때"만 불리고 구독 시점의 현재값은 안 불러주므로,
            // 최초 1회는 지금 값으로 직접 반영해줘야 HUD가 빈 채로 시작하지 않음.
            UpdateBar(_myHpFill, _myHpText, _boundMyHpVar.Value, _myBoard.Health.MaxHp);
            UpdateBar(_enemyHpFill, _enemyHpText, _boundEnemyHpVar.Value, _enemyBoard.Health.MaxHp);
            UpdateBar(_mySpFill, _mySpText, _boundMySpVar.Value, _myBoard.Resources.MaxSP);
            UpdateBar(_enemySpFill, _enemySpText, _boundEnemySpVar.Value, _enemyBoard.Resources.MaxSP);
        }

        private void Unbind()
        {
            if (_boundMyHpVar != null && _myHpHandler != null) _boundMyHpVar.OnValueChanged -= _myHpHandler;
            if (_boundEnemyHpVar != null && _enemyHpHandler != null) _boundEnemyHpVar.OnValueChanged -= _enemyHpHandler;
            if (_boundMySpVar != null && _mySpHandler != null) _boundMySpVar.OnValueChanged -= _mySpHandler;
            if (_boundEnemySpVar != null && _enemySpHandler != null) _boundEnemySpVar.OnValueChanged -= _enemySpHandler;

            _boundMyHpVar = null;
            _boundEnemyHpVar = null;
            _boundMySpVar = null;
            _boundEnemySpVar = null;
            _myHpHandler = null;
            _enemyHpHandler = null;
            _mySpHandler = null;
            _enemySpHandler = null;
        }

        private static void UpdateBar(VisualElement fill, Label text, float current, float max)
        {
            float pct = max > 0f ? Mathf.Clamp01(current / max) * 100f : 0f;
            fill.style.width = new Length(pct, LengthUnit.Percent);
            text.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
        }
    }
}
