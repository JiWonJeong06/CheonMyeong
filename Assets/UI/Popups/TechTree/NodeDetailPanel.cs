using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using TowerDefense.Data;
using TowerDefense.Economy;

namespace TowerDefense.UI
{
    /// <summary>
    /// 테크트리에서 노드를 눌렀을 때 뜨는 상세창(오버레이). 씬/UXML에 따로 배치하지 않고 TechTreeController가
    /// 코드로 만들어서 팝업 루트에 붙임(스타일은 TechTree.uss의 node-detail-* 클래스).
    ///
    /// 보여주는 것:
    ///  - 아직 안 연 노드: 설명/효과, 해금 비용(보유량과 비교), 선행 노드 상태 + [해금] / [취소]
    ///  - 연 스탯 노드: 효과 설명 + "해금됨"
    ///  - 연 캐릭터 노드: 현재 레벨 → 다음 레벨의 공격력/공격 간격/DPS(현재값 → 변한 값), 레벨업 비용
    ///    (돌파 레벨이면 금화 + 태극 휘장) + [레벨업] / [닫기]
    /// 캐릭터 수치는 "노드 계층"(레벨 배율 + 노드 % 효과)만 반영한 값이고 합성/SP 강화는 매치 안에서 붙으므로 제외임.
    /// 버튼 콜백은 이 패널이 만들어질 때 한 번만 등록함(팝업을 닫으면 UIDocument가 트리를 통째로 버리므로
    /// 별도 해제 불필요 - 트리 밖으로 새는 구독이 없음).
    /// </summary>
    public class NodeDetailPanel : VisualElement
    {
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly VisualElement _body;
        private readonly Label _reasonLabel;
        private readonly Button _primaryButton;
        private readonly Button _closeButton;

        private TechNodeData _node;
        private Action _primaryAction;

        public bool IsShowing => style.display == DisplayStyle.Flex;

        public NodeDetailPanel()
        {
            AddToClassList("node-detail-overlay");
            style.display = DisplayStyle.None;

            var card = new VisualElement();
            card.AddToClassList("node-detail-card");
            Add(card);

            _title = new Label();
            _title.AddToClassList("node-detail-title");
            card.Add(_title);

            _subtitle = new Label();
            _subtitle.AddToClassList("node-detail-subtitle");
            card.Add(_subtitle);

            _body = new VisualElement();
            _body.AddToClassList("node-detail-body");
            card.Add(_body);

            _reasonLabel = new Label();
            _reasonLabel.AddToClassList("node-detail-reason");
            card.Add(_reasonLabel);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("button-row");
            card.Add(buttonRow);

            _primaryButton = new Button(OnPrimaryClicked) { text = string.Empty };
            _primaryButton.AddToClassList("app-button");
            buttonRow.Add(_primaryButton);

            _closeButton = new Button(Hide) { text = "닫기" };
            _closeButton.AddToClassList("app-button");
            buttonRow.Add(_closeButton);
        }

        public void ShowNode(TechNodeData node)
        {
            _node = node;
            style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            _node = null;
            _primaryAction = null;
        }

        /// <summary>해금/레벨업/재화 변화 뒤 같은 노드를 다시 그림(열려 있을 때만).</summary>
        public void RefreshIfShowing()
        {
            if (IsShowing && _node != null) Refresh();
        }

        private void OnPrimaryClicked() => _primaryAction?.Invoke();

        private void Refresh()
        {
            _body.Clear();
            _reasonLabel.text = string.Empty;
            _primaryAction = null;

            var tree = TechTreeManager.Instance;
            if (_node == null || tree == null) { Hide(); return; }

            bool isCharacter = !string.IsNullOrEmpty(_node.linkedCharacterId);
            bool unlocked = tree.IsUnlocked(_node.nodeId);

            _title.text = _node.displayName;

            if (isCharacter)
            {
                var character = CharacterDatabase.GetById(_node.linkedCharacterId);
                if (character == null)
                {
                    _subtitle.text = "캐릭터 노드";
                    AddText("캐릭터 데이터를 찾을 수 없습니다.");
                    SetPrimary(null, false);
                    return;
                }

                if (unlocked) BuildCharacterLevelView(character);
                else BuildLockedView(tree, character);
            }
            else
            {
                if (unlocked) BuildUnlockedStatView();
                else BuildLockedView(tree, null);
            }
        }

        #region 아직 안 연 노드

        private void BuildLockedView(TechTreeManager tree, CharacterDataSO character)
        {
            _subtitle.text = character != null ? $"캐릭터 해금 · {FactionText(character)}" : "스탯 노드 · 미해금";

            if (character != null)
            {
                // 해금 전에 어떤 캐릭터인지 알 수 있게 Lv1 기본 스탯을 보여줌.
                AddRow("공격력", FormatNumber(character.baseDamage), null);
                AddRow("공격 간격", $"{character.attackInterval:0.##}초", null);
                AddRow("사거리", $"{character.range:0.##}칸", null);
                if (character.isSupport) AddText("버프형 - 기본 공격 없이 스킬·궁극기만 사용");
            }
            else
            {
                string effect = DescribeEffect(_node);
                AddText(!string.IsNullOrEmpty(effect) ? effect : (!string.IsNullOrEmpty(_node.description) ? _node.description : "효과 수치 미정"));
            }

            // 비용
            GetUnlockCost(character, out string costName, out int cost, out int owned);
            AddRow("해금 비용", $"{costName} {cost:N0}", owned >= cost ? null : "부족");
            AddRow("보유", $"{costName} {owned:N0}", null);

            // 선행 노드
            string missing = MissingPrerequisiteNames(tree);
            bool prereqMet = string.IsNullOrEmpty(missing);
            if (!prereqMet) AddRow("선행 노드", missing, "미해금");

            bool affordable = owned >= cost;
            if (!prereqMet) _reasonLabel.text = "선행 노드를 먼저 해금하세요";
            else if (!affordable) _reasonLabel.text = $"{costName}이(가) 부족합니다";

            string nodeId = _node.nodeId;
            SetPrimary("해금", prereqMet && affordable, () =>
            {
                if (!TechTreeManager.Instance.TryUnlock(nodeId))
                {
                    ToastController.Instance?.Show("해금하지 못했습니다 (재화 부족)");
                }
                // 성공하면 TechTreeManager.OnNodeUnlocked → TechTreeController가 트리를 다시 그리고 이 패널도 갱신함.
            });
        }

        private void GetUnlockCost(CharacterDataSO character, out string costName, out int cost, out int owned)
        {
            var economy = EconomyManager.Instance;
            if (character != null)
            {
                bool gem = character.unlockCurrency == CurrencyType.Gem;
                costName = gem ? "보석" : "골드";
                cost = character.unlockCost;
                owned = economy != null ? (gem ? economy.Gem : economy.Gold) : 0;
            }
            else
            {
                costName = "골드";
                cost = _node.cost;
                owned = economy != null ? economy.Gold : 0;
            }
        }

        private string MissingPrerequisiteNames(TechTreeManager tree)
        {
            if (_node.prerequisiteNodeIds == null || _node.prerequisiteNodeIds.Count == 0) return null;

            var all = tree.GetAllNodes();
            var sb = new StringBuilder();
            foreach (string id in _node.prerequisiteNodeIds)
            {
                if (tree.IsUnlocked(id)) continue;
                string name = id;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].nodeId == id) { name = all[i].displayName; break; }
                }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(name);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        #endregion

        #region 연 스탯 노드

        private void BuildUnlockedStatView()
        {
            _subtitle.text = "스탯 노드 · 해금됨";
            string effect = DescribeEffect(_node);
            AddText(!string.IsNullOrEmpty(effect) ? effect : (!string.IsNullOrEmpty(_node.description) ? _node.description : "효과 수치 미정 (기획자 배치 후 결정)"));
            SetPrimary(null, false);
        }

        #endregion

        #region 연 캐릭터 노드 - 레벨업

        private void BuildCharacterLevelView(CharacterDataSO character)
        {
            var levels = CharacterLevelManager.Instance;
            int level = levels != null ? levels.GetLevel(character.characterId) : CharacterLevelTable.MinLevel;
            bool isMax = level >= CharacterLevelTable.MaxLevel;

            _subtitle.text = $"{FactionText(character)} · Lv {level} / {CharacterLevelTable.MaxLevel}";

            var current = NodeBonusCalculator.Compute(character, level);
            var next = isMax ? current : NodeBonusCalculator.Compute(character, level + 1);

            if (character.isSupport)
            {
                // 버프형은 기본 공격이 없어 공격력/DPS 수치가 의미 없음 - 배율만 보여줌.
                AddText("버프형 - 기본 공격 없이 스킬·궁극기만 사용");
                AddRow("공격력 배율", $"×{current.damageMultiplier:0.###}", isMax ? null : $"×{next.damageMultiplier:0.###}");
            }
            else
            {
                float curDamage = character.baseDamage * current.damageMultiplier;
                float nextDamage = character.baseDamage * next.damageMultiplier;
                float curInterval = character.attackInterval * current.attackIntervalMultiplier;
                float nextInterval = character.attackInterval * next.attackIntervalMultiplier;
                float curDps = curInterval > 0f ? curDamage / curInterval : 0f;
                float nextDps = nextInterval > 0f ? nextDamage / nextInterval : 0f;

                AddRow("공격력", FormatNumber(curDamage), isMax ? null : $"{FormatNumber(nextDamage)} ({FormatSigned(nextDamage - curDamage)})");
                AddRow("공격 간격", $"{curInterval:0.00}초", isMax ? null : $"{nextInterval:0.00}초 ({FormatSigned(nextInterval - curInterval, "0.00")})");
                AddRow("DPS", FormatNumber(curDps), isMax ? null : $"{FormatNumber(nextDps)} ({FormatSigned(nextDps - curDps)})");
            }
            AddText("합성·SP 강화 효과는 제외한 값입니다 (노드 레벨 + 노드 효과만 반영)");

            if (isMax)
            {
                _reasonLabel.text = "최대 레벨입니다";
                SetPrimary(null, false);
                return;
            }

            int gold = CharacterLevelTable.GetGoldCostToNext(level);
            int medal = CharacterLevelTable.GetMedalCostToNext(level);
            var economy = EconomyManager.Instance;
            int ownedGold = economy != null ? economy.Gold : 0;
            int ownedMedal = economy != null ? economy.Medal : 0;

            if (CharacterLevelTable.IsBreakthroughLevel(level)) AddText($"Lv {level} 돌파 - 금화와 태극 휘장이 필요합니다");

            AddRow($"Lv {level} → Lv {level + 1} 비용", $"금화 {gold:N0}", ownedGold >= gold ? null : "부족");
            if (medal > 0) AddRow(string.Empty, $"태극 휘장 {medal:N0}", ownedMedal >= medal ? null : "부족");
            AddRow("보유", medal > 0 ? $"금화 {ownedGold:N0} · 휘장 {ownedMedal:N0}" : $"금화 {ownedGold:N0}", null);

            bool affordable = ownedGold >= gold && ownedMedal >= medal;
            if (!affordable) _reasonLabel.text = ownedGold < gold ? "금화가 부족합니다" : "태극 휘장이 부족합니다";

            string characterId = character.characterId;
            SetPrimary("레벨업", affordable && levels != null, () =>
            {
                var manager = CharacterLevelManager.Instance;
                if (manager == null || !manager.TryLevelUp(characterId))
                {
                    ToastController.Instance?.Show("레벨업하지 못했습니다");
                    return;
                }
                // 성공하면 CharacterLevelManager.OnLevelChanged → TechTreeController가 트리와 이 패널을 갱신함.
            });
        }

        #endregion

        #region 표시 헬퍼

        private void SetPrimary(string text, bool enabled, Action action = null)
        {
            _primaryButton.style.display = text == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (text != null) _primaryButton.text = text;
            _primaryButton.SetEnabled(enabled);
            _primaryAction = enabled ? action : null;
        }

        // 한 줄: [라벨] [현재값] (→ [변한 값 / 경고]). change가 "부족"/"미해금"이면 경고색.
        private void AddRow(string label, string value, string change)
        {
            var row = new VisualElement();
            row.AddToClassList("node-detail-row");

            var labelElement = new Label(label);
            labelElement.AddToClassList("node-detail-row-label");
            row.Add(labelElement);

            var valueElement = new Label(value);
            valueElement.AddToClassList("node-detail-row-value");
            row.Add(valueElement);

            if (!string.IsNullOrEmpty(change))
            {
                bool warning = change == "부족" || change == "미해금";
                var changeElement = new Label(warning ? change : $"→ {change}");
                changeElement.AddToClassList(warning ? "node-detail-row-warning" : "node-detail-row-change");
                row.Add(changeElement);
            }

            _body.Add(row);
        }

        private void AddText(string text)
        {
            var label = new Label(text);
            label.AddToClassList("node-detail-text");
            _body.Add(label);
        }

        private static string FactionText(CharacterDataSO character)
        {
            string line = NodeTreeLines.GetLine(character.code);
            return string.IsNullOrEmpty(line) ? "소속 없음" : (NodeTreeLines.ReceivesLineEffect(character.code) ? line : $"{line} 라인 (무소속)");
        }

        private static string FormatNumber(float value) => value >= 100f ? value.ToString("0") : value.ToString("0.#");

        private static string FormatSigned(float value, string format = "0.#")
        {
            return value >= 0f ? "+" + value.ToString(format) : value.ToString(format);
        }

        // 노드 효과 한 줄 설명(수치가 비어 있으면 null). 값은 퍼센트 단위.
        private static string DescribeEffect(TechNodeData node)
        {
            var type = node.ParsedEffectType;
            if (type == NodeEffectType.None) return null;

            string v = node.effectValue.ToString("0.##");
            switch (type)
            {
                case NodeEffectType.LineAttackPercent: return $"{node.effectLine} 소속 공격력 +{v}% (무소속 제외)";
                case NodeEffectType.LineAttackSpeedPercent: return $"{node.effectLine} 소속 공격속도 +{v}% (무소속 제외)";
                case NodeEffectType.LineEnhancePriceDiscountPercent: return $"{node.effectLine} 소속 SP 강화 가격 -{v}% (무소속 제외)";
                case NodeEffectType.GlobalAttackPercent: return $"전체 공격력 +{v}% (무소속 포함)";
                default: return null;
            }
        }

        #endregion
    }
}
