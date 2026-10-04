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
        [Tooltip("[현재 미사용] 노드 상세창(NodeDetailPanel)이 해금/레벨업 확인 역할을 대신함 - 기존 씬 참조가 끊기지 않게 필드만 유지")]
        [SerializeField] private ConfirmDialogController confirmDialog;

        private const float NodeSize = 80f;
        private const float CoreNodeSize = 110f; // 코어 노드는 일반 노드보다 크게
        private const float HubNodeSize = 100f;  // 조직 허브 노드(천명회 등)도 일반 노드보다 크게(.tech-node-hub와 맞춤)
        private const float EdgeMargin = 12f; // 캔버스 가장자리와 가장 바깥쪽 노드 사이 최소 여백
        private const float CoreNodeGap = 40f; // 코어 테두리와 첫 번째 링(depth 0) 노드 사이 최소 간격
        private const float NodeSpacingGap = 28f; // 같은 링에서 이웃한 노드 사이 최소 빈틈(노드가 겹쳐 안 보이는 것 방지)
        private const float MinRingStep = NodeSize + 30f; // 링과 링 사이 최소 간격
        private const float MinZoom = 0.3f;
        private const float MaxZoom = 1.6f;

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
        private NodeDetailPanel _detailPanel; // 노드를 눌렀을 때 뜨는 상세창(코드로 생성 - 씬/UXML 수정 불필요)

        private readonly Dictionary<string, VisualElement> _nodeElements = new();
        private readonly Dictionary<string, HexagonVisual> _nodeHexVisuals = new();

        private bool _isDragging;
        private float _zoom = 1f;          // 마우스 휠로 바꾸는 확대 배율
        private Vector2 _worldSize;        // 노드가 겹치지 않게 펼친 트리 전체 크기(캔버스보다 클 수 있음)
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
            _canvas.RegisterCallback<WheelEvent>(OnCanvasWheel);
            _canvas.RegisterCallback<ClickEvent>(OnCanvasClicked);

            // 팝업을 새로 열 때마다 이전에 옮겨둔 화면 위치는 초기화하고 중앙에서 다시 시작.
            _panOffset = Vector2.zero;
            _zoom = 1f;
            _isDragging = false;

            // 상세창은 팝업 루트(전체 화면 딤 컨테이너) 위에 덮어 띄움 - UIDocument는 팝업을 열 때마다 트리를
            // 새로 만들기 때문에 매번 새로 생성해도 이전 인스턴스가 남지 않음.
            _detailPanel = new NodeDetailPanel();
            (root.Q<VisualElement>("popup-root") ?? root).Add(_detailPanel);

            _world = new VisualElement { name = "techtree-world" };
            _world.style.position = Position.Absolute;
            _world.style.left = 0;
            _world.style.top = 0;
            _world.style.right = 0;
            _world.style.bottom = 0;
            // 월드(캔버스보다 커질 수 있는 컨테이너)가 빈 공간 클릭을 먹어버리면 캔버스가 드래그를 못 받음 -
            // 빈 곳은 항상 캔버스가 target이 되도록 월드 자신은 클릭을 무시함(자식 노드는 그대로 클릭됨).
            _world.pickingMode = PickingMode.Ignore;
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
                _canvas.UnregisterCallback<WheelEvent>(OnCanvasWheel);
                _canvas.UnregisterCallback<ClickEvent>(OnCanvasClicked);
            }

            if (TechTreeManager.Instance != null)
            {
                TechTreeManager.Instance.OnNodeUnlocked -= OnNodeUnlockedChanged;
            }
            if (CharacterLevelManager.Instance != null)
            {
                CharacterLevelManager.Instance.OnLevelChanged -= OnCharacterLevelChanged;
            }
            _detailPanel = null;
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
            ApplyViewTransform();
        }

        // 이동(translate, 화면 픽셀 단위)과 확대(scale, 월드 중심 기준)를 월드에 적용함 - 둘 다 렌더 트랜스폼이라
        // 레이아웃 재계산 없이 매 프레임 호출해도 가벼움.
        private void ApplyViewTransform()
        {
            _world.style.translate = new Translate(_panOffset.x, _panOffset.y, 0f);
            _world.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
        }

        // 마우스 휠 = 커서 위치를 기준으로 확대/축소(휠을 아래로 굴리면 축소). 월드 중심이 캔버스 중심에 놓여 있으므로
        // 커서 아래의 점이 그대로 커서 아래에 남도록 이동값을 같이 보정함.
        private void OnCanvasWheel(WheelEvent evt)
        {
            float newZoom = Mathf.Clamp(_zoom * (1f - evt.delta.y * 0.05f), MinZoom, MaxZoom);
            if (!Mathf.Approximately(newZoom, _zoom))
            {
                Vector2 cursor = _canvas.WorldToLocal(evt.mousePosition);
                Vector2 anchor = new Vector2(_canvas.resolvedStyle.width / 2f, _canvas.resolvedStyle.height / 2f);
                _panOffset = cursor - anchor - (cursor - anchor - _panOffset) * (newZoom / _zoom);
                _zoom = newZoom;
                ApplyViewTransform();
            }
            evt.StopPropagation();
        }

        // 빈 공간 더블클릭 = 전체 보기(트리 전체가 캔버스에 들어오도록 축소하고 중앙으로).
        private void OnCanvasClicked(ClickEvent evt)
        {
            if (evt.clickCount < 2 || evt.target != _canvas) return;

            float w = _canvas.resolvedStyle.width;
            float h = _canvas.resolvedStyle.height;
            if (_worldSize.x <= 0f || _worldSize.y <= 0f || w <= 0f || h <= 0f) return;

            _zoom = Mathf.Clamp(Mathf.Min(w / _worldSize.x, h / _worldSize.y), MinZoom, 1f);
            _panOffset = Vector2.zero;
            ApplyViewTransform();
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

            if (CharacterLevelManager.Instance != null)
            {
                CharacterLevelManager.Instance.OnLevelChanged -= OnCharacterLevelChanged;
                CharacterLevelManager.Instance.OnLevelChanged += OnCharacterLevelChanged;
            }

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

            var nodeById = nodes.ToDictionary(n => n.nodeId);
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

            // 허브 아래에서는 스탯 노드 가지를 캐릭터들 "사이사이"에 끼워 넣음(한쪽에 몰리지 않게 균등 간격으로).
            foreach (var node in nodes)
            {
                if (node.isHub && childrenByParent.TryGetValue(node.nodeId, out var hubKids))
                {
                    childrenByParent[node.nodeId] = InterleaveStatBranches(hubKids);
                }
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

            float maxRadius = Mathf.Max(0f, Mathf.Min(w, h) / 2f - NodeSize / 2f - EdgeMargin);
            // 캔버스에 딱 맞추는 기본 반지름(트리가 작을 때 예전처럼 화면을 꽉 채우는 용도).
            float coreClearance = Mathf.Min(CoreNodeSize / 2f + NodeSize / 2f + CoreNodeGap, maxRadius);
            float ringStep = maxDepth > 0 ? (maxRadius - coreClearance) / maxDepth : 0f;

            // 1) 각도 배정 - 반지름과 무관. node에 배정된 [angleStart, angleEnd] 구간의 중앙 각도에 놓고, 자식들은
            //    이 구간을 서브트리 크기 비율로 나눠 물려받아 재귀적으로 배정함.
            var angleById = new Dictionary<string, float>();
            void AssignSector(TechNodeData node, float angleStart, float angleEnd)
            {
                angleById[node.nodeId] = (angleStart + angleEnd) / 2f;

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

            // 2) depth별 반지름 - 같은 링의 이웃 노드가 겹치지 않도록(노드 크기 + 빈틈) 링을 키움. 캐릭터 노드처럼
            //    한 링에 수십 개가 몰리면 캔버스보다 훨씬 커질 수 있고, 넘치는 부분은 드래그/휠로 둘러봄.
            //    (예전엔 캔버스 안에 전부 욱여넣어서 노드가 겹쳐 안 보였음.)
            float spacing = NodeSize + NodeSpacingGap;
            var anglesByDepth = new List<float>[maxDepth + 1];
            for (int d = 0; d <= maxDepth; d++) anglesByDepth[d] = new List<float>();
            foreach (var node in nodes)
            {
                if (angleById.TryGetValue(node.nodeId, out float ang)) anglesByDepth[depthById[node.nodeId]].Add(ang);
            }

            var radiusByDepth = new float[maxDepth + 1];
            float previousRadius = 0f;
            for (int d = 0; d <= maxDepth; d++)
            {
                float required = d == 0 ? coreClearance : previousRadius + MinRingStep;
                required = Mathf.Max(required, coreClearance + ringStep * d);

                var angles = anglesByDepth[d];
                if (angles.Count >= 2)
                {
                    angles.Sort();
                    float minGap = 360f - (angles[angles.Count - 1] - angles[0]); // 마지막 → 첫 번째(한 바퀴 돌아서)
                    for (int i = 1; i < angles.Count; i++) minGap = Mathf.Min(minGap, angles[i] - angles[i - 1]);
                    minGap = Mathf.Clamp(minGap, 0.5f, 180f);
                    // 반지름 r의 원 위에서 각도 차 g인 두 점의 현 길이 = 2 r sin(g/2) ≥ spacing
                    required = Mathf.Max(required, spacing / (2f * Mathf.Sin(minGap * Mathf.Deg2Rad / 2f)));
                }

                radiusByDepth[d] = required;
                previousRadius = required;
            }

            // 3) 월드 크기 = 캔버스와 트리 전체 중 큰 쪽. 월드 중심을 캔버스 중심에 맞춰 첫 화면엔 코어가 중앙에 옴.
            float halfExtent = radiusByDepth[maxDepth] + NodeSize / 2f + EdgeMargin;
            float worldW = Mathf.Max(w, halfExtent * 2f);
            float worldH = Mathf.Max(h, halfExtent * 2f);
            _worldSize = new Vector2(worldW, worldH);

            _world.style.right = StyleKeyword.Auto;
            _world.style.bottom = StyleKeyword.Auto;
            _world.style.width = worldW;
            _world.style.height = worldH;
            _world.style.left = (w - worldW) / 2f;
            _world.style.top = (h - worldH) / 2f;
            ApplyViewTransform();

            float cx = worldW / 2f;
            float cy = worldH / 2f;
            var corePos = new Vector2(cx, cy);

            var positions = new Dictionary<string, Vector2>();
            foreach (var kvp in angleById)
            {
                float radius = radiusByDepth[depthById[kvp.Key]];
                float rad = kvp.Value * Mathf.Deg2Rad;
                positions[kvp.Key] = new Vector2(cx + radius * Mathf.Cos(rad), cy + radius * Mathf.Sin(rad));
            }

            foreach (var kvp in positions)
            {
                if (!_nodeElements.TryGetValue(kvp.Key, out var element)) continue;
                float size = nodeById[kvp.Key].isHub ? HubNodeSize : NodeSize; // 허브는 더 큼
                element.style.left = kvp.Value.x - size / 2f;
                element.style.top = kvp.Value.y - size / 2f;
            }

            _coreElement.style.left = corePos.x - CoreNodeSize / 2f;
            _coreElement.style.top = corePos.y - CoreNodeSize / 2f;

            _connectorLayer.style.width = worldW;
            _connectorLayer.style.height = worldH;
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
                    // 허브에서 뻗어나가는 선은 그 조직 색으로(스탯 노드까지 이어지는 선도 허브 색 - 어느 조직 가지인지 보이게).
                    Color tint = default;
                    if (nodeById.TryGetValue(prereqId, out var prereqNode) && prereqNode.isHub &&
                        NodeTreeLineColors.TryGet(prereqNode.hubLine, out _, out var lineColor))
                    {
                        tint = lineColor;
                    }
                    _connectorLayer.Lines.Add(new ConnectorLayer.Line(from, to, childUnlocked, tint));
                }
            }

            // 2) 코어 -> 루트(depth 0) 노드 연결선. 다른 노드의 선행조건으로 한 번도 안 쓰이는 독립 노드
            //    (예: 캐릭터 해금 노드)도 이 연결선 덕분에 항상 트리에 이어져 있음.
            foreach (var node in roots)
            {
                if (!positions.TryGetValue(node.nodeId, out var to)) continue;
                bool unlocked = TechTreeManager.Instance.IsUnlocked(node.nodeId);
                Color tint = default;
                if (node.isHub && NodeTreeLineColors.TryGet(node.hubLine, out _, out var lineColor)) tint = lineColor;
                _connectorLayer.Lines.Add(new ConnectorLayer.Line(corePos, to, unlocked, tint));
            }

            _connectorLayer.Redraw();
        }

        // 허브의 자식 목록에서 캐릭터 노드들 사이에 스탯 노드 가지를 균등하게 끼워 넣은 새 목록을 만듦.
        // 스탯 가지가 k개, 캐릭터가 n명이면 i번째 가지를 캐릭터 n*(i+1)/(k+1)번째 뒤에 놓음.
        private static List<TechNodeData> InterleaveStatBranches(List<TechNodeData> kids)
        {
            var characters = new List<TechNodeData>();
            var stats = new List<TechNodeData>();
            foreach (var kid in kids)
            {
                if (!string.IsNullOrEmpty(kid.linkedCharacterId)) characters.Add(kid);
                else stats.Add(kid);
            }
            if (stats.Count == 0 || characters.Count == 0) return kids;

            var result = new List<TechNodeData>(kids.Count);
            int nextStat = 0;
            for (int i = 0; i < characters.Count; i++)
            {
                result.Add(characters[i]);
                while (nextStat < stats.Count && i + 1 >= characters.Count * (nextStat + 1) / (stats.Count + 1))
                {
                    result.Add(stats[nextStat]);
                    nextStat++;
                }
            }
            while (nextStat < stats.Count) result.Add(stats[nextStat++]);
            return result;
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
            // 조직 허브 - 해금하는 노드가 아니라 폴더 같은 묶음. 조직색으로 채우고 이름만 표시함(클릭해도 아무 일 없음).
            if (node.isHub) return BuildHubElement(node);

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
            ApplyRingStyle(hex, node, isSearchMatch: false);
            wrapper.Add(hex);
            _nodeHexVisuals[node.nodeId] = hex;

            string labelText = node.displayName;
            if (!unlocked) labelText = $"{node.displayName}\n({GetCostSymbolText(node)})";
            else if (isCharacterNode && CharacterLevelManager.Instance != null)
            {
                labelText = $"{node.displayName}\nLv {CharacterLevelManager.Instance.GetLevel(node.linkedCharacterId)}"; // 해금된 캐릭터 노드엔 현재 레벨 표시
            }
            var label = new Label(labelText);
            label.AddToClassList("tech-node-label");
            wrapper.Add(label);

            wrapper.RegisterCallback<ClickEvent>(_ => OnNodeClicked(node));

            return wrapper;
        }

        private VisualElement BuildHubElement(TechNodeData hubNode)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("tech-node");
            wrapper.AddToClassList("tech-node-hub");

            NodeTreeLineColors.TryGet(hubNode.hubLine, out var fill, out var ring);
            var hex = new HexagonVisual { FillColor = fill, RingColor = ring, RingWidth = 4f };
            wrapper.Add(hex);
            _nodeHexVisuals[hubNode.nodeId] = hex;

            var label = new Label(hubNode.displayName);
            label.AddToClassList("tech-node-label");
            label.AddToClassList("tech-node-hub-label");
            wrapper.Add(label);

            return wrapper;
        }

        private static Color GetFillColor(bool unlocked, bool available)
        {
            if (unlocked) return UnlockedFillColor;
            return available ? AvailableFillColor : UnavailableFillColor;
        }

        // 기본 테두리: 허브 = 조직색 두껍게 / 캐릭터 노드 = 소속 조직색 / 스탯 노드 = 무색(테두리 없음).
        // 검색 일치(노란 링)가 이 기본 테두리보다 우선순위가 높음(더 눈에 띄어야 하니까).
        private static void ApplyRingStyle(HexagonVisual hex, TechNodeData node, bool isSearchMatch)
        {
            if (isSearchMatch)
            {
                hex.RingColor = SearchMatchRingColor;
                hex.RingWidth = 4f;
            }
            else if (node.isHub && NodeTreeLineColors.TryGet(node.hubLine, out _, out var hubRing))
            {
                hex.RingColor = hubRing;
                hex.RingWidth = 4f;
            }
            else if (!string.IsNullOrEmpty(node.linkedCharacterId))
            {
                // 무소속 3명도 노드 트리 라인(허브) 색을 따름 - 연결된 허브와 같은 색이어야 한눈에 묶여 보임.
                hex.RingColor = NodeTreeLineColors.TryGet(node.hubLine, out _, out var ringColor) ? ringColor : CharacterNodeRingColor;
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

        // 노드를 누르면 상세창을 띄움 - 해금 여부(해금/취소), 해금된 캐릭터면 레벨업 여부와 변하는 수치를 거기서 결정함.
        private void OnNodeClicked(TechNodeData node)
        {
            if (node.isHub) return; // 허브는 폴더 같은 묶음 - 해금/상세 없음
            _detailPanel?.ShowNode(node);
        }

        private void OnNodeUnlockedChanged(string nodeId)
        {
            BuildGrid();
            _detailPanel?.RefreshIfShowing(); // 방금 해금한 캐릭터 노드면 곧바로 레벨 화면으로 바뀜
        }

        private void OnCharacterLevelChanged(string characterId, int newLevel)
        {
            BuildGrid(); // 노드 라벨의 레벨 표시 갱신
            _detailPanel?.RefreshIfShowing();
        }

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

                ApplyRingStyle(hex, node, isMatch);
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
                public readonly Color Tint; // 조직색 연결선(알파 0이면 기본 색 사용)

                public Line(Vector2 from, Vector2 to, bool active, Color tint = default)
                {
                    From = from;
                    To = to;
                    Active = active;
                    Tint = tint;
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
                    if (line.Tint.a > 0f)
                    {
                        // 조직색 선 - 해금된 쪽은 선명하게, 아직이면 반투명하게
                        var tint = line.Tint;
                        tint.a = line.Active ? 0.95f : 0.45f;
                        painter.strokeColor = tint;
                    }
                    else
                    {
                        painter.strokeColor = line.Active ? ActiveColor : InactiveColor;
                    }
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
