using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using TowerDefense.Data;
using TowerDefense.Map;

namespace TowerDefense.UI
{
    /// <summary>
    /// 인게임 화면 오른쪽에 고정된 덱(핸드) 패널 - DeckManager.GetActiveDeck()(마지막으로 저장된 편성)을
    /// 5칸에 그대로 표시하고, 클릭한 카드를 TowerPlacementController.SetSelectedCharacter로 넘겨서
    /// "지금 이 카드를 소환 대기 중" 상태로 만듦. 실제 배치는 여전히 TowerPlacementController가
    /// 보드 클릭을 받아서 처리함 - 이 컨트롤러는 "무엇을 선택했는지"만 책임짐.
    ///
    /// 슬롯 개수(5)는 UXML에 정적으로 5개가 박혀있어서 DeckManager.DeckSize(기획 확정 전 더미값)가
    /// 나중에 바뀌면 InGameDeck.uxml의 슬롯 개수도 같이 맞춰줘야 함 - 자동으로 안 늘어남.
    ///
    /// [기획 확정 - 소환 비용 고정] 캐릭터별 개별 SP 비용(CharacterDataSO.summonCost)은 더 이상 안
    /// 씀 - 소환 비용은 모든 캐릭터 공통이며 소환할 때마다 +10(MatchController.BoardSummonCostA/B로 복제됨). 그래서 카드마다
    /// 다른 비용 숫자를 보여주던 배지는 UI에서 제거했고(InGameDeck.uxml 참고), 카드 흐림 표시
    /// (RefreshAffordability)도 그 현재 가격 하나로만 판단함.
    ///
    /// 카드 슬롯들 아래 남는 공간엔 누적 "SP 소모값" 표시가 하나 더 있음(sp-spent-label) - 이것도
    /// 서버가 복제한 다음 소환 가격(BoardSummonCost)에서 소환 횟수를 역산해 계산하는 순수 표시용 값임
    /// (로컬 요청 시점이 아니라 서버 확정 후 갱신됨).
    ///
    /// [카메라 레이아웃 연동] 이 덱 패널(.deck-root)이 실제로 화면에서 차지하는 폭을 첫 레이아웃
    /// 해석 직후(GeometryChangedEvent) 측정해서 InGameDeckLayout을 통해 TowerDefense.Map.
    /// InGameCameraLayout에 알려줌 - 그래야 메인 카메라가 정확히 그만큼만 뷰포트를 줄여서 보드가
    /// 덱 패널 밑에 깔리지 않게 함(InGameDeckLayout.cs 클래스 doc 참고).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InGameDeckController : MonoBehaviour
    {
        private const string SelectedClass = "deck-slot-selected";
        private const string EmptyClass = "deck-slot-empty";
        private const string UnaffordableClass = "deck-slot-unaffordable";

        private UIDocument _document;
        private VisualElement _deckRoot;
        private Label _spSpentLabel;
        private float _lastSp;
        private int _lastCost = MatchResourceManager.SummonBaseCost;
        private bool _reservedFractionReported;

        private readonly Button[] _slotButtons = new Button[DeckManager.DeckSize];

        // [SP 강화] 각 카드 하단의 강화 버튼. 가격/레벨 텍스트는 레벨이 바뀔 때만 다시 만들고(문자열 할당 최소화),
        // SP가 바뀔 때는 캐시한 가격으로 클래스만 토글함(처치마다 SP가 바뀌므로 이 경로는 할당이 없어야 함).
        private const string EnhanceUnaffordableClass = "deck-enhance-unaffordable";
        private const string EnhanceMaxClass = "deck-enhance-max";
        private readonly Button[] _enhanceButtons = new Button[DeckManager.DeckSize];
        private readonly int[] _enhanceCost = new int[DeckManager.DeckSize]; // 다음 강화 실제 가격, 최대 레벨이면 0
        private readonly CharacterDataSO[] _slotData = new CharacterDataSO[DeckManager.DeckSize];

        private int _selectedIndex = -1;
        private bool _deckRootHovered; // InGamePointerOverUI 카운트를 정확히 1번씩만 증감시키기 위한 로컬 상태

        private PlayerBoard _myBoard;
        private NetworkVariable<float> _boundMySpVar;
        private NetworkVariable<float>.OnValueChangedDelegate _mySpHandler;
        private NetworkVariable<int> _boundMyCostVar;
        private NetworkVariable<int>.OnValueChangedDelegate _myCostHandler;
        private Coroutine _bindRoutine;

        private TowerPlacementController _boundPlacementController;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _deckRoot = root.Q<VisualElement>("deck-root");
            // 패널 전체(버튼 사이 여백 포함)를 hover 영역으로 잡음 - 버튼 하나하나가 아니라 패널
            // 전체 위에서의 클릭이 그 아래 보드로 새지 않게 막는 게 목적이라 이렇게 하는 게 더 확실함.
            _deckRoot.RegisterCallback<PointerEnterEvent>(OnDeckRootPointerEnter);
            _deckRoot.RegisterCallback<PointerLeaveEvent>(OnDeckRootPointerLeave);
            // 덱 패널의 실제 레이아웃이 해석된 뒤(최초 1회) 폭을 측정해서 카메라 레이아웃 쪽에 알려줌.
            _deckRoot.RegisterCallback<GeometryChangedEvent>(OnDeckRootGeometryChanged);

            _spSpentLabel = root.Q<Label>("sp-spent-label");
            RefreshSpSpentLabel();

            for (int i = 0; i < DeckManager.DeckSize; i++)
            {
                int capturedIndex = i; // 클로저 캡처 버그 방지 - 루프 변수를 그대로 캡처하면 전부 마지막 값을 참조하게 됨
                var button = root.Q<Button>($"deck-slot-{i}");
                if (button == null)
                {
                    Debug.LogError($"[InGameDeck] UXML에서 deck-slot-{i}을 못 찾음 - InGameDeck.uxml 구조를 확인할 것.");
                    continue;
                }

                _slotButtons[i] = button;
                button.clicked += () => OnSlotClicked(capturedIndex);

                // 카드 안쪽 하단에 강화 버튼을 코드로 붙임(UXML 구조를 안 건드림). 자식 버튼의 클릭은 부모 카드 클릭으로
                // 전파되지 않음(Clickable이 포인터 다운을 소비함).
                var enhance = new Button { name = $"deck-enhance-{i}" };
                enhance.AddToClassList("deck-enhance");
                enhance.clicked += () => OnEnhanceClicked(capturedIndex);
                button.Add(enhance);
                _enhanceButtons[i] = enhance;
            }

            PopulateSlots();
            RefreshEnhanceTexts();
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_deckRootHovered)
            {
                InGamePointerOverUI.Exit();
                _deckRootHovered = false;
            }
            if (_deckRoot != null)
            {
                _deckRoot.UnregisterCallback<PointerEnterEvent>(OnDeckRootPointerEnter);
                _deckRoot.UnregisterCallback<PointerLeaveEvent>(OnDeckRootPointerLeave);
                _deckRoot.UnregisterCallback<GeometryChangedEvent>(OnDeckRootGeometryChanged);
            }
            // clicked 델리게이트는 클로저 람다라 -=로 정확히 못 떼지만, 버튼 자체가 이 GameObject의
            // UIDocument에 속해서 씬과 함께 파괴되므로(InGame 씬 전용, DontDestroyOnLoad 아님) 실질적인
            // 메모리 누수는 아님 - HUD/PauseMenu처럼 씬을 넘나드는 오래 사는 객체가 아니기 때문.
            Unbind();
        }

        private void OnDeckRootPointerEnter(PointerEnterEvent evt)
        {
            if (_deckRootHovered) return;
            _deckRootHovered = true;
            InGamePointerOverUI.Enter();
        }

        private void OnDeckRootPointerLeave(PointerLeaveEvent evt)
        {
            if (!_deckRootHovered) return;
            _deckRootHovered = false;
            InGamePointerOverUI.Exit();
        }

        // panel.visualTree(패널 전체 루트)의 최종 해석된 폭 대비 deck-root 자신의 폭 "비율"만 씀 -
        // 패널 공간 -> 실제 화면 픽셀 변환은 항상 균일한 스케일이라 스케일 계수를 몰라도 이 비율은
        // 화면에서 실제로 차지하는 비율과 정확히 같음(InGameDeckLayout.cs 클래스 doc 참고).
        private void OnDeckRootGeometryChanged(GeometryChangedEvent evt)
        {
            if (_reservedFractionReported) return; // 덱 폭은 런타임 중 안 바뀌므로 최초 1회 측정이면 충분함

            var panelRoot = _deckRoot.panel?.visualTree;
            if (panelRoot == null) return;

            float panelWidth = panelRoot.resolvedStyle.width;
            float deckWidth = _deckRoot.resolvedStyle.width;
            if (panelWidth <= 0f || deckWidth <= 0f) return; // 아직 레이아웃이 완전히 안 잡힌 프레임 - 다음 이벤트를 기다림

            _reservedFractionReported = true;
            InGameDeckLayout.ReportReservedFraction(deckWidth / panelWidth);
        }

        private void PopulateSlots()
        {
            var deck = DeckManager.Instance?.GetActiveDeck();
            bool useFallback = deck == null || deck.All(string.IsNullOrEmpty);

            if (useFallback)
            {
                // MainMenu를 거치지 않고 InGame을 단독 Play했거나(DeckManager 자체가 MainMenu 씬에만
                // 있어서 존재 안 함 - InGameSoloTestBootstrap 참고), 편성을 한 번도 저장한 적이 없어서
                // 5칸이 전부 비어있는 경우임. 그 상태로 두면 카드가 하나도 없어서 배치 테스트 자체가
                // 불가능해지므로, 개발 편의상 캐릭터 목록 앞 5개를 대신 채움(정식 플레이 흐름에서는
                // 항상 저장된 편성이 있어야 정상이고 이 분기를 안 탐).
                Debug.LogWarning("[InGameDeck] 저장된 편성 덱을 찾을 수 없음(MainMenu를 안 거쳤거나 편성 저장 이력이 없음) - " +
                    "테스트용으로 캐릭터 목록 앞 5개를 대신 채움.");
                var all = CharacterDatabase.GetAll();
                for (int i = 0; i < DeckManager.DeckSize; i++)
                {
                    SetSlot(i, i < all.Count ? all[i] : null);
                }
                return;
            }

            for (int i = 0; i < DeckManager.DeckSize; i++)
            {
                string characterId = i < deck.Count ? deck[i] : null;
                var data = string.IsNullOrEmpty(characterId) ? null : CharacterDatabase.GetById(characterId);
                SetSlot(i, data);
            }
        }

        private void SetSlot(int index, CharacterDataSO data)
        {
            _slotData[index] = data;
            var button = _slotButtons[index];
            if (button == null) return;

            if (data == null)
            {
                button.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                button.text = string.Empty;
                button.AddToClassList(EmptyClass);
                return;
            }

            button.RemoveFromClassList(EmptyClass);
            if (data.iconSprite != null)
            {
                button.style.backgroundImage = new StyleBackground(data.iconSprite);
                button.text = string.Empty; // 아이콘이 있으면 이름 텍스트와 안 겹치게 비움
            }
            else
            {
                // [임시 플레이스홀더 - 2026-09-29] 지금 캐릭터 5종 전부 iconSprite가 아직 배정 안 됨
                // (CharacterDataSO 에셋에 스프라이트 없음 - 아트 리소스 자체가 없는 상태). 아이콘 없이
                // .deck-slot의 반투명 배경만 보이면 실제로 편성이 채워졌는데도 "편성창에 아무것도
                // 안 뜬다"고 오인하기 쉬움(실제로 재현됨 - 저장은 성공했는데 슬롯이 빈 것처럼 보여서
                // 소환을 시도조차 안 한 케이스). 아이콘 대신 캐릭터 이름 텍스트라도 보여줘서 최소한
                // "슬롯에 뭐가 들어있는지"는 구분되게 함 - iconSprite가 나중에 배정되면 위 분기를
                // 타면서 이 텍스트는 자동으로 사라짐(따로 되돌릴 코드 필요 없음).
                button.text = data.displayName;
            }
        }

        private void OnSlotClicked(int index)
        {
            var data = _slotData[index];
            if (data == null) return; // 빈 슬롯 - 선택할 카드가 없음

            if (_selectedIndex >= 0 && _slotButtons[_selectedIndex] != null)
            {
                _slotButtons[_selectedIndex].RemoveFromClassList(SelectedClass);
            }

            _selectedIndex = index;
            _slotButtons[index].AddToClassList(SelectedClass);

            TowerPlacementController.Instance?.SetSelectedCharacter(data);
        }

        // 내 보드의 SP 변화를 구독해서, 지금 SP로 못 놓는 카드를 흐리게 표시함. MatchResourceManager
        // (PlayerBoard.Resources)의 OnSPChanged는 서버 사본에서만 발생함(클라이언트 쪽 사본은
        // IsAuthoritative가 false라 아예 갱신되지 않음, MatchResourceManager.cs 참고) - 그래서
        // InGameHUDController와 동일하게 MatchController의 복제된 NetworkVariable(BoardSpA/B)을
        // 구독해야 호스트/클라이언트 양쪽에서 정확히 동작함(PlayerBoard.Resources를 직접 구독하면
        // 클라이언트 화면에서는 절대 안 갱신되는 버그가 생김).
        //
        // 같은 코루틴에서 TowerPlacementController.Instance도 기다렸다가 OnTowerPlaced를 구독함 -
        // "SP 소모값" 표시 카운터가 여기 붙음(둘 다 씬 로드 후 비동기로 준비되는 싱글턴이라 동일한
        // 대기 패턴을 재사용함).
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

            var mc = MatchController.Instance;
            bool myBoardIsA = _myBoard.BoardIndex == 0;
            _boundMySpVar = myBoardIsA ? mc.BoardSpA : mc.BoardSpB;

            _mySpHandler = (_, sp) => RefreshAffordability(sp);
            _boundMySpVar.OnValueChanged += _mySpHandler;

            _boundMyCostVar = myBoardIsA ? mc.BoardSummonCostA : mc.BoardSummonCostB;
            _myCostHandler = (_, cost) => { _lastCost = cost; RefreshAffordability(_lastSp); RefreshSpSpentLabel(); };
            _boundMyCostVar.OnValueChanged += _myCostHandler;
            _lastCost = _boundMyCostVar.Value;

            mc.OnEnhanceLevelChanged += OnEnhanceLevelChanged;
            _enhanceSubscribed = true;
            RefreshEnhanceTexts();

            RefreshAffordability(_boundMySpVar.Value); // 구독 시점의 현재값은 OnValueChanged가 안 불러주므로 최초 1회 직접 반영
            RefreshSpSpentLabel();

            _bindRoutine = null;
        }

        private bool _enhanceSubscribed;

        private void Unbind()
        {
            if (_enhanceSubscribed && MatchController.Instance != null)
            {
                MatchController.Instance.OnEnhanceLevelChanged -= OnEnhanceLevelChanged;
            }
            _enhanceSubscribed = false;

            if (_boundMySpVar != null && _mySpHandler != null)
            {
                _boundMySpVar.OnValueChanged -= _mySpHandler;
            }
            if (_boundMyCostVar != null && _myCostHandler != null)
            {
                _boundMyCostVar.OnValueChanged -= _myCostHandler;
            }
            _myBoard = null;
            _boundMySpVar = null;
            _mySpHandler = null;
            _boundMyCostVar = null;
            _myCostHandler = null;
        }

        private void RefreshSpSpentLabel()
        {
            if (_spSpentLabel == null) return;
            // 다음 소환에 드는 SP만 표시함(이미 쓴 누적량은 표시하지 않음). 소환할 때마다 +10 올라감(100 → 110 → 120 ...).
            // 서버가 복제한 BoardSummonCost를 그대로 보여줌.
            _spSpentLabel.text = $"다음 소환 SP: {_lastCost}";
        }

        private void OnEnhanceLevelChanged(int boardIndex, string characterId, int level)
        {
            if (_myBoard == null || boardIndex != _myBoard.BoardIndex) return;
            RefreshEnhanceTexts();
        }

        // 카드별 강화 레벨/다음 가격 텍스트를 다시 만듦 - 레벨이 바뀌거나 매치에 바인딩될 때만 호출함.
        // 가격은 서버와 같은 계산기(NodeBonusCalculator)로 이 기기의 노드 해금 상태 기준 감소율을 반영함.
        private void RefreshEnhanceTexts()
        {
            var mc = MatchController.Instance;
            for (int i = 0; i < DeckManager.DeckSize; i++)
            {
                var button = _enhanceButtons[i];
                if (button == null) continue;

                var data = _slotData[i];
                if (data == null)
                {
                    button.style.display = DisplayStyle.None;
                    _enhanceCost[i] = 0;
                    continue;
                }
                button.style.display = DisplayStyle.Flex;

                int level = (mc != null && _myBoard != null) ? mc.GetEnhanceLevel(_myBoard.BoardIndex, data.characterId) : 1;
                int baseCost = TowerProgression.GetEnhanceCost(level);
                if (baseCost <= 0)
                {
                    _enhanceCost[i] = 0;
                    button.text = $"Lv{level} MAX";
                    button.AddToClassList(EnhanceMaxClass);
                    button.RemoveFromClassList(EnhanceUnaffordableClass);
                    continue;
                }

                button.RemoveFromClassList(EnhanceMaxClass);
                int cost = NodeBonusCalculator.ApplyEnhanceDiscount(baseCost, NodeBonusCalculator.GetEnhanceDiscountPercent(data));
                _enhanceCost[i] = cost;
                button.text = $"Lv{level} · 강화 {cost}";
            }
            RefreshEnhanceAffordability(_lastSp);
        }

        // SP가 바뀔 때마다 호출됨(처치 보상으로 자주 바뀜) - 문자열을 만들지 않고 클래스만 토글함.
        private void RefreshEnhanceAffordability(float currentSP)
        {
            for (int i = 0; i < DeckManager.DeckSize; i++)
            {
                var button = _enhanceButtons[i];
                if (button == null || _slotData[i] == null || _enhanceCost[i] <= 0) continue;

                if (currentSP < _enhanceCost[i]) button.AddToClassList(EnhanceUnaffordableClass);
                else button.RemoveFromClassList(EnhanceUnaffordableClass);
            }
        }

        private void OnEnhanceClicked(int index)
        {
            var data = _slotData[index];
            var mc = MatchController.Instance;
            if (data == null || mc == null || _myBoard == null) return;

            if (_enhanceCost[index] <= 0)
            {
                ToastController.Instance?.Show("이미 최대 강화입니다");
                return;
            }
            if (mc.GetBoardSp(_myBoard.BoardIndex) < _enhanceCost[index])
            {
                ToastController.Instance?.Show("SP가 부족합니다");
                return;
            }

            // 최종 SP 차감/레벨 반영은 서버가 함. 결과는 OnEnhanceLevelChanged로 돌아와 버튼 텍스트가 갱신됨.
            mc.RequestEnhanceRpc(_myBoard.BoardIndex, data.characterId);
        }

        private void RefreshAffordability(float currentSP)
        {
            _lastSp = currentSP;
            RefreshEnhanceAffordability(currentSP);
            bool affordable = currentSP >= _lastCost;
            for (int i = 0; i < DeckManager.DeckSize; i++)
            {
                var data = _slotData[i];
                var button = _slotButtons[i];
                if (data == null || button == null) continue;

                if (!affordable) button.AddToClassList(UnaffordableClass);
                else button.RemoveFromClassList(UnaffordableClass);
            }
        }
    }
}
