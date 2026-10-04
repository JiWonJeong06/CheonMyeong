using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TowerDefense.Economy;

namespace TowerDefense.Data
{
    /// <summary>
    /// 노드트리(강화) 더미 매니저 - Resources/Data/TechTreeData.json을 읽어서 순수 스탯 강화 노드
    /// 목록/선행조건을 갖고 있고, 거기에 더해 CharacterDatabase에 있는 캐릭터 수만큼 "캐릭터 해금 노드"를
    /// 런타임에 자동으로 만들어서 같은 트리에 섞어 넣음(캐릭터 해금은 이제 보관함이 아니라 여기서만 진행됨).
    ///
    /// 스탯 노드는 항상 Gold(무료 재화)만 소모하고 해금 여부를 자체 PlayerPrefs 목록에 저장하지만,
    /// 캐릭터 해금 노드는 비용/통화(Gold or Gem)와 해금 여부 저장을 전부 CharacterUnlockManager에
    /// 위임함(TechNodeData.linkedCharacterId가 채워져 있으면 캐릭터 노드) - 그래야 보관함/테크트리
    /// 어느 쪽에서 봐도 "이 캐릭터가 해금됐는지"가 항상 같은 값으로 일치함.
    /// </summary>
    public class TechTreeManager : MonoBehaviour
    {
        public static TechTreeManager Instance { get; private set; }

        private const string JsonResourcePath = "Data/TechTreeData"; // 확장자 제외, Resources 기준 상대경로
        private const string UnlockedKey = "TechTree_UnlockedIds";
        private const string CharacterNodeIdPrefix = "char_"; // 캐릭터 해금 노드 id 접두어 (JSON 노드 id와 충돌 방지)
        public const string HubNodeIdPrefix = "hub_";           // 조직 허브 노드 id 접두어

        /// <summary>허브(조직) 표시 순서 - 엑셀 '조직' 시트 순서. 노드 트리 라인 이름과 같음(NodeTreeLines).</summary>
        public static readonly string[] HubLines =
        {
            NodeTreeLines.Cheonmyeonghoe, NodeTreeLines.Heugyeonhoe, NodeTreeLines.Biseoncheong,
            NodeTreeLines.Jihamun, NodeTreeLines.Pungnyudan, NodeTreeLines.Hwarranghoe
        };

        public static string HubNodeId(string line) => HubNodeIdPrefix + line;

        private readonly Dictionary<string, TechNodeData> _nodesById = new();
        private readonly HashSet<string> _unlockedIds = new();

        public event Action<string> OnNodeUnlocked;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadNodes();
            LoadUnlockedState();
        }

        public IReadOnlyList<TechNodeData> GetAllNodes() => _nodesById.Values.ToList();

        public bool IsUnlocked(string nodeId)
        {
            if (!_nodesById.TryGetValue(nodeId, out var node)) return false;
            if (node.isHub) return true; // 허브는 폴더 같은 묶음이라 항상 열려 있음 - 하위 노드의 선행 조건이 항상 충족됨

            if (!string.IsNullOrEmpty(node.linkedCharacterId))
            {
                return CharacterUnlockManager.Instance != null &&
                       CharacterUnlockManager.Instance.IsUnlocked(node.linkedCharacterId);
            }

            return _unlockedIds.Contains(nodeId);
        }

        /// <summary>
        /// 해금된 노드들의 효과 합(퍼센트, 5 = +5%). 소속 효과(Line*)는 line이 같은 노드만, 전체 효과
        /// (GlobalAttackPercent)는 line 무관. unlockedOverride가 null이면 이 기기의 해금 상태를, 아니면 그
        /// 집합(예: 서버가 클라이언트에게서 받은 PlayerLoadout.unlockedNodeIds)을 기준으로 계산함.
        /// 딕셔너리 값을 foreach로 도는 구조체 열거자라 할당이 없음.
        /// </summary>
        public float GetBonusPercent(NodeEffectType type, string line, HashSet<string> unlockedOverride = null)
        {
            if (type == NodeEffectType.None) return 0f;

            float sum = 0f;
            foreach (var node in _nodesById.Values)
            {
                if (node.ParsedEffectType != type) continue;
                if (type != NodeEffectType.GlobalAttackPercent &&
                    !string.Equals(node.effectLine, line, StringComparison.Ordinal)) continue;

                bool unlocked = unlockedOverride != null ? unlockedOverride.Contains(node.nodeId) : IsUnlocked(node.nodeId);
                if (unlocked) sum += node.effectValue;
            }
            return sum;
        }

        /// <summary>해금된 "스탯 노드" id를 destination에 복사(캐릭터 해금 노드는 CharacterUnlockManager 소관이라 제외).</summary>
        public void CopyUnlockedStatNodeIdsTo(HashSet<string> destination)
        {
            foreach (string id in _unlockedIds) destination.Add(id);
        }

        public bool ArePrerequisitesMet(string nodeId)
        {
            if (!_nodesById.TryGetValue(nodeId, out var node)) return false;
            if (node.prerequisiteNodeIds == null || node.prerequisiteNodeIds.Count == 0) return true;
            return node.prerequisiteNodeIds.All(IsUnlocked);
        }

        public bool TryUnlock(string nodeId)
        {
            if (!_nodesById.TryGetValue(nodeId, out var node)) return false;
            if (node.isHub) return false; // 허브는 해금 대상이 아님
            if (IsUnlocked(nodeId)) return false;
            if (!ArePrerequisitesMet(nodeId)) return false;

            if (!string.IsNullOrEmpty(node.linkedCharacterId))
            {
                // 캐릭터 해금 노드 - 비용/통화(Gold or Gem)와 실제 해금 저장은 CharacterUnlockManager가 처리함.
                var characterData = CharacterDatabase.GetById(node.linkedCharacterId);
                if (characterData == null)
                {
                    Debug.LogWarning($"[TechTreeManager] linkedCharacterId '{node.linkedCharacterId}'에 해당하는 캐릭터를 못 찾음.");
                    return false;
                }

                if (CharacterUnlockManager.Instance == null)
                {
                    Debug.LogWarning("[TechTreeManager] CharacterUnlockManager를 찾을 수 없음.");
                    return false;
                }

                bool unlocked = CharacterUnlockManager.Instance.TryUnlock(characterData);
                if (!unlocked) return false; // 재화 부족 등 - CharacterUnlockManager.TryUnlock이 이미 판단함

                OnNodeUnlocked?.Invoke(nodeId);
                return true;
            }

            // 순수 스탯 노드 - 항상 Gold만 소모 (기획 확정 사항)
            if (EconomyManager.Instance == null)
            {
                Debug.LogWarning("[TechTreeManager] EconomyManager를 찾을 수 없음.");
                return false;
            }

            if (!EconomyManager.Instance.TrySpendGold(node.cost)) return false;

            _unlockedIds.Add(nodeId);
            SaveUnlockedState();
            OnNodeUnlocked?.Invoke(nodeId);
            return true;
        }

        private void LoadNodes()
        {
            _nodesById.Clear();

            AddHubNodes(); // JSON을 못 읽어도 조직 허브와 캐릭터 노드는 항상 만들어 둠(아래 return 이전에 호출)

            var jsonAsset = Resources.Load<TextAsset>(JsonResourcePath);
            if (jsonAsset == null)
            {
                Debug.LogWarning($"[TechTreeManager] {JsonResourcePath}.json을 Resources에서 못 찾음.");
                AddCharacterUnlockNodes();
                return;
            }

            TechTreeFile parsed;
            try
            {
                parsed = JsonUtility.FromJson<TechTreeFile>(jsonAsset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TechTreeManager] TechTreeData.json 파싱 실패: {e.Message}");
                return;
            }

            if (parsed?.nodes != null)
            {
                int rootIndex = 0;
                foreach (var node in parsed.nodes)
                {
                    if (string.IsNullOrWhiteSpace(node.nodeId)) continue;

                    // 선행 조건 없는 스탯 노드(뿌리)는 어느 조직 허브에 붙일지 정해야 함 - JSON의 hubLine을 우선하고,
                    // 비어 있거나 없는 조직 이름이면 6개 허브에 돌아가며 배정함. 그 뒤 허브를 선행 조건으로 넣음
                    // (허브는 항상 열려 있어서 해금 흐름은 그대로).
                    if (node.prerequisiteNodeIds == null || node.prerequisiteNodeIds.Count == 0)
                    {
                        string line = node.hubLine;
                        if (string.IsNullOrEmpty(line) || Array.IndexOf(HubLines, line) < 0)
                        {
                            line = HubLines[rootIndex % HubLines.Length];
                        }
                        rootIndex++;
                        node.hubLine = line;
                        node.prerequisiteNodeIds = new List<string> { HubNodeId(line) };
                    }
                    _nodesById[node.nodeId] = node;
                }
            }

            AddCharacterUnlockNodes();
        }

        // 조직 허브 6개(천명회·흑연회·비선청·지하문·풍류단·화랑회) - 해금하는 노드가 아니라 캐릭터/스탯 노드를
        // 묶어주는 폴더 같은 노드. 코어에 연결되고 항상 열려 있음.
        private void AddHubNodes()
        {
            foreach (string line in HubLines)
            {
                string id = HubNodeId(line);
                _nodesById[id] = new TechNodeData
                {
                    nodeId = id,
                    displayName = line,
                    description = "조직",
                    cost = 0,
                    prerequisiteNodeIds = null,
                    hubLine = line,
                    isHub = true
                };
            }
        }

        // CharacterDatabase에 있는 캐릭터 수만큼 "캐릭터 해금 노드"를 자동 생성해서 같은 트리에 섞어 넣음.
        // JSON에 캐릭터를 직접 쓰지 않는 이유: 기획팀이 캐릭터를 추가/삭제할 때마다 TechTreeData.json도
        // 같이 손봐야 하는 이중 관리 부담을 없애기 위함 - 캐릭터 쪽 데이터(CharacterDataSO)만 늘어나면
        // 테크트리에도 자동으로 반영됨. 자기 조직 허브(depth 0)에 연결된 노드(depth 1)로 취급함.
        private void AddCharacterUnlockNodes()
        {
            foreach (var characterData in CharacterDatabase.GetAll())
            {
                string nodeId = CharacterNodeIdPrefix + characterData.characterId;
                _nodesById[nodeId] = new TechNodeData
                {
                    nodeId = nodeId,
                    displayName = characterData.displayName,
                    description = "캐릭터 해금",
                    cost = characterData.unlockCost,
                    linkedCharacterId = characterData.characterId
                };
                // 캐릭터는 자기 조직 허브에 연결됨(무소속 3명은 노드 트리 라인 기준: 강민준→천명회, 임서연→풍류단, 쇼타→지하문).
                // 알 수 없는 코드면 허브 없이 코어에 직접 연결됨(prerequisiteNodeIds 비움).
                string line = NodeTreeLines.GetLine(characterData.code);
                var node = _nodesById[nodeId];
                if (!string.IsNullOrEmpty(line))
                {
                    node.hubLine = line;
                    node.prerequisiteNodeIds = new List<string> { HubNodeId(line) };
                }
            }
        }

        /// <summary>
        /// [디버그 전용] 스탯 노드 해금 기록만 지움 (캐릭터 해금은 CharacterUnlockManager가 별도 관리하므로
        /// 여기선 안 건드림 - 전체 초기화는 DebugResetAllPurchases를 사용할 것).
        /// </summary>
        [ContextMenu("Debug: Reset Stat Unlocks")]
        public void DebugResetUnlocks()
        {
            _unlockedIds.Clear();
            SaveUnlockedState();
            OnNodeUnlocked?.Invoke(null);
        }

        /// <summary>
        /// [디버그 전용] 테스트를 위한 전체 초기화 - 스탯 노드 해금, 캐릭터 해금, 재화를 전부
        /// 시작 상태로 되돌림. TechTree 팝업의 "[테스트] 초기화" 버튼(TechTreeController.OnDebugResetClicked)에서
        /// 호출됨. QA 편의용 기능이므로 출시 전에 이 메서드와 관련 UI 버튼을 반드시 삭제할 것.
        ///
        /// 순서 주의: TechTreeController는 OnNodeUnlocked 이벤트를 받으면 그 자리에서 바로 화면을
        /// 다시 그림(BuildGrid) - 그 시점에 캐릭터 해금/재화가 아직 초기화 전이면 캐릭터 노드가
        /// 잠깐 예전 상태로 잘못 그려짐. 그래서 캐릭터/재화 초기화를 먼저 끝내고, 화면 갱신을
        /// 트리거하는 자기 자신의 DebugResetUnlocks()를 가장 마지막에 호출함.
        /// </summary>
        [ContextMenu("Debug: Reset All Purchases")]
        public void DebugResetAllPurchases()
        {
            CharacterUnlockManager.Instance?.DebugResetUnlocks();
            CharacterLevelManager.Instance?.DebugResetLevels();
            EconomyManager.Instance?.DebugResetCurrency();
            DebugResetUnlocks();
        }

        private void LoadUnlockedState()
        {
            _unlockedIds.Clear();
            string raw = PlayerPrefs.GetString(UnlockedKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;

            foreach (string id in raw.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(id)) _unlockedIds.Add(id);
            }
        }

        private void SaveUnlockedState()
        {
            PlayerPrefs.SetString(UnlockedKey, string.Join(",", _unlockedIds));
        }
    }
}
