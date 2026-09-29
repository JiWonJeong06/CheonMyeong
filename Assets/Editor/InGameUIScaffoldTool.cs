using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using TowerDefense.UI;
using TowerDefense.Map;
using TowerDefense.Network;

namespace TowerDefense.EditorTools
{
    /// <summary>
    /// 인게임 일시정지 UI(PauseMenuPopup, InGame 씬 전용 ConfirmDialog, PopupManager)와 솔로 테스트용
    /// 네트워크 부트스트랩(InGameSoloTestBootstrap), 덱 패널을 위한 카메라 레이아웃 보정(InGameCameraLayout)을
    /// InGame 씬에 자동으로 배치/연결하는 에디터 툴. Tools/Dev Scaffold 메뉴에 추가함(커밋 로그에
    /// 남은 DevScaffoldTool과 같은 계열 - 프리팹/씬 자동 배치를 코드로 재현 가능하게 해서, 씬 파일
    /// 수정 내역을 리뷰하기 쉽게 하고 사람이 매번 인스펙터에서 드래그하는 실수를 줄이기 위함).
    ///
    /// 여러 번 실행해도 안전(idempotent)함 - 이미 만들어진 GameObject/연결이 있으면 새로 만들지 않고
    /// 참조만 다시 맞춰줌. 그래서 UXML/컨트롤러를 수정한 뒤 다시 실행해도 중복 오브젝트가 안 생김.
    ///
    /// 이 스크립트 자체는 Assets/Editor 폴더 밑에 있어서 빌드에는 포함되지 않음(유니티가 Editor
    /// 폴더를 자동으로 에디터 전용으로 취급함).
    /// </summary>
    public static class InGameUIScaffoldTool
    {
        private const string InGameScenePath = "Assets/Scenes/InGame.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string PanelSettingsPath = "Assets/UI Toolkit/All Panel Settings.asset";
        private const string PauseMenuUxmlPath = "Assets/UI/Popups/PauseMenu/PauseMenu.uxml";
        private const string InGameDeckUxmlPath = "Assets/UI/Screens/InGame/InGameDeck.uxml";

        private const string ManagersName = "Managers";
        private const string ConfirmDialogName = "ConfirmDialog";
        private const string PauseMenuPopupName = "PauseMenuPopup";
        private const string InGameHudName = "InGameHUD";
        private const string InGameDeckName = "InGameDeck";
        private const string MatchResultName = "MatchResult";
        private const string NetworkManagerName = "NetworkManager";
        private const string SoloNetworkManagerName = "SoloTestNetworkManager";
        private const string MainCameraName = "Main Camera";

        private const int PauseMenuSortingOrder = 10;
        private const int InGameDeckSortingOrder = 0; // HUD와 동일 - 상시 노출 UI, 모달 팝업 아님
        private const int MatchResultSortingOrder = 30; // 다른 팝업(0~15)보다 항상 위에 떠야 함

        [MenuItem("Tools/Dev Scaffold/Setup InGame Pause UI")]
        public static void SetupInGamePauseUI()
        {
            // EditorSceneManager의 씬 열기/저장 API는 Play 모드에서 호출하면 전부
            // InvalidOperationException을 던짐 - Play 모드에서 실수로 누르면 조용히 죽는 대신
            // 여기서 먼저 걸러서 뭘 해야 하는지 알려줌.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[InGameUIScaffoldTool] Play 모드에서는 실행할 수 없음 - Play를 정지한 뒤 다시 실행할 것.");
                EditorUtility.DisplayDialog("InGame Pause UI Setup", "Play 모드를 먼저 정지한 뒤 다시 실행해주세요.", "확인");
                return;
            }

            // 현재 열려있는 씬에 저장 안 한 변경사항이 있으면 유니티 표준 저장 확인창을 띄움 -
            // 이걸 건너뛰면 아래에서 씬을 새로 열 때(OpenScene) 사용자가 모르게 작업 내용을 잃을 수 있음.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[InGameUIScaffoldTool] 사용자가 저장을 취소해서 작업을 중단함.");
                return;
            }

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var pauseMenuUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PauseMenuUxmlPath);
            var inGameDeckUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(InGameDeckUxmlPath);

            if (panelSettings == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] PanelSettings를 못 찾음: {PanelSettingsPath}");
                return;
            }
            if (pauseMenuUxml == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] PauseMenu.uxml을 못 찾음: {PauseMenuUxmlPath} - 프로젝트에 파일이 있는지, 유니티가 임포트를 끝냈는지 확인할 것.");
                return;
            }
            if (inGameDeckUxml == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] InGameDeck.uxml을 못 찾음: {InGameDeckUxmlPath} - 프로젝트에 파일이 있는지, 유니티가 임포트를 끝냈는지 확인할 것.");
                return;
            }

            // InGame 씬을 단독으로 염(이미 열려있고 활성 씬이면 그대로 사용, 아니면 새로 로드).
            var inGameScene = SceneManager.GetActiveScene().path == InGameScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(InGameScenePath, OpenSceneMode.Single);

            bool changed = false;

            // 1) PopupManager - Managers GameObject에 부착(없으면 새로 만듦).
            var managers = FindGameObjectInScene(inGameScene, ManagersName);
            if (managers == null)
            {
                managers = new GameObject(ManagersName);
                Undo.RegisterCreatedObjectUndo(managers, "Create Managers (InGame)");
                SceneManager.MoveGameObjectToScene(managers, inGameScene);
                changed = true;
            }
            if (managers.GetComponent<PopupManager>() == null)
            {
                Undo.AddComponent<PopupManager>(managers);
                changed = true;
            }

            // 2) ConfirmDialog - InGame 씬엔 원래 없어서 MainMenu 씬에서 복제해옴(항복 확인에 재사용).
            var confirmDialog = FindGameObjectInScene(inGameScene, ConfirmDialogName);
            if (confirmDialog == null)
            {
                confirmDialog = CloneFromMainMenu(ConfirmDialogName, ConfirmDialogName, inGameScene);
                if (confirmDialog == null)
                {
                    Debug.LogError($"[InGameUIScaffoldTool] MainMenu 씬에서 '{ConfirmDialogName}'을 못 찾아 복제 실패 - 이후 단계를 건너뜀.");
                    return;
                }
                changed = true;
            }
            confirmDialog.SetActive(false); // 팝업은 기본 비활성화 상태여야 함(다른 팝업들과 동일 규칙)

            var confirmDialogController = confirmDialog.GetComponent<ConfirmDialogController>();
            if (confirmDialogController == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] '{ConfirmDialogName}'에 ConfirmDialogController 컴포넌트가 없음 - 복제본이 손상됐을 수 있음.");
                return;
            }

            // 3) PauseMenuPopup - 없으면 새로 만들고, UIDocument/PauseMenuController를 세팅함.
            var pauseMenuGo = FindGameObjectInScene(inGameScene, PauseMenuPopupName);
            if (pauseMenuGo == null)
            {
                pauseMenuGo = new GameObject(PauseMenuPopupName);
                Undo.RegisterCreatedObjectUndo(pauseMenuGo, "Create PauseMenuPopup");
                SceneManager.MoveGameObjectToScene(pauseMenuGo, inGameScene);
                changed = true;
            }
            pauseMenuGo.SetActive(false); // 다른 팝업들처럼 기본 비활성화

            var pauseMenuDocument = pauseMenuGo.GetComponent<UIDocument>();
            if (pauseMenuDocument == null) pauseMenuDocument = Undo.AddComponent<UIDocument>(pauseMenuGo);
            pauseMenuDocument.panelSettings = panelSettings;
            pauseMenuDocument.visualTreeAsset = pauseMenuUxml;
            pauseMenuDocument.sortingOrder = PauseMenuSortingOrder;

            var pauseMenuController = pauseMenuGo.GetComponent<PauseMenuController>();
            if (pauseMenuController == null) pauseMenuController = Undo.AddComponent<PauseMenuController>(pauseMenuGo);

            // confirmDialog 필드는 private [SerializeField]라 SerializedObject로 설정함
            // (public으로 안 바꾸는 이유: 인스펙터 캡슐화를 유지하되 에디터 툴에서만 우회 접근).
            SetPrivateObjectReference(pauseMenuController, "confirmDialog", confirmDialogController);

            // 3.5) InGameDeck - 없으면 새로 만들고, UIDocument/InGameDeckController를 세팅함.
            //      InGameDeckController는 런타임 싱글턴(DeckManager.Instance, TowerPlacementController.Instance,
            //      MatchController.Instance)만으로 동작해서 별도 인스펙터 참조 연결이 필요 없음.
            var deckGo = FindGameObjectInScene(inGameScene, InGameDeckName);
            if (deckGo == null)
            {
                deckGo = new GameObject(InGameDeckName);
                Undo.RegisterCreatedObjectUndo(deckGo, "Create InGameDeck");
                SceneManager.MoveGameObjectToScene(deckGo, inGameScene);
                changed = true;
            }

            var deckDocument = deckGo.GetComponent<UIDocument>();
            if (deckDocument == null) deckDocument = Undo.AddComponent<UIDocument>(deckGo);
            deckDocument.panelSettings = panelSettings;
            deckDocument.visualTreeAsset = inGameDeckUxml;
            deckDocument.sortingOrder = InGameDeckSortingOrder;

            if (deckGo.GetComponent<InGameDeckController>() == null)
            {
                Undo.AddComponent<InGameDeckController>(deckGo);
                changed = true;
            }

            // 4) InGameHUD의 InGameHUDController.pauseMenuPopup 필드 연결.
            var hudGo = FindGameObjectInScene(inGameScene, InGameHudName);
            if (hudGo == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] '{InGameHudName}' GameObject를 InGame 씬에서 못 찾음.");
                return;
            }
            var hudController = hudGo.GetComponent<InGameHUDController>();
            if (hudController == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] '{InGameHudName}'에 InGameHUDController 컴포넌트가 없음.");
                return;
            }
            SetPrivateObjectReference(hudController, "pauseMenuPopup", pauseMenuController);

            // 5) MatchResult - 다른 팝업(0~15)보다 항상 위에 뜨도록 정렬 순서를 올림.
            var matchResultGo = FindGameObjectInScene(inGameScene, MatchResultName);
            if (matchResultGo == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] '{MatchResultName}' GameObject를 InGame 씬에서 못 찾음.");
                return;
            }
            var matchResultDocument = matchResultGo.GetComponent<UIDocument>();
            if (matchResultDocument == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] '{MatchResultName}'에 UIDocument 컴포넌트가 없음.");
                return;
            }
            if (matchResultDocument.sortingOrder != MatchResultSortingOrder)
            {
                Undo.RecordObject(matchResultDocument, "Set MatchResult Sorting Order");
                matchResultDocument.sortingOrder = MatchResultSortingOrder;
                changed = true;
            }

            // 6) SoloTestNetworkManager - InGame 씬을 매칭 없이 단독 Play해도 Rpc 호출이 죽지 않도록,
            //    MainMenu의 NetworkManager를 복제해서 기본 비활성 상태로 넣어둠. 정상 흐름(MainMenu ->
            //    매칭)에선 InGameSoloTestBootstrap이 NetworkManager.Singleton != null인 걸 보고
            //    이 오브젝트를 절대 활성화하지 않으므로 실제 대전에는 영향이 없음.
            var soloNetworkManagerGo = FindGameObjectInScene(inGameScene, SoloNetworkManagerName);
            if (soloNetworkManagerGo == null)
            {
                soloNetworkManagerGo = CloneFromMainMenu(NetworkManagerName, SoloNetworkManagerName, inGameScene);
                if (soloNetworkManagerGo == null)
                {
                    Debug.LogError($"[InGameUIScaffoldTool] MainMenu 씬에서 '{NetworkManagerName}'을 못 찾아 복제 실패 - 솔로 테스트 부트스트랩 연결을 건너뜀.");
                }
                else
                {
                    changed = true;
                }
            }
            if (soloNetworkManagerGo != null)
            {
                soloNetworkManagerGo.SetActive(false); // 반드시 비활성 상태로 둘 것 - 활성화는 런타임에 부트스트랩이 조건부로만 함

                if (managers.GetComponent<InGameSoloTestBootstrap>() == null)
                {
                    Undo.AddComponent<InGameSoloTestBootstrap>(managers);
                    changed = true;
                }
                var soloBootstrap = managers.GetComponent<InGameSoloTestBootstrap>();
                SetPrivateObjectReference(soloBootstrap, "soloNetworkManager", soloNetworkManagerGo);
            }

            // 7) Main Camera - InGameCameraLayout을 붙여서 오른쪽 덱 패널만큼 뷰포트/위치를 보정함.
            //    실제 예약 비율은 덱 패널이 런타임에 스스로 측정해서 넘겨줌(InGameDeckLayout 브릿지,
            //    InGameCameraLayout.cs 클래스 doc 참고) - 여기서는 컴포넌트 부착만 하면 됨, 필드
            //    설정 불필요. 씬 뷰(에디터)엔 반영 안 되고 Play 시점에만 적용됨.
            var mainCameraGo = FindGameObjectInScene(inGameScene, MainCameraName);
            if (mainCameraGo == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] '{MainCameraName}' GameObject를 InGame 씬에서 못 찾음 - 카메라 레이아웃 보정을 건너뜀.");
            }
            else if (mainCameraGo.GetComponent<InGameCameraLayout>() == null)
            {
                Undo.AddComponent<InGameCameraLayout>(mainCameraGo);
                changed = true;
            }

            EditorSceneManager.MarkSceneDirty(inGameScene);
            EditorSceneManager.SaveScene(inGameScene);

            string message = changed
                ? "InGame 씬에 일시정지 UI + 덱 패널 + 카메라 레이아웃 보정 + 솔로 테스트 부트스트랩을 배치/연결하고 저장했습니다."
                : "이미 전부 배치돼 있어서 참조만 다시 확인하고 저장했습니다.";
            Debug.Log($"[InGameUIScaffoldTool] {message}");
            EditorUtility.DisplayDialog("InGame Pause UI Setup", message, "확인");
        }

        // scene.GetRootGameObjects()는 최상위 오브젝트만 주므로, 자식까지 포함해서 이름으로 찾음
        // (비활성 오브젝트도 포함 - includeInactive: true, 팝업들은 기본 비활성화 상태라 반드시 필요).
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

        // MainMenu 씬을 애디티브로 잠깐 열어서 원본을 복제한 뒤, 원본 씬은 저장하지 않고 다시 닫음
        // (원본 씬 자체는 절대 건드리지 않음 - 읽기 전용으로만 씀). cloneName이 sourceObjectName과
        // 다르면 복제본 이름을 바꿔서 넣음(예: "NetworkManager" -> "SoloTestNetworkManager").
        private static GameObject CloneFromMainMenu(string sourceObjectName, string cloneName, Scene targetScene)
        {
            var mainMenuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);
            try
            {
                var source = FindGameObjectInScene(mainMenuScene, sourceObjectName);
                if (source == null) return null;

                var clone = Object.Instantiate(source);
                clone.name = cloneName; // Instantiate가 붙이는 "(Clone)" 접미사 제거 + 필요 시 이름 변경
                Undo.RegisterCreatedObjectUndo(clone, $"Clone {sourceObjectName} from MainMenu");
                SceneManager.MoveGameObjectToScene(clone, targetScene);
                return clone;
            }
            finally
            {
                // dirty 여부와 무관하게 MainMenu 씬은 절대 저장하지 않고 닫음 - 우리는 거기서
                // 아무것도 바꾸지 않았어야 하고(읽기 전용 복제), 혹시 모를 실수로 인한 저장을 막기 위함.
                EditorSceneManager.CloseScene(mainMenuScene, true);
            }
        }

        private static void SetPrivateObjectReference(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[InGameUIScaffoldTool] {target.GetType().Name}에서 필드 '{fieldName}'을 못 찾음 - 필드명이 바뀌었는지 확인할 것.");
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }
    }
}
