using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using TowerDefense.Map;
using TowerDefense.Monsters;

namespace TowerDefense.EditorTools
{
    /// <summary>
    /// 태극(太極) 문양 인게임 대전 보드의 "몬스터 경로 배정" 전용 에디터 툴.
    ///
    /// [2026-09-29 정리] 이전 버전엔 보드/경로 모양 자체를 하드코딩 좌표로 찍어주는
    /// SetupTaegeukBoard(), 고정 타원/물결 공식으로 흰 타일을 그려주는
    /// PaintLargestEllipseInWhiteTileBounds()/PaintMirrorSymmetricWaveAcrossEllipse(), 그
    /// 공식을 그대로 웨이포인트 순서로 쓰던 구버전 경로 배정 3종(Wave/Ring Arcs/Ring And
    /// Wave 프리셋) 등, 지금 씬 모양을 만드는 과정에서 썼던 여러 실험적 스캐폴드가 함께
    /// 들어있었음. 이 실험들은 전부 "공식으로 이상적인 도형을 다시 계산"하는 방식이었는데,
    /// 실측해보니 실제로 칠해진 모양과 안 맞음이 확정되어(당시 LogWhiteCellDiagnostics
    /// 진단으로 확인) 지금은 전부 안 씀 - 사용자 확인을 거쳐 이 파일에서 제거함(이미
    /// 생성돼 씬에 남아있는 보드/타일맵 오브젝트 자체는 그대로 유지되고, 여기서 지운 건
    /// "그 모양을 다시 찍어내는 도구 코드"일 뿐임).
    ///
    /// 지금 남은 건 순수하게 "몬스터 경로 배정" 파이프라인뿐임:
    ///   1) 디자이너가 Tile Palette로 Tile_White를 몬스터가 다닐 길 모양대로 손으로 칠함
    ///      (보드 모양이 바뀌어도 이 손칠하기 과정 자체는 그대로 유지됨 - 특정 좌표/공식에
    ///      의존하지 않음).
    ///   2) AssignMonsterRoutesFromPaintedShape()가 씬 전체를 스캔해서 실제로 칠해진
    ///      Tile_White 칸만 모으고(TraceConnectedPath로 8방향 인접을 그대로 따라감 - 도형이
    ///      원이든 사각형이든 별 모양이든 상관없이 항상 "실제로 칠해진 그 모양"만 따라감),
    ///      그 경로를 MonsterSpawner.waypoints로 배선함.
    ///
    /// [향후 맵 교체 시 주의할 점] AssignMonsterRoutesFromPaintedShape()는 두 진영이 만나는
    /// 접합점(허브)을 찾을 때 대략적인 좌표 (-16,-2)/(11,1)을 "출발 힌트"로 써서 그 근처의
    /// 실제로 칠해진 칸에 스냅함(ClosestInSet) - 지금 더미 태극 보드의 대략적인 위치일
    /// 뿐, 반드시 있어야 하는 하드코딩 값은 아님. 나중에 진짜 맵이 들어와서 두 진영
    /// 접합점 위치가 많이 달라지면(예: 허브가 2개가 아니라 1개거나, 위치가 완전히 다른
    /// 좌표대) 이 두 힌트 좌표만 그 맵에 맞게 바꿔주면 됨 - 나머지 추적/배정 로직
    /// (TraceConnectedPath, ClosestInSet, BuildAndWireMonsterRoutes 이하)은 좌표를
    /// 하드코딩하지 않고 전부 "씬에서 실측한 칸"만 갖고 계산하므로 도형 자체가 바뀌어도
    /// 그대로 재사용 가능함.
    /// </summary>
    public static class TaegeukBoardScaffoldTool
    {
        private const string InGameScenePath = "Assets/Scenes/InGame.unity";
        private const string WhiteTilePath = "Assets/Prefabs/_Placeholder/Tile_White.asset";

        private const string TaegeukBoardRootName = "TaegeukBoard";
        private const string P1MonsterPathName = "P1MonsterPath";

        // ================================================================================
        // [사용자 요청] "이제 그냥 몬스터 길 위치를 하나만 남기자. 저 흰색 있는 것만 남기고
        // 없애자. 보드는 절대 건들지 말고." - TaegeukBoard 밑에 몬스터 경로용 타일맵이
        // MonsterPath(구버전 잔재, 흰색 아닌 다른 타일 사용, 미사용 - guid 7cf2b26c...),
        // P1MonsterPath(빈 타일맵, 미사용), P2MonsterPath(현재 그려진 흰색 타원링+S자 물결,
        // 사용 중 - guid 09ac756f...가 실제로 들어있는 걸 확인함) 이렇게 셋이나 남아있어서,
        // 흰색이 실제로 칠해진 P2MonsterPath 하나만 남기고 나머지 둘은 완전히 삭제함
        // (SetActive(false)가 아니라 DestroyImmediate - "없애자"라고 명시적으로 요청함).
        // TaegeukBoard의 "직계 자식" 중 이름이 정확히 일치하는 것만 지우도록 스코프를
        // 한정해서, P1Board/P2Board는 이름조차 건드리지 않음.
        [MenuItem("Tools/Dev Scaffold/Remove Duplicate Monster Path Tilemaps")]
        public static void RemoveDuplicateMonsterPathTilemaps()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[TaegeukBoardScaffoldTool] Play 모드에서는 실행할 수 없음 - Play를 정지한 뒤 다시 실행할 것.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[TaegeukBoardScaffoldTool] 사용자가 저장을 취소해서 작업을 중단함.");
                return;
            }

            var inGameScene = SceneManager.GetActiveScene().path == InGameScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(InGameScenePath, OpenSceneMode.Single);

            var rootGo = FindGameObjectInScene(inGameScene, TaegeukBoardRootName);
            if (rootGo == null)
            {
                Debug.LogError($"[TaegeukBoardScaffoldTool] '{TaegeukBoardRootName}'를 씬에서 못 찾음.");
                return;
            }

            int removed = 0;
            foreach (var childName in new[] { "MonsterPath", P1MonsterPathName })
            {
                var child = rootGo.transform.Find(childName);
                if (child != null)
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                    removed++;
                    Debug.Log($"[TaegeukBoardScaffoldTool] '{childName}' 삭제함.");
                }
            }

            if (removed == 0)
            {
                Debug.Log("[TaegeukBoardScaffoldTool] 지울 대상이 이미 없음(MonsterPath/P1MonsterPath 둘 다 이미 삭제돼있음).");
                return;
            }

            EditorSceneManager.MarkSceneDirty(inGameScene);
            EditorSceneManager.SaveScene(inGameScene);
            Debug.Log($"[TaegeukBoardScaffoldTool] 몬스터 경로 오브젝트 {removed}개 삭제 완료 - " +
                $"'P2MonsterPath'(흰색 경로)만 남음. P1Board/P2Board는 건드리지 않음.");
        }

        // ================================================================================
        // [배선 개요] MonsterSpawner.cs/BaseHealth.cs는 Tilemap 색이 아니라 Transform[]
        // waypoints 배열과 이벤트 구독만으로 동작함(코드 수정 불필요, 이미 실무 컨벤션대로
        // 구현돼 있었음 - Tilemap.GetTile을 매 프레임 조회하지 않고 웨이포인트 좌표를 한
        // 번만 구워서 씀). 아래 파이프라인은 그 배열을 채울 실제 Transform 체인 +
        // MonsterSpawner/BaseHealth 오브젝트를 씬에 자동으로 만들어 배선하는 역할만 함.
        //
        // [진영 배정 규칙] 남겨둔 흰색 경로가 어떤 부분 경로를 고르든 항상 만나는 두
        // 접합점(허브) 중 왼쪽을 P1 스폰/진영, 오른쪽을 P2 스폰/진영으로 고정함(다른 근거는
        // 없고 순전히 임의 배정 - 반대로 하고 싶으면 AssignMonsterRoutesFromPaintedShape() 안의
        // p1/p2 조립 순서만 바꾸면 됨). 본진 위치도 기획 미확정이라, 각 경로의 도착 지점을
        // 임시 본진 위치로 잡음 - Scene 뷰에서 Base 오브젝트를 드래그해서 언제든 옮길 수 있음.
        //
        // [웨이포인트 밀도] "꺾이는 지점만"이라고 답해서 방향이 바뀌는 칸만 남김
        // (ReduceToCorners) - 타원/물결처럼 곡률이 있는 도형은 완전한 직선 구간이 거의 없어서
        // 원본 칸 수 대비 약 40~55%로만 줄어드는 게 정상(더 줄이면 곡선에서 실제 타일 경로와
        // 눈에 띄게 벗어남).
        private const string P1RouteName = "P1MonsterRoute";
        private const string P2RouteName = "P2MonsterRoute";
        private const string P1WaypointsParentName = "P1RouteWaypoints";
        private const string P2WaypointsParentName = "P2RouteWaypoints";
        private const string P1BaseName = "P1Base";
        private const string P2BaseName = "P2Base";

        // [결론 - 2026-09-29] 공식으로 이상적인 도형을 다시 계산하는 방식(고정 반지름 타원/물결
        // 공식)은 실제로 칠해진(비원형/비대칭) 모양과는 애초에 맞을 수가 없음이 실측으로
        // 확정됐음. 그래서 공식으로 재계산하는 대신, 실제로 칠해진 흰 칸을 8방향 인접
        // 그래프로 보고 그대로 따라가서 순서를 만드는 방식으로 만듦 - 어떤 모양이 칠해져
        // 있든 항상 정확히 그 위만 지나가므로, 나중에 맵 모양이 통째로 바뀌어도 그대로
        // 재사용 가능함.
        //
        // TraceConnectedPath: 시작 칸에서 출발해 "직전 이동 방향과 가장 비슷한 방향"의 이웃을
        // 우선 선택하며 안 밟은 칸을 계속 따라감(지그재그 방지). 접합점(3방향 이상 갈림)에서도
        // 방향 유지 우선순위 덕에 대체로 자연스럽게 한쪽을 골라 계속 진행함 - 폭 1칸짜리 단일
        // 경로에서는 실전에 충분히 안정적이지만, 그래프 분기를 수학적으로 완벽히 처리하는 건
        // 아니므로 반드시 아래 읽기 전용 진단으로 먼저 결과를 확인할 것(바로 게임 스포너에
        // 배선하지 않음).
        // preVisited: 이미 밟은 걸로 치고 시작할 칸들(다른 구간이 이미 쓴 칸을 피해서 남은
        // 갈래로 가게 만들 때 씀). stopAt: 이 칸에 도착하면 그 칸까지 포함해서 바로 끝냄(그
        // 너머로 계속 진행하지 않음). 둘 다 생략하면 원래 동작(끝까지 자유 추적)과 같음.
        private static System.Collections.Generic.List<Vector2Int> TraceConnectedPath(
            System.Collections.Generic.HashSet<Vector2Int> pathCells,
            Vector2Int start,
            System.Collections.Generic.HashSet<Vector2Int> preVisited = null,
            Vector2Int? stopAt = null)
        {
            var dirs8 = new System.Collections.Generic.List<Vector2Int>
            {
                new Vector2Int(1,0), new Vector2Int(1,1), new Vector2Int(0,1), new Vector2Int(-1,1),
                new Vector2Int(-1,0), new Vector2Int(-1,-1), new Vector2Int(0,-1), new Vector2Int(1,-1)
            };

            var visited = preVisited != null
                ? new System.Collections.Generic.HashSet<Vector2Int>(preVisited)
                : new System.Collections.Generic.HashSet<Vector2Int>();
            visited.Add(start);
            var path = new System.Collections.Generic.List<Vector2Int> { start };
            var current = start;
            var lastDir = Vector2Int.zero;

            while (true)
            {
                // [수정] stopAt 칸 자체가 이미 preVisited에 들어있으면(다른 구간의 시작점이었던
                // 경우) "현재 칸 == stopAt"은 절대 만족 못 함 - stopAt으로 실제 "이동"할 수가
                // 없기 때문(이동 후보에서 visited라 걸러짐). 그래서 "정확히 도착"이 아니라
                // "8방향 이웃 중에 stopAt이 있으면(=도착 직전이면) 거기서 멈춤"으로 바꿈 - 이러면
                // stopAt이 이미 다른 구간이 차지한 칸이어도 그 근처에서 정확히 멈추고, 남은 칸을
                // 엉뚱하게 더 밟아서 다음 구간(예: arcB) 몫을 침범하는 걸 막음.
                if (stopAt.HasValue)
                {
                    var toStop = current - stopAt.Value;
                    if (Mathf.Abs(toStop.x) <= 1 && Mathf.Abs(toStop.y) <= 1) break;
                }

                Vector2Int? best = null;
                int bestScore = int.MinValue;
                foreach (var d in dirs8)
                {
                    var next = current + d;
                    if (!pathCells.Contains(next) || visited.Contains(next)) continue;
                    int score = (lastDir == Vector2Int.zero) ? 0 : (d.x * lastDir.x + d.y * lastDir.y);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = next;
                    }
                }
                if (best == null) break;
                lastDir = best.Value - current;
                current = best.Value;
                visited.Add(current);
                path.Add(current);
            }
            return path;
        }

        private static Vector2Int ClosestInSet(System.Collections.Generic.HashSet<Vector2Int> set, Vector2Int target)
        {
            Vector2Int best = default;
            int bestDist = int.MaxValue;
            foreach (var c in set)
            {
                int d = (c.x - target.x) * (c.x - target.x) + (c.y - target.y) * (c.y - target.y);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        // [실제 배선용 - 씬을 바꿈] 왼쪽 허브(-16,-2) 근처에서 시작해 자연스럽게 잡히는
        // 방향으로 가면 arcA(왼쪽→오른쪽 허브) → 물결(오른쪽→왼쪽 허브 근처) 순으로 이어지고,
        // arcA/물결을 이미 밟은 걸로 치면 오른쪽 허브의 남은 세 번째 갈래가 arcB(반대편 반원)로
        // 이어짐 - 공식이 아니라 실제로 칠해진 칸 그대로에서 "반원+물결" 구조를 뽑아냄. 이
        // 메서드는 그 실측 결과를 그대로 P1/P2 경로로 조립해서 BuildAndWireMonsterRoutes에
        // 넘김(고정 반지름 공식은 전혀 안 씀).
        [MenuItem("Tools/Dev Scaffold/Assign Monster Routes - From Painted Shape (Real Cells)")]
        public static void AssignMonsterRoutesFromPaintedShape()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[TaegeukBoardScaffoldTool] Play 모드에서는 실행할 수 없음 - Play를 정지한 뒤 다시 실행할 것.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[TaegeukBoardScaffoldTool] 사용자가 저장을 취소해서 작업을 중단함.");
                return;
            }

            var whiteTile = AssetDatabase.LoadAssetAtPath<TileBase>(WhiteTilePath);
            if (whiteTile == null)
            {
                Debug.LogError($"[TaegeukBoardScaffoldTool] Tile_White를 못 찾음: {WhiteTilePath}");
                return;
            }

            var inGameScene = SceneManager.GetActiveScene().path == InGameScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(InGameScenePath, OpenSceneMode.Single);

            var rootGo = FindGameObjectInScene(inGameScene, TaegeukBoardRootName);
            if (rootGo == null)
            {
                Debug.LogError($"[TaegeukBoardScaffoldTool] '{TaegeukBoardRootName}'를 씬에서 못 찾음.");
                return;
            }

            Tilemap targetTilemap = null;
            var whiteCells = new System.Collections.Generic.List<Vector2Int>();
            foreach (var root in inGameScene.GetRootGameObjects())
            {
                foreach (var tilemap in root.GetComponentsInChildren<Tilemap>(true))
                {
                    foreach (var pos in tilemap.cellBounds.allPositionsWithin)
                    {
                        if (tilemap.GetTile(pos) == whiteTile)
                        {
                            whiteCells.Add(new Vector2Int(pos.x, pos.y));
                            targetTilemap = tilemap;
                        }
                    }
                }
            }

            if (whiteCells.Count == 0)
            {
                Debug.LogError("[TaegeukBoardScaffoldTool] 씬 어디에도 Tile_White가 칠해진 칸이 없음.");
                return;
            }

            var pathCells = new System.Collections.Generic.HashSet<Vector2Int>(whiteCells);
            // [향후 맵 교체 시] 아래 두 좌표는 지금 더미 태극 보드에서 두 진영 경로가 만나는
            // 접합점의 대략적인 위치일 뿐인 "출발 힌트"임 - ClosestInSet이 실제로 칠해진 칸
            // 중 가장 가까운 칸에 스냅하므로 정확할 필요는 없지만, 새 맵에서 접합점 위치가
            // 크게 달라지면(허브가 아예 다른 좌표대에 있음) 이 두 값만 그 맵에 맞게 바꿔줄 것.
            var hubLeft = ClosestInSet(pathCells, new Vector2Int(-16, -2));
            var hubRight = ClosestInSet(pathCells, new Vector2Int(11, 1));

            var arcA = TraceConnectedPath(pathCells, hubLeft, stopAt: hubRight);
            var arcAVisited = new System.Collections.Generic.HashSet<Vector2Int>(arcA);
            var wave = TraceConnectedPath(pathCells, hubRight, preVisited: arcAVisited, stopAt: hubLeft);
            var usedForArcB = new System.Collections.Generic.HashSet<Vector2Int>(arcA);
            usedForArcB.UnionWith(wave);

            // [수정] hubRight 자기 자신에서 다시 시작하면, 그 8방향 이웃이 이미 arcA(끝) + 물결(시작)
            // 로 다 차버려서 세 번째 갈래(arcB)로 갈 칸이 없음 - 접합점이 "허브 한 칸"이 아니라
            // "허브 근처 2~4칸짜리 겹침 구간"이라 진짜 갈림길이 hubRight 그 자체가 아닌 걸로
            // 보임. 그래서 hubRight에서 다시 시작하는 대신, 아직 아무 구간도 안 쓴 "나머지 칸" 중에서
            // hubRight에 제일 가까운 칸을 arcB의 실제 시작점으로 잡음.
            // [추가 수정] 단순히 "나머지 칸 중 hubRight에 유클리드 거리로 제일 가까운 칸"을 골랐더니
            // 그게 진짜 arcB 몸통이 아니라 외따로 떨어진 낡은 잔재 1칸(이웃이 전부 이미 arcA/물결에
            // 쓰인 칸)이 뽑혀서 1칸만에 막힘. 그래서 "나머지 칸"을 8방향 인접 기준으로 연결
            // 요소(component)별로 나눠서 제일 큰 덩어리(=진짜 arcB 몸통)를 고르고, 그 안에서만
            // hubRight에 제일 가까운 칸을 시작점으로 잡음.
            var remaining = new System.Collections.Generic.HashSet<Vector2Int>(pathCells);
            remaining.ExceptWith(usedForArcB);
            System.Collections.Generic.List<Vector2Int> arcB;
            if (remaining.Count == 0)
            {
                arcB = new System.Collections.Generic.List<Vector2Int>();
            }
            else
            {
                var dirs8ForComponents = new System.Collections.Generic.List<Vector2Int>
                {
                    new Vector2Int(1,0), new Vector2Int(1,1), new Vector2Int(0,1), new Vector2Int(-1,1),
                    new Vector2Int(-1,0), new Vector2Int(-1,-1), new Vector2Int(0,-1), new Vector2Int(1,-1)
                };
                var seen = new System.Collections.Generic.HashSet<Vector2Int>();
                var largestComponent = new System.Collections.Generic.List<Vector2Int>();
                foreach (var seed in remaining)
                {
                    if (seen.Contains(seed)) continue;
                    var component = new System.Collections.Generic.List<Vector2Int>();
                    var queue = new System.Collections.Generic.Queue<Vector2Int>();
                    queue.Enqueue(seed);
                    seen.Add(seed);
                    while (queue.Count > 0)
                    {
                        var cur = queue.Dequeue();
                        component.Add(cur);
                        foreach (var d in dirs8ForComponents)
                        {
                            var next = cur + d;
                            if (remaining.Contains(next) && !seen.Contains(next))
                            {
                                seen.Add(next);
                                queue.Enqueue(next);
                            }
                        }
                    }
                    if (component.Count > largestComponent.Count) largestComponent = component;
                }

                Debug.Log($"[TaegeukBoardScaffoldTool][실측 경로] 나머지 {remaining.Count}칸을 연결 요소로 나눠보니 " +
                    $"가장 큰 덩어리가 {largestComponent.Count}칸 - 이걸 arcB 몸통으로 씀.");

                var largestSet = new System.Collections.Generic.HashSet<Vector2Int>(largestComponent);
                var arcBStart = ClosestInSet(largestSet, hubRight);
                arcB = TraceConnectedPath(pathCells, arcBStart, preVisited: usedForArcB, stopAt: hubLeft);
            }

            Debug.Log($"[TaegeukBoardScaffoldTool][실측 경로] hubLeft={hubLeft}, hubRight={hubRight} - " +
                $"arcA(hubLeft→hubRight) {arcA.Count}칸, 물결(hubRight→hubLeft) {wave.Count}칸, " +
                $"arcB(hubRight→hubLeft, 반대편) {arcB.Count}칸.");

            if (arcA.Count < 2 || wave.Count < 2 || arcB.Count < 2)
            {
                Debug.LogError("[TaegeukBoardScaffoldTool][실측 경로] 세 구간 중 하나가 제대로 안 이어짐(2칸 미만) - " +
                    "경로 배정을 중단함(씬 변경 없음). 아래 진단 메뉴로 원인을 다시 확인할 것.");
                return;
            }

            int waveCenterIdx = wave.Count / 2;
            var p1 = new System.Collections.Generic.List<Vector2Int>(arcA);
            for (int i = 1; i <= waveCenterIdx; i++) p1.Add(wave[i]);

            var waveFromLeft = new System.Collections.Generic.List<Vector2Int>(wave);
            waveFromLeft.Reverse();
            int waveCenterIdx2 = waveFromLeft.Count / 2;
            var p2 = new System.Collections.Generic.List<Vector2Int>(arcB);
            for (int i = 1; i <= waveCenterIdx2; i++) p2.Add(waveFromLeft[i]);

            BuildAndWireMonsterRoutes("실측 경로(태극 실제 칠해진 칸 기준, 공식 미사용)", p1, p2, targetTilemap, rootGo, inGameScene);
        }

        // [읽기 전용 진단] 실제 칠해진 흰 칸을 TraceConnectedPath로 따라가봤을 때 몇 칸이
        // 나오는지, 어디서 끝나는지만 로그로 확인함 - 씬을 전혀 안 바꿈. 새 맵으로 바뀐 뒤
        // AssignMonsterRoutesFromPaintedShape()의 결과가 이상하면, 이 메뉴로 먼저 원인을
        // 좁혀볼 것. startNear에 가장 가까운 실제 흰 칸을 시작점으로 씀.
        [MenuItem("Tools/Dev Scaffold/Debug - Trace Real Path From (-16,-2) (Read-Only)")]
        public static void LogTraceFromLeftHub()
        {
            LogTraceFromPoint(new Vector2Int(-16, -2));
        }

        [MenuItem("Tools/Dev Scaffold/Debug - Trace Real Path From (11,1) (Read-Only)")]
        public static void LogTraceFromRightHub()
        {
            LogTraceFromPoint(new Vector2Int(11, 1));
        }

        private static void LogTraceFromPoint(Vector2Int startNear)
        {
            var whiteTile = AssetDatabase.LoadAssetAtPath<TileBase>(WhiteTilePath);
            if (whiteTile == null)
            {
                Debug.LogError($"[TaegeukBoardScaffoldTool][진단] Tile_White를 못 찾음: {WhiteTilePath}");
                return;
            }
            var inGameScene = SceneManager.GetActiveScene().path == InGameScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(InGameScenePath, OpenSceneMode.Single);

            var whiteCells = new System.Collections.Generic.List<Vector2Int>();
            foreach (var root in inGameScene.GetRootGameObjects())
                foreach (var tilemap in root.GetComponentsInChildren<Tilemap>(true))
                    foreach (var pos in tilemap.cellBounds.allPositionsWithin)
                        if (tilemap.GetTile(pos) == whiteTile)
                            whiteCells.Add(new Vector2Int(pos.x, pos.y));

            if (whiteCells.Count == 0)
            {
                Debug.LogError("[TaegeukBoardScaffoldTool][진단] Tile_White 칸이 없음.");
                return;
            }

            // 사각형 테두리로 보이는 칸을 걸러내지 않고 칠해진 흰 칸 전부를 후보로 씀 - 실제
            // 링이 그 가장자리 좌표를 지나가는 부분까지 같이 잘려서 추적이 중간에 끊기는 걸
            // 피하기 위함(진짜 테두리가 섞여 있어도 추적 중 그쪽으로 잠깐 새는 정도라 전체
            // 경로 파악에는 문제 없음).
            var pathCells = new System.Collections.Generic.HashSet<Vector2Int>(whiteCells);

            Debug.Log($"[TaegeukBoardScaffoldTool][진단] 전체 흰 칸 {whiteCells.Count}개(테두리 미제외) 전부를 경로 후보로 씀.");

            Vector2Int start = pathCells.First();
            int bestDist = int.MaxValue;
            foreach (var c in pathCells)
            {
                int d = (c.x - startNear.x) * (c.x - startNear.x) + (c.y - startNear.y) * (c.y - startNear.y);
                if (d < bestDist) { bestDist = d; start = c; }
            }

            var traced = TraceConnectedPath(pathCells, start);
            var last = traced[traced.Count - 1];
            Debug.Log($"[TaegeukBoardScaffoldTool][진단] {startNear} 근처 실제 칸 {start}에서 시작해 추적한 경로: {traced.Count}칸 " +
                $"(전체 경로 후보 {pathCells.Count}칸 중 {traced.Count}칸을 밟음). 끝난 지점: {last}. " +
                $"전체 경로: [{string.Join(" ", traced.ConvertAll(c => $"({c.x},{c.y})"))}]");

            if (traced.Count < pathCells.Count)
            {
                Debug.LogWarning($"[TaegeukBoardScaffoldTool][진단] 추적이 전체 후보({pathCells.Count}칸)보다 적게({traced.Count}칸) 끝남 - " +
                    "접합점에서 반대쪽으로 갈라져 나머지 칸을 못 밟았을 수 있음(정상일 수도 있음 - 반원 하나만 추적한 거라면). " +
                    "끝난 지점이 기대한 반대쪽 허브나 중앙 근처인지 위 좌표로 직접 확인할 것.");
            }
        }

        // 방향이 바뀌는 칸(꺾이는 지점)만 남기고 나머지는 압축함 - 시작/끝 칸은 항상 남김.
        private static System.Collections.Generic.List<Vector2Int> ReduceToCorners(
            System.Collections.Generic.List<Vector2Int> path)
        {
            if (path.Count < 3) return new System.Collections.Generic.List<Vector2Int>(path);
            var result = new System.Collections.Generic.List<Vector2Int> { path[0] };
            for (int i = 1; i < path.Count - 1; i++)
            {
                var d1 = path[i] - path[i - 1];
                var d2 = path[i + 1] - path[i];
                if (d1 != d2) result.Add(path[i]);
            }
            result.Add(path[path.Count - 1]);
            return result;
        }

        // 웨이포인트 압축 → 웨이포인트 Transform 체인 + MonsterSpawner 오브젝트 2세트(P1/P2) →
        // 각 경로의 도착 지점에 BaseHealth 오브젝트를 만들어 그 스포너와 배선. 재실행 시
        // 이전에 이 메서드로 만든 오브젝트를 먼저 지우고 새로 만듦(멱등).
        private static void BuildAndWireMonsterRoutes(
            string presetLabel,
            System.Collections.Generic.List<Vector2Int> p1CellsRaw,
            System.Collections.Generic.List<Vector2Int> p2CellsRaw,
            Tilemap referenceTilemap,
            GameObject taegeukRoot,
            Scene inGameScene)
        {
            var p1Cells = ReduceToCorners(p1CellsRaw);
            var p2Cells = ReduceToCorners(p2CellsRaw);

            foreach (var name in new[]
                     {
                         P1RouteName, P2RouteName, P1WaypointsParentName, P2WaypointsParentName, P1BaseName, P2BaseName
                     })
            {
                var existing = taegeukRoot.transform.Find(name);
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var p1Spawner = BuildRouteAndSpawner(taegeukRoot, P1RouteName, P1WaypointsParentName, p1Cells, referenceTilemap);
            var p2Spawner = BuildRouteAndSpawner(taegeukRoot, P2RouteName, P2WaypointsParentName, p2Cells, referenceTilemap);

            BuildBase(taegeukRoot, P1BaseName, p1Spawner, referenceTilemap, p1Cells[p1Cells.Count - 1]);
            BuildBase(taegeukRoot, P2BaseName, p2Spawner, referenceTilemap, p2Cells[p2Cells.Count - 1]);

            EditorSceneManager.MarkSceneDirty(inGameScene);
            EditorSceneManager.SaveScene(inGameScene);

            Debug.Log($"[TaegeukBoardScaffoldTool] [{presetLabel}] " +
                $"P1 경로 {p1CellsRaw.Count}칸 → 꺾이는 지점 {p1Cells.Count}개(스폰 {p1Cells[0]} → 도착/상대 본진 {p1Cells[p1Cells.Count - 1]}), " +
                $"P2 경로 {p2CellsRaw.Count}칸 → 꺾이는 지점 {p2Cells.Count}개(스폰 {p2Cells[0]} → 도착/상대 본진 {p2Cells[p2Cells.Count - 1]}). " +
                $"'{P1RouteName}'/'{P2RouteName}'에 MonsterSpawner, '{P1BaseName}'/'{P2BaseName}'에 BaseHealth를 새로 만들어 배선함 " +
                $"(웨이브 목록(waves)은 기획 미정이라 비워둠 - 인스펙터에서 채울 것, 몬스터 프리팹도 MonsterSpawner.SpawnMonster에 넘길 MonsterDataSO를 아직 안 만들어서 비워둠).");
        }

        private static MonsterSpawner BuildRouteAndSpawner(
            GameObject parent, string routeName, string waypointsParentName,
            System.Collections.Generic.List<Vector2Int> cells, Tilemap referenceTilemap)
        {
            var routeGo = new GameObject(routeName);
            Undo.RegisterCreatedObjectUndo(routeGo, $"Create {routeName}");
            Undo.SetTransformParent(routeGo.transform, parent.transform, $"Parent {routeName}");
            routeGo.transform.localPosition = Vector3.zero;

            var waypointsParent = new GameObject(waypointsParentName);
            Undo.RegisterCreatedObjectUndo(waypointsParent, $"Create {waypointsParentName}");
            Undo.SetTransformParent(waypointsParent.transform, parent.transform, $"Parent {waypointsParentName}");

            var waypoints = new Transform[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                var wp = new GameObject($"WP_{i:00}");
                Undo.RegisterCreatedObjectUndo(wp, "Create Waypoint");
                Undo.SetTransformParent(wp.transform, waypointsParent.transform, "Parent Waypoint");
                wp.transform.position = referenceTilemap.GetCellCenterWorld(new Vector3Int(cells[i].x, cells[i].y, 0));
                waypoints[i] = wp.transform;
            }

            var spawner = Undo.AddComponent<MonsterSpawner>(routeGo);
            SetPrivateTransformArray(spawner, "waypoints", waypoints);
            return spawner;
        }

        private static void BuildBase(
            GameObject parent, string baseName, MonsterSpawner spawner, Tilemap referenceTilemap, Vector2Int endCell)
        {
            var baseGo = new GameObject(baseName);
            Undo.RegisterCreatedObjectUndo(baseGo, $"Create {baseName}");
            Undo.SetTransformParent(baseGo.transform, parent.transform, $"Parent {baseName}");
            baseGo.transform.position = referenceTilemap.GetCellCenterWorld(new Vector3Int(endCell.x, endCell.y, 0));

            var baseHealth = Undo.AddComponent<BaseHealth>(baseGo);
            SetPrivateObjectReference(baseHealth, "spawner", spawner);
        }

        private static GameObject FindGameObjectInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var match = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.gameObject.name == name);
                if (match != null) return match.gameObject;
            }
            return null;
        }

        private static void SetPrivateObjectReference(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[TaegeukBoardScaffoldTool] {target.GetType().Name}에서 필드 '{fieldName}'을 못 찾음 - 필드명이 바뀌었는지 확인할 것.");
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void SetPrivateTransformArray(Object target, string fieldName, Transform[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[TaegeukBoardScaffoldTool] {target.GetType().Name}에서 필드 '{fieldName}'을 못 찾음 - 필드명이 바뀌었는지 확인할 것.");
                return;
            }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedProperties();
        }
    }
}
