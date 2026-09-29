using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TowerDefense.Map;
using TowerDefense.Monsters;

namespace TowerDefense.EditorTools
{
    /// <summary>
    /// [버그 수정 - 2026-09-30] 실제 2인 매칭 테스트에서 HP/SP가 계속 0/0이고 웨이브가 전혀 시작
    /// 안 되는 문제를 진단한 결과, InGame 씬의 P1Board/P2Board GameObject에 PlayerBoard 컴포넌트
    /// 자체가 아예 붙어있지 않았음(GUID 기준으로 씬 파일 전체를 검색해서 확인함 - 프리팹 인스턴스가
    /// 아니라 씬에 직접 배치된 오브젝트라 프리팹 쪽에 숨어있을 가능성도 없음). MatchController.
    /// RegisterBoard는 오직 PlayerBoard.Start()의 RegisterWhenReady 코루틴을 통해서만 호출되므로,
    /// 컴포넌트가 없으면 두 보드 다 영원히 등록이 안 되고 TryStartMatch()가 항상
    /// "보드 등록 미완료"로 막힘 - 이번에 확인된 진단 로그(TryStartMatch: 등록된 키: (없음))와
    /// 정확히 일치함.
    ///
    /// [보드 하위 구조가 처음 가정과 달랐음 - 1차 실행 결과로 확인] PlacementGrid/BaseHealth/
    /// MonsterSpawner가 한 GameObject 밑에 다 같이 있는 게 아니라, TaegeukBoard 루트 밑에
    /// P1Board(Grid)/P1Base(Health)/P1MonsterRoute(Spawner)가 서로 다른 형제 GameObject로
    /// 흩어져 있음(GUID 기준 재검색으로 확인함). 그래서 GetComponentInChildren 한 번으로는 못 찾고,
    /// 이름으로 각각 따로 찾아야 함. 게다가 MatchResourceManager는 씬 전체에 단 하나도 안 붙어있어서
    /// (SP 시스템 자체가 아직 씬에 배선된 적이 없음) - 새로 AddComponent해서 만들어야 함(기본
    /// 인스펙터 값 그대로 써도 안전함, MatchResourceManager.cs의 필드 기본값 참고: maxSP=100,
    /// spPerSecond=5, startingSP=50 - 전부 [SerializeField] 기본값이 있어서 컴포넌트만 추가해도
    /// 정상 동작함).
    ///
    /// 이 툴은 P1Board(boardIndex=0)/P2Board(boardIndex=1)에 PlayerBoard + MatchResourceManager를
    /// 추가하고(이미 있으면 재사용), P1Base/P2Base의 BaseHealth와 P1MonsterRoute/P2MonsterRoute의
    /// MonsterSpawner를 이름으로 찾아서 4개 필드를 배선함(TaegeukBoardScaffoldTool/
    /// MonsterDataScaffoldTool과 같은 SerializedObject 기반 private 필드 배선 패턴 재사용).
    /// 여러 번 실행해도 안전(매번 새로 찾아서 덮어씀).
    /// </summary>
    public static class PlayerBoardScaffoldTool
    {
        private const string InGameScenePath = "Assets/Scenes/InGame.unity";

        private static readonly BoardSpec[] Boards =
        {
            new BoardSpec { boardIndex = 0, boardRootName = "P1Board", baseName = "P1Base", spawnerRouteName = "P1MonsterRoute" },
            new BoardSpec { boardIndex = 1, boardRootName = "P2Board", baseName = "P2Base", spawnerRouteName = "P2MonsterRoute" },
        };

        private struct BoardSpec
        {
            public int boardIndex;
            public string boardRootName;
            public string baseName;
            public string spawnerRouteName;
        }

        [MenuItem("Tools/Dev Scaffold/Setup PlayerBoard Components")]
        public static void SetupPlayerBoards()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[PlayerBoardScaffoldTool] Play 모드에서는 실행할 수 없음 - Play를 정지한 뒤 다시 실행할 것.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[PlayerBoardScaffoldTool] 사용자가 저장을 취소해서 작업을 중단함.");
                return;
            }

            var inGameScene = SceneManager.GetActiveScene().path == InGameScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(InGameScenePath, OpenSceneMode.Single);

            bool allOk = true;
            foreach (var spec in Boards)
            {
                if (!SetupOneBoard(inGameScene, spec)) allOk = false;
            }

            if (!allOk)
            {
                Debug.LogError("[PlayerBoardScaffoldTool] 일부 보드 배선 실패 - 위 에러 메시지를 확인할 것. 씬은 저장하지 않음.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(inGameScene);
            EditorSceneManager.SaveScene(inGameScene);

            Debug.Log("[PlayerBoardScaffoldTool] P1Board(boardIndex=0)/P2Board(boardIndex=1)에 " +
                      "PlayerBoard + MatchResourceManager 컴포넌트를 추가하고 Grid/Spawner/Health/Resources를 " +
                      "자동 배선 완료. 이제 Play해서 매치가 정상 시작되는지(HP/SP가 0/0이 아닌 실제 값으로 바뀌는지) 확인할 것.");
        }

        private static bool SetupOneBoard(Scene scene, BoardSpec spec)
        {
            var boardRoot = FindGameObjectInScene(scene, spec.boardRootName);
            if (boardRoot == null)
            {
                Debug.LogError($"[PlayerBoardScaffoldTool] 씬에서 '{spec.boardRootName}'을 못 찾음 - TaegeukBoardScaffoldTool로 보드를 먼저 만들었는지 확인할 것.");
                return false;
            }

            var grid = boardRoot.GetComponentInChildren<PlacementGrid>(true);

            var baseGo = FindGameObjectInScene(scene, spec.baseName);
            var health = baseGo != null ? baseGo.GetComponent<BaseHealth>() : null;

            var spawnerGo = FindGameObjectInScene(scene, spec.spawnerRouteName);
            var spawner = spawnerGo != null ? spawnerGo.GetComponent<MonsterSpawner>() : null;

            if (grid == null || spawner == null || health == null)
            {
                Debug.LogError($"[PlayerBoardScaffoldTool] '{spec.boardRootName}' 계열에서 필요한 컴포넌트를 못 찾음 " +
                                $"(Grid@{spec.boardRootName}={grid != null}, Spawner@{spec.spawnerRouteName}={spawner != null}, Health@{spec.baseName}={health != null}) - " +
                                "보드 하위 구조가 예상과 다른지 확인할 것.");
                return false;
            }

            // MatchResourceManager는 씬에 하나도 없었으므로 보드 루트에 새로 추가함(기본 인스펙터
            // 값만으로도 동작함 - 클래스 doc 참고).
            var resources = boardRoot.GetComponent<MatchResourceManager>();
            if (resources == null)
            {
                resources = boardRoot.AddComponent<MatchResourceManager>();
            }

            var playerBoard = boardRoot.GetComponent<PlayerBoard>();
            if (playerBoard == null)
            {
                playerBoard = boardRoot.AddComponent<PlayerBoard>();
            }

            var serialized = new SerializedObject(playerBoard);
            SetIntField(serialized, "boardIndex", spec.boardIndex);
            SetObjectField(serialized, "grid", grid);
            SetObjectField(serialized, "spawner", spawner);
            SetObjectField(serialized, "health", health);
            SetObjectField(serialized, "resources", resources);
            serialized.ApplyModifiedProperties();

            return true;
        }

        private static void SetIntField(SerializedObject serialized, string fieldName, int value)
        {
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[PlayerBoardScaffoldTool] PlayerBoard에서 필드 '{fieldName}'을 못 찾음 - 필드명이 바뀌었는지 확인할 것.");
                return;
            }
            property.intValue = value;
        }

        private static void SetObjectField(SerializedObject serialized, string fieldName, Object value)
        {
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[PlayerBoardScaffoldTool] PlayerBoard에서 필드 '{fieldName}'을 못 찾음 - 필드명이 바뀌었는지 확인할 것.");
                return;
            }
            property.objectReferenceValue = value;
        }

        // MonsterDataScaffoldTool/TaegeukBoardScaffoldTool과 동일한 패턴 - 씬 전체 루트를 순회하며
        // 이름으로 찾음(다른 클래스라 헬퍼를 공유하지 않고 그대로 복제함, 기존 컨벤션 참고).
        private static GameObject FindGameObjectInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var t in transforms)
                {
                    if (t.gameObject.name == name) return t.gameObject;
                }
            }
            return null;
        }
    }
}
