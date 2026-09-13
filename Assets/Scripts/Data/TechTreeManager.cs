using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TowerDefense.Economy;

namespace TowerDefense.Data
{
    /// <summary>
    /// 노드트리(강화) 더미 매니저 - Resources/Data/TechTreeData.json을 읽어서 노드 목록/선행조건을
    /// 갖고 있고, 어떤 노드가 해금됐는지는 CharacterUnlockManager와 같은 방식(PlayerPrefs,
    /// 쉼표로 이어붙인 id 목록)으로 저장함. 항상 Gold(무료 재화)만 소모함 - 노드트리는
    /// 인게임이 아니라 메인화면에서만 조작되므로 소급 적용 로직이 필요 없음.
    /// </summary>
    public class TechTreeManager : MonoBehaviour
    {
        public static TechTreeManager Instance { get; private set; }

        private const string JsonResourcePath = "Data/TechTreeData"; // 확장자 제외, Resources 기준 상대경로
        private const string UnlockedKey = "TechTree_UnlockedIds";

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

        public bool IsUnlocked(string nodeId) => _unlockedIds.Contains(nodeId);

        public bool ArePrerequisitesMet(string nodeId)
        {
            if (!_nodesById.TryGetValue(nodeId, out var node)) return false;
            if (node.prerequisiteNodeIds == null || node.prerequisiteNodeIds.Count == 0) return true;
            return node.prerequisiteNodeIds.All(_unlockedIds.Contains);
        }

        public bool TryUnlock(string nodeId)
        {
            if (!_nodesById.TryGetValue(nodeId, out var node)) return false;
            if (IsUnlocked(nodeId)) return false;
            if (!ArePrerequisitesMet(nodeId)) return false;

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

            var jsonAsset = Resources.Load<TextAsset>(JsonResourcePath);
            if (jsonAsset == null)
            {
                Debug.LogWarning($"[TechTreeManager] {JsonResourcePath}.json을 Resources에서 못 찾음.");
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

            if (parsed?.nodes == null) return;

            foreach (var node in parsed.nodes)
            {
                if (string.IsNullOrWhiteSpace(node.nodeId)) continue;
                _nodesById[node.nodeId] = node;
            }
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
