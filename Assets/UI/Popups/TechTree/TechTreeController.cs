using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Data;

namespace TowerDefense.UI
{
    /// <summary>
    /// 노드트리(강화) 팝업 - 선행조건 단계(depth)별로 링을 그리며 방사형으로 배치함.
    /// depth 0(선행조건 없는 노드)도 첫 번째 링 위에 배치해서 캔버스 정중앙은 항상 빈 공간으로 남김.
    /// 각 노드는 자기 선행조건 노드와 선으로 이어짐.
    /// 캔버스 크기가 바뀔 때마다(팝업 최초 오픈 시점 포함, 전체화면의 70~90% 크기라 해상도에 따라
    /// 달라짐) 다시 배치하도록 GeometryChangedEvent를 구독함.
    /// 빈 공간(노드가 아닌 부분)을 드래그하면 트리 전체가 이동함 - PointerDownEvent의 target이
    /// 캔버스 자신일 때만 드래그를 시작해서 노드 버튼 클릭과 겹치지 않게 함.
    /// 검색창에 입력하면 이름이 일치하는 노드는 테두리로 강조하고, 나머지는 투명도를 낮춤
    /// (UI Toolkit 런타임엔 box-shadow/글로우가 없어서 이 조합으로 근사함).
    /// 구조만 잡아두는 단계라 실제 강화 효과 적용(전투 스탯 반영)은 안 하고, 해금 여부만 관리함.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TechTreeController : MonoBehaviour, IPopupPanel
    {
        [Tooltip("확인창 GameObject (하이어라키에서 기본 비활성화 상태)")]
        [SerializeField] private ConfirmDialogController confirmDialog;

        private const float NodeSize = 80f;
        private const float EdgeMargin = 12f; // 캔버스 가장자리와 가장 바깥쪽 노드 사이 최소 여백

        private UIDocument _document;
        private VisualElement _canvas;
        private VisualElement _world; // 캔버스 안에서 실제로 드래그(팬)되는 컨테이너 - 노드/연결선이 여기 들어감
        private ConnectorLayer _connectorLayer;
        private Button _closeButton;
        private TextField _searchField;

        private readonly Dictionary<string, VisualElement> _nodeElements = new();

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

            _closeButton.clicked += OnCloseClicked;
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

        // target이 캔버스 자신일 때만 드래그 시작 - 노드 버튼 위에서 누른 경우엔 target이 그 버튼이라
        // 여기로는 안 들어오고(버블링은 되지만 target은 그대로 버튼) 버튼 클릭이 정상적으로 우선됨.
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

            if (TechTreeManager.Instance == null)
            {
                Debug.LogWarning("[TechTree] TechTreeManager를 찾을 수 없음 - 씬에 배치했는지 확인할 것.");
                return;
            }

            TechTreeManager.Instance.OnNodeUnlocked -= OnNodeUnlockedChanged;
            TechTreeManager.Instance.OnNodeUnlocked += OnNodeUnlockedChanged;

            // 선(연결선)이 노드보다 먼저 추가돼야 노드 버튼 아래에 깔려서 그려짐.
            _connectorLayer = new ConnectorLayer();
            _world.Add(_connectorLayer);

            foreach (var node in TechTreeManager.Instance.GetAllNodes())
            {
                var element = BuildNodeElement(node);
                _world.Add(element);
                _nodeElements[node.nodeId] = element;
            }

            LayoutNodes();
            ApplySearchFilter(_searchField != null ? _searchField.value : string.Empty);
        }

        // 선행조건 depth별로 링을 그리며 방사형 배치. depth 0도 첫 번째 링(중앙이 아님) 위에 둬서
        // 캔버스 정중앙은 항상 빈 공간으로 남김.
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
            var byDepth = nodes.GroupBy(n => depthById[n.nodeId]).OrderBy(g => g.Key).ToList();
            int maxDepth = byDepth.Max(g => g.Key);

            float cx = w / 2f;
            float cy = h / 2f;
            float maxRadius = Mathf.Max(0f, Mathf.Min(w, h) / 2f - NodeSize / 2f - EdgeMargin);
            // 링 개수 = maxDepth + 1 (depth 0도 링 위에 놓기 때문). 이렇게 하면 반지름 0인 지점,
            // 즉 정중앙에는 어떤 노드도 배치되지 않음.
            float ringStep = maxRadius / (maxDepth + 1);

            var positions = new Dictionary<string, Vector2>();

            foreach (var group in byDepth)
            {
                int depth = group.Key;
                var list = group.ToList();
                int count = list.Count;

                float radius = ringStep * (depth + 1);
                // 이전 depth 링과 각도가 겹쳐서 선이 서로 포개지는 걸 줄이려고 홀수 depth는 살짝 회전시킴.
                float angleOffset = (depth % 2 == 0) ? 0f : (360f / count) / 2f;

                for (int i = 0; i < count; i++)
                {
                    float angle = -90f + angleOffset + (360f / count) * i;
                    float rad = angle * Mathf.Deg2Rad;
                    var pos = new Vector2(cx + radius * Mathf.Cos(rad), cy + radius * Mathf.Sin(rad));
                    positions[list[i].nodeId] = pos;
                }
            }

            foreach (var kvp in positions)
            {
                if (!_nodeElements.TryGetValue(kvp.Key, out var element)) continue;
                element.style.left = kvp.Value.x - NodeSize / 2f;
                element.style.top = kvp.Value.y - NodeSize / 2f;
            }

            _connectorLayer.style.width = w;
            _connectorLayer.style.height = h;
            _connectorLayer.Lines.Clear();
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

            var element = new Button { text = unlocked ? node.displayName : $"{node.displayName}\n(G {node.cost})" };
            element.AddToClassList("tech-node");
            element.AddToClassList(unlocked ? "unlocked" : (available ? "available" : "unavailable"));

            element.clicked += () => OnNodeClicked(node);

            return element;
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
                $"{node.displayName}을(를) 골드 {node.cost}(으)로 해금하시겠습니까?",
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

        private void OnSearchChanged(ChangeEvent<string> evt) => ApplySearchFilter(evt.newValue);

        // 검색어와 이름이 일치하는 노드는 강조(search-match), 나머지는 명도를 낮춤(search-dim).
        // 검색어가 비어있으면 둘 다 꺼서 원래 상태로 되돌림.
        private void ApplySearchFilter(string query)
        {
            query = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
            bool hasQuery = query.Length > 0;

            if (TechTreeManager.Instance == null) return;

            foreach (var node in TechTreeManager.Instance.GetAllNodes())
            {
                if (!_nodeElements.TryGetValue(node.nodeId, out var element)) continue;

                bool isMatch = hasQuery && node.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                element.EnableInClassList("search-match", isMatch);
                element.EnableInClassList("search-dim", hasQuery && !isMatch);
            }
        }

        // 노드 사이 연결선만 그리는 전용 레이어. 노드 버튼들보다 먼저 world에 추가해서 항상 뒤에 깔리게 함.
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
    }
}
