using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using TowerDefense.Map;
using TowerDefense.Data;

namespace TowerDefense.UI
{
    /// <summary>
    /// 인게임 HUD - 내 보드/상대 보드의 기지 체력(HP)과 SP를 실시간으로 보여줌. 현재값은
    /// MatchController(서버 권위, Assets/Scripts/Map/MatchController.cs)의 NetworkVariable
    /// (BoardHpA/B, BoardSpA/B)을 구독해서 받고, 최대값(MaxHp/MaxSP)은 씬에 이미 배치된
    /// PlayerBoard의 로컬 컴포넌트 값을 그대로 씀 - 둘 다 클라이언트/서버 씬 파일에 동일하게
    /// 박혀있는 인스펙터 값이라 따로 네트워크 동기화가 필요 없음.
    ///
    /// 중앙의 일시정지 버튼은 PauseMenuController 팝업을 여는 것 외엔 아무 상태도 안 갖고 있음
    /// (Assets/UI/Popups/PauseMenu 참고) - HUD 자체는 항상 떠있어야 하므로 팝업 열림/닫힘과 무관함.
    ///
    /// [구조 조정 - 계절/날씨 시스템] MatchController.SelectedSeason/CurrentWeather를 HP/SP와 같은
    /// OnValueChanged 구독 패턴으로 텍스트 표시하고, 낙엽 날씨의 시야 가림 연출은
    /// MatchController.OnVisionBlockStarted 이벤트를 받아 vision-block-overlay를 페이드 인/아웃함
    /// (순수 시각 효과 - 게임플레이 수치와 무관, "본게임 화면 구성" 슬라이드의 "추가 예정(기획안)"
    /// 항목 중 계절/날씨 표시를 구현한 것).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InGameHUDController : MonoBehaviour
    {
        [Tooltip("일시정지 팝업 GameObject (하이어라키에서 기본 비활성화 상태) - PauseMenuController가 붙어있어야 함")]
        [SerializeField] private PauseMenuController pauseMenuPopup;

        private UIDocument _document;

        private VisualElement _myHpFill;
        private Label _myHpText;
        private VisualElement _mySpFill;
        private Label _mySpText;

        private VisualElement _enemyHpFill;
        private Label _enemyHpText;
        private VisualElement _enemySpFill;
        private Label _enemySpText;

        private Button _pauseButton;
        private bool _pauseButtonHovered; // InGamePointerOverUI 카운트를 정확히 1번씩만 증감시키기 위한 로컬 상태

        private Label _seasonWeatherLabel;
        private VisualElement _visionBlockOverlay;
        private Coroutine _visionBlockRoutine;

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

        // 계절/날씨 표시(SelectedSeason/CurrentWeather)와 낙엽 시야 가림 연출(OnVisionBlockStarted) -
        // HP/SP와 같은 이유로 델리게이트 인스턴스를 필드에 저장해둠(구독 해제 시 정확히 같은
        // 인스턴스로 -=해야 누수 없이 해제됨).
        private NetworkVariable<int>.OnValueChangedDelegate _seasonHandler;
        private NetworkVariable<int>.OnValueChangedDelegate _weatherHandler;
        private NetworkVariable<int> _boundSeasonVar;
        private NetworkVariable<int> _boundWeatherVar;
        private System.Action<float> _visionBlockHandler;

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

            _seasonWeatherLabel = root.Q<Label>("season-weather-label");
            _visionBlockOverlay = root.Q<VisualElement>("vision-block-overlay");

            _pauseButton = root.Q<Button>("pause-button");
            _pauseButton.clicked += OnPauseClicked;
            // 일시정지 버튼 위에서의 클릭이 그 아래 보드 칸에 타워 배치로 새는 걸 막음(InGamePointerOverUI 참고).
            _pauseButton.RegisterCallback<PointerEnterEvent>(OnPauseButtonPointerEnter);
            _pauseButton.RegisterCallback<PointerLeaveEvent>(OnPauseButtonPointerLeave);

            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_pauseButton != null)
            {
                _pauseButton.clicked -= OnPauseClicked;
                _pauseButton.UnregisterCallback<PointerEnterEvent>(OnPauseButtonPointerEnter);
                _pauseButton.UnregisterCallback<PointerLeaveEvent>(OnPauseButtonPointerLeave);
            }
            // 포인터가 버튼 위에 있는 채로 이 UI 자체가 비활성화될 수 있어서(예: 씬 전환) PointerLeaveEvent가
            // 안 올 수 있음 - _pauseButtonHovered로 "지금 우리가 실제로 카운트를 올려둔 상태인지"를 정확히
            // 추적해서, 그 경우에만 Exit()을 호출함(안 그러면 다른 UI의 hover 카운트를 잘못 깎을 수 있음).
            if (_pauseButtonHovered)
            {
                InGamePointerOverUI.Exit();
                _pauseButtonHovered = false;
            }
            if (_visionBlockRoutine != null)
            {
                StopCoroutine(_visionBlockRoutine);
                _visionBlockRoutine = null;
            }
            Unbind();
        }

        private void OnPauseButtonPointerEnter(PointerEnterEvent evt)
        {
            if (_pauseButtonHovered) return;
            _pauseButtonHovered = true;
            InGamePointerOverUI.Enter();
        }

        private void OnPauseButtonPointerLeave(PointerLeaveEvent evt)
        {
            if (!_pauseButtonHovered) return;
            _pauseButtonHovered = false;
            InGamePointerOverUI.Exit();
        }

        private void OnPauseClicked()
        {
            if (pauseMenuPopup == null)
            {
                Debug.LogError("[InGameHUD] pauseMenuPopup이 인스펙터에 연결 안 됨.");
                return;
            }
            pauseMenuPopup.Open();
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
            _mySpHandler = (_, sp) => UpdateSp(_mySpFill, _mySpText, sp);
            _enemySpHandler = (_, sp) => UpdateSp(_enemySpFill, _enemySpText, sp);

            _boundMyHpVar.OnValueChanged += _myHpHandler;
            _boundEnemyHpVar.OnValueChanged += _enemyHpHandler;
            _boundMySpVar.OnValueChanged += _mySpHandler;
            _boundEnemySpVar.OnValueChanged += _enemySpHandler;

            // OnValueChanged는 "변경될 때"만 불리고 구독 시점의 현재값은 안 불러주므로,
            // 최초 1회는 지금 값으로 직접 반영해줘야 HUD가 빈 채로 시작하지 않음.
            UpdateBar(_myHpFill, _myHpText, _boundMyHpVar.Value, _myBoard.Health.MaxHp);
            UpdateBar(_enemyHpFill, _enemyHpText, _boundEnemyHpVar.Value, _enemyBoard.Health.MaxHp);
            UpdateSp(_mySpFill, _mySpText, _boundMySpVar.Value);
            UpdateSp(_enemySpFill, _enemySpText, _boundEnemySpVar.Value);

            // 계절/날씨 표시 - 계절(SelectedSeason)은 매치 시작 시 1회만 정해지고 안 바뀌지만,
            // 날씨(CurrentWeather)는 웨이브마다 바뀌므로 그 값이 바뀔 때마다 라벨을 갱신함.
            _boundSeasonVar = mc.SelectedSeason;
            _boundWeatherVar = mc.CurrentWeather;
            _seasonHandler = (_, __) => UpdateSeasonWeatherLabel();
            _weatherHandler = (_, __) => UpdateSeasonWeatherLabel();
            _boundSeasonVar.OnValueChanged += _seasonHandler;
            _boundWeatherVar.OnValueChanged += _weatherHandler;
            UpdateSeasonWeatherLabel();

            // 낙엽 날씨 시야 가림 연출 - MatchController가 웨이브마다 낙엽이 뽑혔을 때만 1회 브로드캐스트함.
            _visionBlockHandler = OnVisionBlockStarted;
            mc.OnVisionBlockStarted += _visionBlockHandler;
        }

        private void Unbind()
        {
            if (_boundMyHpVar != null && _myHpHandler != null) _boundMyHpVar.OnValueChanged -= _myHpHandler;
            if (_boundEnemyHpVar != null && _enemyHpHandler != null) _boundEnemyHpVar.OnValueChanged -= _enemyHpHandler;
            if (_boundMySpVar != null && _mySpHandler != null) _boundMySpVar.OnValueChanged -= _mySpHandler;
            if (_boundEnemySpVar != null && _enemySpHandler != null) _boundEnemySpVar.OnValueChanged -= _enemySpHandler;
            if (_boundSeasonVar != null && _seasonHandler != null) _boundSeasonVar.OnValueChanged -= _seasonHandler;
            if (_boundWeatherVar != null && _weatherHandler != null) _boundWeatherVar.OnValueChanged -= _weatherHandler;
            if (MatchController.Instance != null && _visionBlockHandler != null) MatchController.Instance.OnVisionBlockStarted -= _visionBlockHandler;

            _boundMyHpVar = null;
            _boundEnemyHpVar = null;
            _boundMySpVar = null;
            _boundEnemySpVar = null;
            _boundSeasonVar = null;
            _boundWeatherVar = null;
            _myHpHandler = null;
            _enemyHpHandler = null;
            _mySpHandler = null;
            _enemySpHandler = null;
            _seasonHandler = null;
            _weatherHandler = null;
            _visionBlockHandler = null;
        }

        // 화면 텍스트는 "봄 · 미세먼지"처럼 계절/날씨를 한글로 표시함(전용 아이콘 에셋이 아직
        // 없어서 텍스트로만 - 슬라이드 원문 명칭 그대로 씀). Clear(맑음)일 땐 "맑음"만 표시함.
        private void UpdateSeasonWeatherLabel()
        {
            if (_seasonWeatherLabel == null || _boundSeasonVar == null || _boundWeatherVar == null) return;

            var season = (SeasonType)_boundSeasonVar.Value;
            var weather = (WeatherType)_boundWeatherVar.Value;
            _seasonWeatherLabel.text = $"{SeasonDisplayName(season)} · {WeatherDisplayName(weather)}";
        }

        private static string SeasonDisplayName(SeasonType season) => season switch
        {
            SeasonType.Spring => "봄",
            SeasonType.Summer => "여름",
            SeasonType.Fall => "가을",
            SeasonType.Winter => "겨울",
            _ => "-",
        };

        private static string WeatherDisplayName(WeatherType weather) => weather switch
        {
            WeatherType.Clear => "맑음",
            WeatherType.Pollen => "꽃가루",
            WeatherType.FineDust => "미세먼지",
            WeatherType.Flood => "홍수",
            WeatherType.Heatwave => "폭염",
            WeatherType.FallenLeaves => "낙엽",
            WeatherType.Fog => "안개",
            WeatherType.Snow => "눈",
            WeatherType.Hail => "우박",
            WeatherType.Rain => "비",
            WeatherType.Wind => "바람",
            _ => "-",
        };

        // 낙엽 날씨 시작 브로드캐스트(MatchController.OnVisionBlockStarted) 수신 - 오버레이를
        // 페이드 인/유지/페이드 아웃함. 순수 시각 연출이라 게임플레이 로직에는 전혀 손 안 댐.
        private void OnVisionBlockStarted(float durationSeconds)
        {
            if (_visionBlockOverlay == null) return;
            if (_visionBlockRoutine != null) StopCoroutine(_visionBlockRoutine);
            _visionBlockRoutine = StartCoroutine(VisionBlockRoutine(durationSeconds));
        }

        private IEnumerator VisionBlockRoutine(float durationSeconds)
        {
            _visionBlockOverlay.style.opacity = 1f; // USS transition-duration(0.4s)이 알아서 페이드 인시킴
            yield return new WaitForSeconds(Mathf.Max(0f, durationSeconds - 0.4f));
            _visionBlockOverlay.style.opacity = 0f; // 페이드 아웃
            _visionBlockRoutine = null;
        }

        // SP는 상한이 없는 누적 지갑이라 게이지 대신 숫자만 보여줌(채움 막대는 숨김).
        private static void UpdateSp(VisualElement fill, Label text, float current)
        {
            if (fill != null) fill.style.display = DisplayStyle.None;
            text.text = Mathf.RoundToInt(current).ToString();
        }

        private static void UpdateBar(VisualElement fill, Label text, float current, float max)
        {
            float pct = max > 0f ? Mathf.Clamp01(current / max) * 100f : 0f;
            fill.style.width = new Length(pct, LengthUnit.Percent);
            text.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
        }
    }
}
