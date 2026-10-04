using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TowerDefense.Monsters;

namespace TowerDefense.EditorTools
{
    /// <summary>
    /// 천명.pptx(2026-09-28, 최신 기획서) 슬라이드 8(MONSTER SPAWN)·18(ENEMIES) 기준으로
    /// 일반 몬스터 3종 + 보스 9종 MonsterDataSO, 웨이브 1~20 WaveDataSO를 생성/갱신하는
    /// 에디터 전용 툴. CharacterJsonImporter.cs와 같은 "있으면 갱신, 없으면 생성" 패턴을 씀
    /// (AssetDatabase.LoadAssetAtPath로 기존 애셋 찾고, 없으면 CreateInstance+CreateAsset) -
    /// 여러 번 실행해도 안전(idempotent)하고, Inspector에서 손으로 고친 값이 있어도 여기서
    /// 관리하는 필드만 덮어씀(리스트/체력/가중치 등 - abilityName 등도 포함, 전부 이 툴이
    /// 소스오브트루스인 필드들).
    ///
    /// 사용법:
    ///   1) Tools > Dev Scaffold > Create Monster & Wave Data (천명.pptx 기준) - 애셋 생성/갱신.
    ///   2) Tools > Dev Scaffold > Assign Waves To Monster Spawners - 1)에서 만든 Wave_01~20을
    ///      InGame 씬의 P1MonsterRoute/P2MonsterRoute(TaegeukBoardScaffoldTool이 만든 MonsterSpawner)
    ///      양쪽 waves 리스트에 순서대로 자동 배선. 반드시 1)을 먼저 실행해서 웨이브 애셋이 있어야
    ///      하고, TaegeukBoardScaffoldTool의 Assign Monster Routes로 스포너가 이미 만들어져 있어야 함.
    ///
    /// [프리팹 placeholder 안내] data.prefab은 SpawnMonster에서 null이면 스폰 자체를 막기
    /// 때문에(MonsterSpawner.SpawnMonster 가드) 반드시 뭔가 연결돼 있어야 함 - 몬스터/보스별
    /// 실제 아트/프리팹이 아직 없어서 전부 기존 Monster_Placeholder.prefab을 임시로 연결함.
    /// 실제 아트가 나오면 각 MonsterDataSO 애셋의 prefab 필드만 인스펙터에서 새 프리팹으로
    /// 바꿔주면 됨(이 툴을 다시 돌려도 prefab 필드는 "이미 있으면 안 덮어씀" 처리라 안전 -
    /// ApplyBossToAsset/ApplyNormalToAsset의 prefab 대입 부분 참고).
    ///
    /// [체력 스케일 표 - 웨이브당 caveat] 천명.pptx는 1/5/10/15/20웨이브 5개 지점만 표로
    /// 줌(soldierBaselineHp/bossBaselineHp) - 나머지 웨이브(2~4, 6~9, 11~14, 16~19)는 인접한
    /// 두 지점 사이를 선형보간(linear interpolation)한 값이고, 기획팀이 실제로 확정한 수치가
    /// 아님. WaveTable 배열의 각 원소 주석에 "anchor"(기획서 원문 수치)와 "interpolated"(선형
    /// 보간 placeholder)를 구분해뒀으니, 밸런스 담당자가 나중에 interpolated 값만 실제
    /// 곡선/시뮬레이션 결과로 교체하면 됨.
    ///
    /// [웨이브 상한 안내] 기획서 원문은 "라이프 3, 먼저 0 이하가 될 때까지 반복"이라 웨이브
    /// 진행 자체엔 정해진 끝이 없음(엔드리스). 반면 MonsterSpawner.waves는 List라서 유한하고,
    /// 이 툴은 우선 표에 나온 범위인 20웨이브까지만 만듦 - waves 리스트를 다 소진하면
    /// MonsterSpawner.RunWaves()는 OnAllWavesSpawned만 1회 발생시키고 그냥 끝남(더 이상
    /// 몬스터가 안 나옴). 21웨이브 이후를 어떻게 할지(리스트를 더 늘릴지, 마지막 웨이브를
    /// 반복시킬지, 수식으로 무한 생성할지)는 별도 결정이 필요해서 이 툴 스코프 밖으로 남겨둠.
    /// </summary>
    public static class MonsterDataScaffoldTool
    {
        private const string DataFolder = "Assets/Data/Monsters";
        private const string PlaceholderPrefabPath = "Assets/Prefabs/Monster_Placeholder.prefab";
        // 더미 Monster_Placeholder가 삭제된 뒤의 대체 - 아직 전용 아트가 없는 보스는 병사 아트 프리팹을 임시로 씀.
        private const string FallbackPrefabPath = "Assets/Prefabs/Monster_Soldier.prefab";
        private const string InGameScenePath = "Assets/Scenes/InGame.unity";
        private const string TaegeukBoardRootName = "TaegeukBoard"; // TaegeukBoardScaffoldTool.cs와 동일한 이름
        private const string P1RouteName = "P1MonsterRoute"; // TaegeukBoardScaffoldTool.cs가 만든 MonsterSpawner가 붙은 오브젝트
        private const string P2RouteName = "P2MonsterRoute";

        private const int WaveCount = 20;

        // 일반 몬스터 3종 - 천명.pptx 슬라이드 8/18 기준(병사 70%·1배·보통 / 기마병사 20%·0.5배·
        // 1.6배속 / 방패병사 10%·5배·0.6배속).
        private struct NormalMonsterSpec
        {
            public string assetName;
            public string monsterId;
            public string displayName;
            public float hpMultiplier;
            public float moveSpeed; // 칸/초 (엑셀 속도값 ÷ 10)
            public float spawnWeight;
            public float killSp;        // 1웨이브 처치 SP
            public float killSpPerWave; // 웨이브당 증가(+40% = killSp × 0.4)
        }

        private static readonly NormalMonsterSpec[] NormalMonsters =
        {
            new NormalMonsterSpec { assetName = "Monster_Soldier", monsterId = "soldier", displayName = "병사", hpMultiplier = 1f, moveSpeed = 0.5f, spawnWeight = 70f, killSp = 15f, killSpPerWave = 6f },
            new NormalMonsterSpec { assetName = "Monster_Cavalry", monsterId = "cavalry", displayName = "기마 병사", hpMultiplier = 0.5f, moveSpeed = 0.8f, spawnWeight = 20f, killSp = 9f, killSpPerWave = 3.6f },
            new NormalMonsterSpec { assetName = "Monster_Shield", monsterId = "shield", displayName = "방패 병사", hpMultiplier = 5f, moveSpeed = 0.3f, spawnWeight = 10f, killSp = 45f, killSpPerWave = 18f },
        };

        // 보스 9종 - 천명.pptx 슬라이드 18(ENEMIES) 원문 그대로(이름/기믹 이름/기믹 설명).
        // 엑셀(천명_통합) 몬스터 시트 기준: hpMultiplier = 보스 체력 배율(병사 체력 × 85에 곱함),
        // moveSpeed = 칸/초(시트 속도값 ÷ 10, 홍치달만 8 → 0.8).
        private struct BossSpec
        {
            public string assetName;
            public string monsterId;
            public string displayName;
            public string abilityName;
            public string abilityDescription;
            public float hpMultiplier;
            public float moveSpeed;
        }

        private static readonly BossSpec[] Bosses =
        {
            new BossSpec { assetName = "Boss_ChoiMinYeong", monsterId = "boss_choi_min_yeong", displayName = "최민영", abilityName = "소환술", abilityDescription = "몬스터 5마리 소환", hpMultiplier = 0.8f, moveSpeed = 0.5f },
            new BossSpec { assetName = "Boss_HongChiDal", monsterId = "boss_hong_chi_dal", displayName = "홍치달", abilityName = "빠른 이속", abilityDescription = "스킬 사용 시 이동속도 증가", hpMultiplier = 0.8f, moveSpeed = 0.8f },
            new BossSpec { assetName = "Boss_BakSanBal", monsterId = "boss_bak_san_bal", displayName = "박산발", abilityName = "분신", abilityDescription = "사망 시 분신 2명 소환 (최대 2회)", hpMultiplier = 0.35f, moveSpeed = 0.5f },
            new BossSpec { assetName = "Boss_JoJinHo", monsterId = "boss_jo_jin_ho", displayName = "조진호", abilityName = "조작", abilityDescription = "캐릭터 2명의 공격을 회복으로 전환", hpMultiplier = 0.8f, moveSpeed = 0.5f },
            new BossSpec { assetName = "Boss_JwaCheol", monsterId = "boss_jwa_cheol", displayName = "좌철", abilityName = "운석", abilityDescription = "운석에 맞은 캐릭터 제거", hpMultiplier = 0.75f, moveSpeed = 0.5f },
            new BossSpec { assetName = "Boss_SaHyeonTaek", monsterId = "boss_sa_hyeon_taek", displayName = "사현택", abilityName = "사슬", abilityDescription = "캐릭터 2명의 공격 봉인", hpMultiplier = 0.85f, moveSpeed = 0.5f },
            new BossSpec { assetName = "Boss_GongGamHo", monsterId = "boss_gong_gam_ho", displayName = "공감호", abilityName = "거북이 물약", abilityDescription = "필드 전체 공격속도 감소", hpMultiplier = 0.85f, moveSpeed = 0.5f },
            new BossSpec { assetName = "Boss_WonTaeYang", monsterId = "boss_won_tae_yang", displayName = "원태양", abilityName = "태양", abilityDescription = "원거리 공격 무효", hpMultiplier = 0.6f, moveSpeed = 0.5f },
            new BossSpec { assetName = "Boss_WonDal", monsterId = "boss_won_dal", displayName = "원달", abilityName = "달", abilityDescription = "근거리 공격 무효", hpMultiplier = 0.6f, moveSpeed = 0.5f },
        };

        // 웨이브별 체력 스케일 - anchor(기획서 원문: 1/5/10/15/20)와 interpolated(그 사이,
        // 인접 anchor 두 지점을 선형보간한 placeholder)를 명시적으로 구분함.
        private struct WavePoint
        {
            public int wave;
            public float soldierHp;
            public float bossHp;
            public bool isAnchor;
        }

        private static readonly WavePoint[] WaveTable =
        {
            new WavePoint { wave = 1,  soldierHp = 100f,  bossHp = 8600f,   isAnchor = true },
            new WavePoint { wave = 2,  soldierHp = 137.5f, bossHp = 11750f, isAnchor = false },
            new WavePoint { wave = 3,  soldierHp = 175f,  bossHp = 14900f,  isAnchor = false },
            new WavePoint { wave = 4,  soldierHp = 212.5f, bossHp = 18050f, isAnchor = false },
            new WavePoint { wave = 5,  soldierHp = 250f,  bossHp = 21200f,  isAnchor = true },
            new WavePoint { wave = 6,  soldierHp = 302f,  bossHp = 25580f,  isAnchor = false },
            new WavePoint { wave = 7,  soldierHp = 354f,  bossHp = 29960f,  isAnchor = false },
            new WavePoint { wave = 8,  soldierHp = 406f,  bossHp = 34340f,  isAnchor = false },
            new WavePoint { wave = 9,  soldierHp = 458f,  bossHp = 38720f,  isAnchor = false },
            new WavePoint { wave = 10, soldierHp = 510f,  bossHp = 43100f,  isAnchor = true },
            new WavePoint { wave = 11, soldierHp = 584f,  bossHp = 49320f,  isAnchor = false },
            new WavePoint { wave = 12, soldierHp = 658f,  bossHp = 55540f,  isAnchor = false },
            new WavePoint { wave = 13, soldierHp = 732f,  bossHp = 61760f,  isAnchor = false },
            new WavePoint { wave = 14, soldierHp = 806f,  bossHp = 67980f,  isAnchor = false },
            new WavePoint { wave = 15, soldierHp = 880f,  bossHp = 74200f,  isAnchor = true },
            new WavePoint { wave = 16, soldierHp = 966f,  bossHp = 81420f,  isAnchor = false },
            new WavePoint { wave = 17, soldierHp = 1052f, bossHp = 88640f,  isAnchor = false },
            new WavePoint { wave = 18, soldierHp = 1138f, bossHp = 95860f,  isAnchor = false },
            new WavePoint { wave = 19, soldierHp = 1224f, bossHp = 103080f, isAnchor = false },
            new WavePoint { wave = 20, soldierHp = 1310f, bossHp = 110300f, isAnchor = true },
        };

        [MenuItem("Tools/Dev Scaffold/Create Monster & Wave Data (천명.pptx 기준)")]
        public static void CreateAll()
        {
            EnsureFolder(DataFolder);

            var placeholderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderPrefabPath);
            if (placeholderPrefab == null) placeholderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPrefabPath);
            if (placeholderPrefab == null)
            {
                Debug.LogError($"[MonsterDataScaffoldTool] {PlaceholderPrefabPath} / {FallbackPrefabPath}를 둘 다 못 찾음 - prefab 필드를 못 채워서 중단함.");
                return;
            }

            var normalAssets = CreateOrUpdateNormalMonsters(placeholderPrefab);
            var bossAssets = CreateOrUpdateBosses(placeholderPrefab);
            var waveResult = CreateOrUpdateWaves(normalAssets, bossAssets);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MonsterDataScaffoldTool] 완료 - " +
                      $"일반 몬스터 {normalAssets.Count}종, 보스 {bossAssets.Count}종, " +
                      $"웨이브 {waveResult.created}개 생성 / {waveResult.updated}개 갱신. " +
                      $"anchor(기획서 확정치): 1/5/10/15/20웨이브, 나머지는 선형보간 placeholder - " +
                      "밸런스 담당자 검수 필요. prefab은 전부 Monster_Placeholder 임시 연결.");
        }

        private static List<MonsterDataSO> CreateOrUpdateNormalMonsters(GameObject placeholderPrefab)
        {
            var result = new List<MonsterDataSO>();

            foreach (var spec in NormalMonsters)
            {
                string assetPath = $"{DataFolder}/{spec.assetName}.asset";
                var so = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(assetPath);
                bool isNew = so == null;
                if (isNew)
                {
                    so = ScriptableObject.CreateInstance<MonsterDataSO>();
                }

                so.monsterId = spec.monsterId;
                so.displayName = spec.displayName;
                so.hpMultiplier = spec.hpMultiplier;
                so.moveSpeed = spec.moveSpeed;
                so.killSp = spec.killSp;
                so.killSpPerWave = spec.killSpPerWave;
                so.moveSpeedMultiplier = 1f; // 속도는 moveSpeed(칸/초)에 직접 반영 - 배율은 쓰지 않음
                so.spawnWeight = spec.spawnWeight;
                so.isBoss = false;
                so.damageToBase = 1; // 기획서: 통과 시 라이프 -1(일반 몬스터)
                so.abilityName = string.Empty;
                so.abilityDescription = string.Empty;
                if (so.prefab == null) so.prefab = placeholderPrefab; // 이미 실제 아트가 연결돼 있으면 안 덮어씀

                if (isNew)
                {
                    AssetDatabase.CreateAsset(so, assetPath);
                }
                else
                {
                    EditorUtility.SetDirty(so);
                }

                result.Add(so);
            }

            return result;
        }

        private static List<MonsterDataSO> CreateOrUpdateBosses(GameObject placeholderPrefab)
        {
            var result = new List<MonsterDataSO>();

            foreach (var spec in Bosses)
            {
                string assetPath = $"{DataFolder}/{spec.assetName}.asset";
                var so = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(assetPath);
                bool isNew = so == null;
                if (isNew)
                {
                    so = ScriptableObject.CreateInstance<MonsterDataSO>();
                }

                so.monsterId = spec.monsterId;
                so.displayName = spec.displayName;
                so.hpMultiplier = spec.hpMultiplier;
                so.moveSpeed = spec.moveSpeed;
                so.killSp = 450f;        // 엑셀: 보스 450 (+150/웨이브), 못 잡으면 절반
                so.killSpPerWave = 150f;
                so.moveSpeedMultiplier = 1f;
                so.spawnWeight = 0f; // 보스는 bossPool에서 개수 기준으로 뽑히므로 안 쓰임(MonsterData.cs 주석 참고)
                so.isBoss = true;
                so.damageToBase = 2; // 기획서: 통과 시 라이프 -2(보스)
                so.abilityName = spec.abilityName;
                so.abilityDescription = spec.abilityDescription;
                if (so.prefab == null) so.prefab = placeholderPrefab;

                if (isNew)
                {
                    AssetDatabase.CreateAsset(so, assetPath);
                }
                else
                {
                    EditorUtility.SetDirty(so);
                }

                result.Add(so);
            }

            return result;
        }

        private static (int created, int updated) CreateOrUpdateWaves(List<MonsterDataSO> monsterPool, List<MonsterDataSO> bossPool)
        {
            int created = 0;
            int updated = 0;

            foreach (var point in WaveTable)
            {
                string assetName = $"Wave_{point.wave:D2}";
                string assetPath = $"{DataFolder}/{assetName}.asset";
                var so = AssetDatabase.LoadAssetAtPath<WaveDataSO>(assetPath);
                bool isNew = so == null;
                if (isNew)
                {
                    so = ScriptableObject.CreateInstance<WaveDataSO>();
                }

                so.waveName = $"Wave {point.wave:D2}" + (point.isAnchor ? " (기획서 확정치)" : " (선형보간 placeholder)");
                so.delayBeforeWave = 5f;
                so.waveDuration = 80f; // 기획서: 웨이브 80초
                so.ownMonsterCount = 150; // 기획서: 웨이브마다 150마리
                so.monsterPool = new List<MonsterDataSO>(monsterPool);
                so.bossPool = new List<MonsterDataSO>(bossPool);
                so.soldierBaselineHp = point.soldierHp;
                so.bossBaselineHp = point.bossHp;

                if (isNew)
                {
                    AssetDatabase.CreateAsset(so, assetPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(so);
                    updated++;
                }
            }

            return (created, updated);
        }

        /// <summary>
        /// Assets/Data/Monsters/Wave_01~Wave_20을 순서대로 찾아서 InGame 씬의 P1MonsterRoute/
        /// P2MonsterRoute(TaegeukBoardScaffoldTool.BuildRouteAndSpawner가 만든, MonsterSpawner가
        /// 붙은 오브젝트) 양쪽 MonsterSpawner.waves에 그대로 배선함 - CreateAll()로 웨이브 애셋을
        /// 먼저 만들어둔 다음에 실행할 것. 여러 번 실행해도 안전(매번 처음부터 다시 배선함).
        /// </summary>
        [MenuItem("Tools/Dev Scaffold/Assign Waves To Monster Spawners")]
        public static void AssignWavesToSpawners()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[MonsterDataScaffoldTool] Play 모드에서는 실행할 수 없음 - Play를 정지한 뒤 다시 실행할 것.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[MonsterDataScaffoldTool] 사용자가 저장을 취소해서 작업을 중단함.");
                return;
            }

            var waves = new List<WaveDataSO>();
            for (int i = 1; i <= WaveCount; i++)
            {
                string assetPath = $"{DataFolder}/Wave_{i:D2}.asset";
                var wave = AssetDatabase.LoadAssetAtPath<WaveDataSO>(assetPath);
                if (wave == null)
                {
                    Debug.LogError($"[MonsterDataScaffoldTool] {assetPath}를 못 찾음 - 먼저 " +
                                    "'Tools > Dev Scaffold > Create Monster & Wave Data'를 실행해서 웨이브 애셋을 만들 것.");
                    return;
                }
                waves.Add(wave);
            }

            var inGameScene = SceneManager.GetActiveScene().path == InGameScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(InGameScenePath, OpenSceneMode.Single);

            var taegeukRoot = FindGameObjectInScene(inGameScene, TaegeukBoardRootName);
            if (taegeukRoot == null)
            {
                Debug.LogError($"[MonsterDataScaffoldTool] 씬에서 '{TaegeukBoardRootName}'을 못 찾음 - " +
                                "TaegeukBoardScaffoldTool로 보드/경로를 먼저 만들었는지 확인할 것.");
                return;
            }

            var p1Spawner = FindSpawner(taegeukRoot, P1RouteName);
            var p2Spawner = FindSpawner(taegeukRoot, P2RouteName);
            if (p1Spawner == null || p2Spawner == null)
            {
                Debug.LogError($"[MonsterDataScaffoldTool] '{P1RouteName}' 또는 '{P2RouteName}'에서 MonsterSpawner를 " +
                                "못 찾음 - TaegeukBoardScaffoldTool의 Assign Monster Routes 메뉴를 먼저 실행했는지 확인할 것.");
                return;
            }

            SetPrivateObjectArray(p1Spawner, "waves", waves.Cast<Object>().ToArray());
            SetPrivateObjectArray(p2Spawner, "waves", waves.Cast<Object>().ToArray());

            EditorSceneManager.MarkSceneDirty(inGameScene);
            EditorSceneManager.SaveScene(inGameScene);

            Debug.Log($"[MonsterDataScaffoldTool] '{P1RouteName}'/'{P2RouteName}' 양쪽 MonsterSpawner.waves에 " +
                      $"Wave_01~Wave_{WaveCount:D2} {waves.Count}개를 순서대로 배선 완료.");
        }

        private static MonsterSpawner FindSpawner(GameObject taegeukRoot, string routeName)
        {
            var routeTransform = taegeukRoot.transform.Find(routeName);
            return routeTransform != null ? routeTransform.GetComponent<MonsterSpawner>() : null;
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

        private static void SetPrivateObjectArray(Object target, string fieldName, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[MonsterDataScaffoldTool] {target.GetType().Name}에서 필드 '{fieldName}'을 못 찾음 - 필드명이 바뀌었는지 확인할 것.");
                return;
            }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedProperties();
        }

        private static void EnsureFolder(string fullPath)
        {
            string[] parts = fullPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
