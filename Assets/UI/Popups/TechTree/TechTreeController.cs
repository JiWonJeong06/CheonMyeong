using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Data;

namespace TowerDefense.UI
{
    /// <summary>
    /// 노드트리(강화) 팝업 - 정중앙엔 아무 글자도 없는 "코어" 노드(일반 노드보다 큼)가 있고,
    /// 선행조건이 없는 모든 노드(스탯 노드든 캐릭터 해금 노드든)는 전부 이 코어 노드와 선으로
    /// 이어짐 - 그래야 다른 노드의 선행조건으로 한 번도 안 쓰이는 독립 노드도 트리에서
    /// 고립되지 않음. 그 바깥쪽은 코어에서 갈라져나가는 "가지"별로 원 둘레의 한 구간(부채꼴)을
    /// 배정하고, 자식 노드는 부모의 구간을 물려받아 서브트리 크기 비율로 쪼개는 재귀적 방사형
    /// 트리 레이아웃으로 배치함(패스오브엑자일 스킬트리 스타일 - LayoutNodes 참고) - 그래서 같은
    /// 가지의 후손들은 항상 비슷한 방향에 몰려 있고, 선이 코어를 가로질러 다른 가지와 꼬이지 않음.
    /// 노드 모양은 육각형 - UI Toolkit엔 다각형 클리핑이 없어서 Painter2D로 직접 채워 그림
    /// (HexagonVisual 참고). 캔버스 크기가 바뀔 때마다(팝업 최초 오픈 시점 포함, 전체화면의
    /// 70~90% 크기라 해상도에 따라 달라짐) 다시 배치하도록 GeometryChangedEvent를 구독함.
    /// 빈 공간(노드가 아닌 부분)을 드래그하면 트리 전체가 이동함 - PointerDownEvent의 target이
    /// 캔버스 자신일 때만 드래그를 시작해서 노드 클릭과 겹치지 않게 함.
    /// 검색창에 입력하면 이름이 일치하는 노드는 육각형 테두리(링)로 강조하고, 나머지는
    /// 투명도를 낮춤(UI Toolkit 런타임엔 box-shadow/글로우가 없어서 이 조합으로 근사함).
    /// 구조만 잡아두는 단계라 실제 강화 효과 적용(전투 스탯 반영)은 안 하고, 해금 여부만 관리함.
    ///
    /// [디버그] 우측 상단 "[테스트] 초기화" 버튼은 QA 편의용 더미 기능 - 캐릭터/스탯 노드 해금과
    /// 재화를 전부 초기 상태로 되돌림. 정식 릴리즈 전에는 반드시 제거할 것(기획 확인 필요).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TechTreeController : MonoBehaviour, IPopupPanel
    {
        [Tooltip("확인창 GameObject (하이어라키에서 기본 비활성화 상태)")]
        [SerializeField] private ConfirmDialogController confirmDialog;

        private const float NodeSize = 80f;
        private const float CoreNodeSize = 110f; // 코어 노드는 일반 노드보다 크게
        private const float EdgeMargin = 12f; // 캔버스 가장자리와 가장 바깥쪽 노드 사이 최소 여백
        private const float CoreNodeGap = 40f; // 코어 테두리와 첫 번째 링(depth 0) 노드 사이 최소 간격

        private static readonly Color UnlockedFillColor = new(70f / 255f, 150f / 255f, 90f / 255f);
        private static readonly Color AvailableFillColor = new(70f / 255f, 110f / 255f, 190f / 255f);
        private static readonly Color UnavailableFillColor = new(60f / 255f, 60f / 255f, 68f / 255f);
        private static readonly Color CharacterNodeRingColor = new(170f / 255f, 120f / 255f, 220f / 255f);
        private static readonly Color SearchMatchRingColor = new(255f / 255f, 225f / 255f, 110f / 255f);
        private static readonly Color CoreFillColor = new(50f / 255f, 50f / 255f, 58f / 255f);
        private static readonly Color CoreRingColor = new(140f / 255f, 140f / 255f, 155f / 255f);

        private UIDocument _document;
        private VisualElement _canvas;
        private VisualElement _world; // 캔버스 안에서 실제로 드래그(팬)되는 컨테이너 - 코어/노드/연결선이 여기 들어감
        private ConnectorLayer _connectorLayer;
        private VisualElement _coreElement;
        private Button _closeButton;
        private TextField _searchField;
        private Button _debugResetButton;

        private readonly Dictionary<string, VisualElement> _nodeElements = new();
        private readonly Dictionary<string, HexagonVisual> _nodeHexVisuals = new();

        private bool _isDragging;
        private Vector2 _panOffset;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartPanOffset;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            _canvas = root.Q<VisualElement>("techtree-canvas");
            _closeButton = root.Q<Button>("close-button");
            _searchField = root.Q<TextField>("techtree-search-field");
            _debugResetButton = root.Q<Button>("debug-reset-button");

            _closeButton.clicked += OnCloseClicked;
            _debugResetButton.clicked += OnDebugResetClicked;
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _searchField.SetValueWithoutNotify(string.Empty);

            _canvas.RegisterCallback<GeometryChangedEvent>(OnCanvasResized);
            _canvas.RegisterCallback<PointerDownEvent>(OnCanvasPointerDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnCanvasPointerMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnCanvasPointerUp);
            _canvas.RegisterCallback<PointerCaptureOutEvent>(OnCanvasPointerCaptureOut);

            // 팝업을 새로 열 때마다 이전에 옮겨둔 화면 위치는 초기화하고 중앙에서 다시 시작.
            _panOffset = Vector2.zero;
            _isDragging = false;

            _world = new VisualElement { name = "techtree-world" };
            _world.style.position = Position.Absolute;
            _world.style.left = 0;
            _world.style.top = 0;
            _world.style.right = 0;
            _world.style.bottom = 0;
            _canvas.Add(_world);

            BuildGrid();
        }

        private void OnDisable()
        {
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
            if (_debugResetButton != null) _debugResetButton.clicked -= OnDebugResetClicked;
            if (_searchField != null) _searchField.UnregisterValueChangedCallback(OnSearchChanged);

            if (_canvas != null)
            {
                _canvas.UnregisterCallback<GeometryChangedEvent>(OnCanvasResized);
                _canvas.UnregisterCallback<PointerDownEvent>(OnCanvasPointerDown);
                _canvas.UnregisterCallback<PointerMoveEvent>(OnCanvasPointerMove);
                _canvas.UnregisterCallback<PointerUpEvent>(OnCanvasPointerUp);
                _canvas.UnregisterCallback<PointerCaptureOutEvent>(OnCanvasPointerCaptureOut);
            }

            if (TechTreeManager.Instance != null)
            {
                TechTreeManager.Instance.OnNodeUnlocked -= OnNodeUnlockedChanged;
            }
        }

        public void Open() => PopupManager.Instance.Open(this);

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        private void OnCanvasResized(GeometryChangedEvent evt) => LayoutNodes();

        #region 드래그(팬) 처리

        // target이 캔버스 자신일 때만 드래그 시작 - 노드 위에서 누른 경우엔 target이 그 노드라
        // 여기로는 안 들어오고(버블링은 되지만 target은 그대로 노드) 노드 클릭이 정상적으로 우선됨.
        private void OnCanvasPointerDown(PointerDownEvent evt)
        {
            if (evt.target != _canvas) return;

            _isDragging = true;
            _dragStartPointer = evt.position;
            _dragStartPanOffset = _panOffset;
            _canvas.CapturePointer(evt.pointerId);
        }

        private void OnCanvasPointerMove(PointerMoveEvent evt)
        {
            if (!_isDragging) return;

            Vector2 delta = (Vector2)evt.position - _dragStartPointer;
            _panOffset = _dragStartPanOffset + delta;
            // style.translate는 레이아웃(Yoga) 재계산 없이 화면에 그려지는 위치만 옮기는 렌더 트랜스폼이라
            // 드래그 중 매 프레임 호출해도 성능 부담이 적음.
            // (구버전 API인 VisualElement.transform.position은 Unity 6000.3 기준 Obsolete라 안 씀.)
            _world.style.translate = new Translate(_panOffset.x, _panOffset.y, 0f);
        }

        private void OnCanvasPointerUp(PointerUpEvent evt) => EndDrag(evt.pointerId);

        private void OnCanvasPointerCaptureOut(PointerCaptureOutEvent evt) => _isDragging = false;

        private void EndDrag(int pointerId)
        {
            if (!_isDragging) return;
            _isDragging = false;
            if (_canvas.HasPointerCapture(pointerId)) _canvas.ReleasePointer(pointerId);
        }

        #endregion

        private void BuildGrid()
        {
            _world.Clear();
            _nodeElements.Clear();
            _nodeHexVisuals.Clear();

            if (TechTreeManager.Instance == null)
            {
                Debug.LogWarning("[TechTree] TechTreeManager를 찾을 수 없음 - 씬에 배치했는지 확인할 것.");
                return;
            }

            TechTreeManager.Instance.OnNodeUnlocked -= OnNodeUnlockedChanged;
            TechTreeManager.Instance.OnNodeUnlocked += OnNodeUnlockedChanged;

            // 선(연결선)이 가장 먼저, 그다음 코어, 그다음 일반 노드 순으로 추가해야
            // 아래에서부터 선 -> 코어 -> 노드 순으로 겹쳐 그려짐.
            _connectorLayer = new ConnectorLayer();
            _world.Add(_connectorLayer);

            _coreElement = BuildCoreElement();
            _world.Add(_coreElement);

            foreach (var node in TechTreeManager.Instance.GetAllNodes())
            {
                var element = BuildNodeElement(node);
                _world.Add(element);
                _nodeElements[node.nodeId] = element;
            }

            LayoutNodes();
            ApplySearchFilter(_searchField != null ? _searchField.value : string.Empty);
        }

        // 정중앙에 놓일, 아무 글자도 없는 코어 노드. 장식용이라 클릭은 안 받음(PickingMode.Ignore).
        private VisualElement BuildCoreElement()
        {
            var core = new VisualElement { pickingMode = PickingMode.Ignore };
            core.AddToClassList("tech-node-core");

            var hex = new HexagonVisual
            {
                FillColor = CoreFillColor,
                RingColor = CoreRingColor,
                RingWidth = 3f
            };
            core.Add(hex);

            return core;
        }

        // 방사형 트리(패스오브엑자일 스킬트리 스타일) 배치. depth(선행조건 단계)는 반지름을,
        // "1차 선행조건(primaryParent = prerequisiteNodeIds[0])" 기준 트리 구조는 각도를 결정함.
        // depth별로 화면을 독립적인 링으로 나눠서 개수만큼 균등 분배하던 예전 방식은 부모-자식이
        // 같은 가지(방향)에 놓인다는 보장이 없어 선이 코어를 가로질러 꼬여 보이는 문제가 있었음 -
        // 그래서 각 depth-0 루트(코어에서 뻗어나가는 "가지")마다 원 둘레의 한 구간(sector)을 배정하고,
        // 그 자식들은 부모의 구간을 서브트리 크기 비율로 쪼개 물려받는 재귀 구조로 바꿈 -
        // 이러면 같은 가지의 후손 노드들이 항상 부모와 비슷한 각도(=같은 방향)에 놓여서
        // 코어를 중심으로 가지가 갈라져 나가는 모양이 유지됨. (2개 이상의 선행조건을 가진 노드는
        // 각도 배치엔 첫 번째 선행조건만 부모로 쓰지만, 실제 연결선은 모든 선행조건에 대해 그려서
        // 가지 사이를 잇는 교차선도 표현됨.)
        private void LayoutNodes()
        {
            if (TechTreeManager.Instance == null) return;

            float w = _canvas.resolvedStyle.width;
            float h = _canvas.resolvedStyle.height;
            // 팝업이 막 열린 첫 프레임엔 레이아웃 계산이 아직 안 끝나 0이 나올 수 있음 -
            // 그런 경우 여기서 그냥 리턴하면, 이후 실제 크기가 잡히는 순간 GeometryChangedEvent가
            // 다시 호출해줘서 알아서 재배치됨.
            if (w <= 0f || h <= 0f) return;

            var nodes = TechTreeManager.Instance.GetAllNodes();
            if (nodes.Count == 0) return;

            var depthById = ComputeDepths(nodes);
            int maxDepth = depthById.Values.DefaultIfEmpty(0).Max();

            // 1차 선행조건(primaryParent) 기준 트리 구조 구축 - 각도 배정에만 사용함.
            var childrenByParent = new Dictionary<string, List<TechNodeData>>();
            var roots = new List<TechNodeData>();
            foreach (var node in nodes)
            {
                string primaryParent = (node.prerequisiteNodeIds != null && node.prerequisiteNodeIds.Count > 0)
                    ? node.prerequisiteNodeIds[0]
                    : null;

                if (string.IsNullOrEmpty(primaryParent))
                {
                    roots.Add(node);
                    continue;
                }

                if (!childrenByParent.TryGetValue(primaryParent, out var list))
                {
                    list = new List<TechNodeData>();
                    childrenByParent[primaryParent] = list;
                }
                list.Add(node);
            }

            // 서브트리 크기(자기 자신 + 모든 후손 수) - 가지마다 배정할 각도 폭을 이 크기에 비례하게
            // 나눠서, 후손이 많은 가지가 더 넓은 부채꼴을 차지하게 함(전부 균등폭이면 노드 많은 가지가
            // 좁은 구간에 몰려서 서로 겹침).
            var subtreeSizeById = new Dictionary<string, int>();
            int GetSubtreeSize(TechNodeData node)
            {
                if (subtreeSizeById.TryGetValue(node.nodeId, out var cached)) return cached;
                int size = 1;
                if (childrenByParent.TryGetValue(node.nodeId, out var kids))
                {
                    foreach (var kid in kids) size += GetSubtreeSize(kid);
                }
                subtreeSizeById[node.nodeId] = size;
                return size;
            }
            foreach (var root in roots) GetSubtreeSize(root);

            float cx = w / 2f;
            float cy = h / 2f;
            var corePos = new Vector2(cx, cy);
            float maxRadius = Mathf.Max(0f, Mathf.Min(w, h) / 2f - NodeSize / 2f - EdgeMargin);
            // depth 0(첫 번째 링) 노드는 코어 테두리에서 최소 CoreNodeGap만큼 떨어진 지점부터 시작하고,
            // 그 뒤(depth 1, 2, ...)는 남은 공간을 depth 수만큼 균등하게 나눠 바깥으로 퍼짐 -
            // 예전엔 반지름 0(코어 중심)부터 링 간격을 계산해서 depth 0 노드가 코어와 거의 겹쳐 보였음.
            float coreClearance = Mathf.Min(CoreNodeSize / 2f + NodeSize / 2f + CoreNodeGap, maxRadius);
            float ringStep = maxDepth > 0 ? (maxRadius - coreClearance) / maxDepth : 0f;

            var positions = new Dictionary<string, Vector2>();

            // node에 배정된 [angleStart, angleEnd] 구간의 중앙 각도에 놓고, 자식들은 이 구간을
            // 서브트리 크기 비율로 나눠 물려받아 재귀적으로 배치함.
            void AssignSector(TechNodeData node, float angleStart, float angleEnd)
            {
                float angle = (angleStart + angleEnd) / 2f;
                float radius = coreClearance + ringStep * depthById[node.nodeId];
                float rad = angle * Mathf.Deg2Rad;
                positions[node.nodeId] = new Vector2(cx + radius * Mathf.Cos(rad), cy + radius * Mathf.Sin(rad));

                if (!childrenByParent.TryGetValue(node.nodeId, out var kids) || kids.Count == 0) return;

                float totalWeight = kids.Sum(k => subtreeSizeById[k.nodeId]);
                float sectorSpan = angleEnd - angleStart;
                float cursor = angleStart;
                foreach (var kid in kids)
                {
                    float share = sectorSpan * (subtreeSizeById[kid.nodeId] / totalWeight);
                    AssignSector(kid, cursor, cursor + share);
                    cursor += share;
                }
            }

            // 루트(가지)들은 원 둘레 360도를 서브트리 크기 비율로 나눠 가짐 - 정각(-90도, 위쪽)부터
            // 시계방향으로 배정.
            float totalRootWeight = roots.Sum(r => subtreeSizeById[r.nodeId]);
            if (totalRootWeight > 0)
            {
                float rootCursor = -90f;
                foreach (var root in roots)
                {
                    float share = 360f * (subtreeSizeById[root.nodeId] / totalRootWeight);
                    AssignSector(root, rootCursor, rootCursor + share);
                    rootCursor += share;
                }
            }

            foreach (var kvp in positions)
            {
                if (!_nodeElements.TryGetValue(kvp.Key, out var element)) continue;
                element.style.left = kvp.Value.x - NodeSize / 2f;
                element.style.top = kvp.Value.y - NodeSize / 2f;
            }

            _coreElement.style.left = corePos.x - CoreNodeSize / 2f;
            _coreElement.style.top = corePos.y - CoreNodeSize / 2f;

            _connectorLayer.style.width = w;
            _connectorLayer.style.height = h;
            _connectorLayer.Lines.Clear();

            // 1) 실제 선행조건 기반 연결선 - 선행조건이 여러 개인 노드(예: 복합 강화)는 primaryParent가
            //    아닌 조건들에 대해서도 여기서 전부 선을 그려서 가지 사이의 교차 연결까지 표현함.
            foreach (var node in nodes)
            {
                if (node.prerequisiteNodeIds == null) continue;
                if (!positions.TryGetValue(node.nodeId, out var to)) continue;

                bool childUnlocked = TechTreeManager.Instance.IsUnlocked(node.nodeId);
                foreach (var prereqId in node.prerequisiteNodeIds)
                {
                    if (!positions.TryGetValue(prereqId, out var from)) continue;
                    _connectorLayer.Lines.Add(new ConnectorLayer.Line(from, to, childUnlocked));
                }
            }

            // 2) 코어 -> 루트(depth 0) 노드 연결선. 다른 노드의 선행조건으로 한 번도 안 쓰이는 독립 노드
            //    (예: 캐릭터 해금 노드)도 이 연결선 덕분에 항상 트리에 이어져 있음.
            foreach (var node in roots)
            {
                if (!positions.TryGetValue(node.nodeId, out var to)) continue;
                bool unlocked = TechTreeManager.Instance.IsUnlocked(node.nodeId);
                _connectorLayer.Lines.Add(new ConnectorLayer.Line(corePos, to, unlocked));
            }

            _connectorLayer.Redraw();
        }

        // 각 노드의 "선행조건 단계 깊이" 계산 (선행조건 없음 = 0, 있으면 선행조건들 중 가장 깊은 값 + 1).
        // 재귀 + 메모이제이션. 순환 참조(A가 B의 선행조건인데 B도 A의 선행조건)는 기획 데이터 상
        // 나오면 안 되는 케이스라 여기서 별도로 방어하진 않음.
        private static Dictionary<string, int> ComputeDepths(IReadOnlyList<TechNodeData> nodes)
        {
            var byId = nodes.ToDictionary(n => n.nodeId);
            var depth = new Dictionary<string, int>();

            int Get(string id)
            {
                if (depth.TryGetValue(id, out var cached)) return cached;
                if (!byId.TryGetValue(id, out var node)) return 0;

                if (node.prerequisiteNodeIds == null || node.prerequisiteNodeIds.Count == 0)
                {
                    depth[id] = 0;
                    return 0;
                }

                int maxPrereqDepth = 0;
                foreach (var prereqId in node.prerequisiteNodeIds)
                {
                    maxPrereqDepth = Mathf.Max(maxPrereqDepth, Get(prereqId));
                }
                int result = maxPrereqDepth + 1;
                depth[id] = result;
                return result;
            }

            foreach (var node in nodes) Get(node.nodeId);
            return depth;
        }

        private VisualElement BuildNodeElement(TechNodeData node)
        {
            bool unlocked = TechTreeManager.Instance.IsUnlocked(node.nodeId);
            bool available = !unlocked && TechTreeManager.Instance.ArePrerequisitesMet(node.nodeId);
            bool isCharacterNode = !string.IsNullOrEmpty(node.linkedCharacterId);

            var wrapper = new VisualElement();
            wrapper.AddToClassList("tech-node");
            wrapper.AddToClassList(unlocked ? "unlocked" : (available ? "available" : "unavailable"));
            // character-node 클래스는 이제 CSS에서 실제로 쓰진 않음(테두리는 아래 hex가 직접 그림) -
            // UI Builder 등에서 구분해보기 편하도록 마커 용도로만 남겨둠.
            if (isCharacterNode) wrapper.AddToClassList("character-node");

            var hex = new HexagonVisual { FillColor = GetFillColor(unlocked, available) };
            ApplyRingStyle(hex, isCharacterNode, isSearchMatch: false);
            wrapper.Add(hex);
            _nodeHexVisuals[node.nodeId] = hex;

            var label = new Label(unlocked ? node.displayName : $"{node.displayName}\n({GetCostSymbolText(node)})");
            label.AddToClassList("tech-node-label");
            wrapper.Add(label);

            wrapper.RegisterCallback<ClickEvent>(_ => OnNodeClicked(node));

            return wrapper;
        }

        private static Color GetFillColor(bool unlocked, bool available)
        {
            if (unlocked) return UnlockedFillColor;
            return available ? AvailableFillColor : UnavailableFillColor;
        }

        // 검색 일치가 캐릭터 노드 테두리보다 우선순위가 높음(더 눈에 띄어야 하니까).
        private static void ApplyRingStyle(HexagonVisual hex, bool isCharacterNode, bool isSearchMatch)
        {
            if (isSearchMatch)
            {
                hex.RingColor = SearchMatchRingColor;
                hex.RingWidth = 4f;
            }
            else if (isCharacterNode)
            {
                hex.RingColor = CharacterNodeRingColor;
                hex.RingWidth = 3f;
            }
            else
            {
                hex.RingColor = Color.clear;
                hex.RingWidth = 0f;
            }
            hex.Redraw();
        }

        // 캐릭터 해금 노드는 CharacterDataSO.unlockCost/unlockCurrency(Gold 또는 Gem)를 그대로 표시하고,
        // 순수 스탯 노드는 기존처럼 항상 Gold만 씀. 버튼 라벨은 기호(G/◆)로 짧게, 확인창은 이름(골드/보석)으로.
        private static string GetCostSymbolText(TechNodeData node)
        {
            if (!string.IsNullOrEmpty(node.linkedCharacterId))
            {
                var characterData = CharacterDatabase.GetById(node.linkedCharacterId);
                if (characterData != null)
                {
                    string symbol = characterData.unlockCurrency == CurrencyType.Gem ? "◆" : "G";
                    return $"{symbol} {characterData.unlockCost}";
                }
            }
            return $"G {node.cost}";
        }

        private static string GetCostWordText(TechNodeData node)
        {
            if (!string.IsNullOrEmpty(node.linkedCharacterId))
            {
                var characterData = CharacterDatabase.GetById(node.linkedCharacterId);
                if (characterData != null)
                {
                    string currencyName = characterData.unlockCurrency == CurrencyType.Gem ? "보석" : "골드";
                    return $"{currencyName} {characterData.unlockCost}";
                }
            }
            return $"골드 {node.cost}";
        }

        private void OnNodeClicked(TechNodeData node)
        {
            bool unlocked = TechTreeManager.Instance.IsUnlocked(node.nodeId);
            if (unlocked)
            {
                Debug.Log($"[TechTree] {node.displayName} - 이미 해금됨 (더미, 실제 강화 효과 적용은 미구현)");
                return;
            }

            if (!TechTreeManager.Instance.ArePrerequisitesMet(node.nodeId))
            {
                ToastController.Instance?.Show("선행 노드를 먼저 해금하세요");
                return;
            }

            if (confirmDialog == null)
            {
                Debug.LogWarning("[TechTree] ConfirmDialog가 연결 안 돼있음 - 인스펙터에서 연결할 것.");
                return;
            }

            confirmDialog.Open(
                $"{node.displayName}을(를) {GetCostWordText(node)}(으)로 해금하시겠습니까?",
                () =>
                {
                    bool success = TechTreeManager.Instance.TryUnlock(node.nodeId);
                    if (!success)
                    {
                        ToastController.Instance?.Show("재화가 부족합니다");
                    }
                });
        }

        private void OnNodeUnlockedChanged(string nodeId) => BuildGrid();

        private void OnCloseClicked() => PopupManager.Instance.RequestClose(this);

        // [디버그 전용] QA가 반복 테스트할 수 있도록 캐릭터/스탯 노드 해금 + 재화를 전부 초기화함.
        // 정식 릴리즈 전 삭제 대상 (기획 확인 필요) - TechTreeManager.DebugResetAllPurchases 참고.
        private void OnDebugResetClicked()
        {
            if (TechTreeManager.Instance == null) return;
            TechTreeManager.Instance.DebugResetAllPurchases();
            ToastController.Instance?.Show("[테스트] 구매/해금 전체 초기화 완료");
        }

        private void OnSearchChanged(ChangeEvent<string> evt) => ApplySearchFilter(evt.newValue);

        // 검색어와 이름이 일치하는 노드는 육각형 테두리(링)로 강조하고, 나머지는 명도를 낮춤(search-dim).
        // 검색어가 비어있으면 둘 다 꺼서 원래 상태로 되돌림.
        private void ApplySearchFilter(string query)
        {
            query = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
            bool hasQuery = query.Length > 0;

            if (TechTreeManager.Instance == null) return;

            foreach (var node in TechTreeManager.Instance.GetAllNodes())
            {
                if (!_nodeElements.TryGetValue(node.nodeId, out var element)) continue;
                if (!_nodeHexVisuals.TryGetValue(node.nodeId, out var hex)) continue;

                bool isMatch = hasQuery && node.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                element.EnableInClassList("search-dim", hasQuery && !isMatch);

                bool isCharacterNode = !string.IsNullOrEmpty(node.linkedCharacterId);
                ApplyRingStyle(hex, isCharacterNode, isMatch);
            }
        }

        // 노드 사이 연결선만 그리는 전용 레이어. 코어/노드보다 먼저 world에 추가해서 항상 뒤에 깔리게 함.
        // 클릭/드래그를 가로채면 안 되니 pickingMode를 Ignore로 둠(캔버스가 드래그 이벤트를 받아야 함).
        private class ConnectorLayer : VisualElement
        {
            public readonly struct Line
            {
                public readonly Vector2 From;
                public readonly Vector2 To;
                public readonly bool Active;

                public Line(Vector2 from, Vector2 to, bool active)
                {
                    From = from;
                    To = to;
                    Active = active;
                }
            }

            public readonly List<Line> Lines = new();

            private static readonly Color ActiveColor = new(0.35f, 0.55f, 0.9f, 0.9f);
            private static readonly Color InactiveColor = new(0.4f, 0.4f, 0.46f, 0.8f);

            public ConnectorLayer()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0;
                style.top = 0;
                generateVisualContent += OnGenerateVisualContent;
            }

            public void Redraw() => MarkDirtyRepaint();

            private void OnGenerateVisualContent(MeshGenerationContext mgc)
            {
                if (Lines.Count == 0) return;

                var painter = mgc.painter2D;
                painter.lineWidth = 3f;
                painter.lineCap = LineCap.Round;

                foreach (var line in Lines)
                {
                    painter.strokeColor = line.Active ? ActiveColor : InactiveColor;
                    painter.BeginPath();
                    painter.MoveTo(line.From);
                    painter.LineTo(line.To);
                    painter.Stroke();
                }
            }
        }

        // 노드/코어를 육각형으로 그리는 전용 레이어. UI Toolkit 스타일 시트엔 다각형 클리핑(예: clip-path)이
        // 없어서 border-radius로는 원/모서리 둥근 사각형만 가능함 - 그래서 배경 채우기(칠)와 테두리(링)를
        // Painter2D로 직접 그림. 이 레이어는 부모 노드의 클릭을 가로채면 안 되므로 PickingMode.Ignore.
        private class HexagonVisual : VisualElement
        {
            public Color FillColor { get; set; } = Color.white;
            public Color RingColor { get; set; } = Color.clear;
            public float RingWidth { get; set; }

            public HexagonVisual()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0;
                style.top = 0;
                style.right = 0;
                style.bottom = 0;
                // 처음 추가된 시점엔 아직 크기가 0이라 못 그리는 경우가 있어서, 실제 크기가 잡히는
                // 순간(레이아웃 완료) 다시 그려주도록 구독함.
                RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
                generateVisualContent += OnGenerateVisualContent;
            }

            public void Redraw() => MarkDirtyRepaint();

            private void OnGenerateVisualContent(MeshGenerationContext mgc)
            {
                float w = resolvedStyle.width;
                float h = resolvedStyle.height;
                if (w <= 0f || h <= 0f) return;

                var points = BuildHexagonPoints(w, h);
                var painter = mgc.painter2D;

                painter.BeginPath();
                painter.MoveTo(points[0]);
                for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
                painter.ClosePath();

                painter.fillColor = FillColor;
                painter.Fill();

                if (RingWidth > 0f && RingColor.a > 0f)
                {
                    painter.lineWidth = RingWidth;
                    painter.lineJoin = LineJoin.Round;
                    painter.strokeColor = RingColor;
                    painter.Stroke();
                }
            }

            private static Vector2[] BuildHexagonPoints(float w, float h)
            {
                float cx = w / 2f;
                float cy = h / 2f;
                float radius = Mathf.Min(w, h) / 2f - 2f; // 테두리가 잘리지 않도록 살짝 여백
                var points = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float angle = Mathf.Deg2Rad * (-90f + i * 60f); // 위/아래로 뾰족한 육각형
                    points[i] = new Vector2(cx + radius * Mathf.Cos(angle), cy + radius * Mathf.Sin(angle));
                }
                return points;
            }
        }
    }
}
