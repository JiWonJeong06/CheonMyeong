using System;
using UnityEngine;

namespace TowerDefense.Economy
{
    /// <summary>
    /// 더미 버전 재화 매니저 — 골드(무료)/보석(유료) 두 가지를 PlayerPrefs로 임시 저장.
    /// 나중에 보유 캐릭터/스킨/테크노드까지 포함하는 정식 SaveData 스키마가 나오면
    /// 저장 방식만 그쪽으로 교체하면 되고, TrySpend/Add 인터페이스는 그대로 재사용 가능하도록 설계함.
    /// SP(인게임 전용 재화)는 여기서 다루지 않음 — 캐릭터/웨이브 팀 담당.
    /// </summary>
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        private const string GoldKey = "Economy_Gold";
        private const string GemKey = "Economy_Gem";
        private const string MedalKey = "Economy_Medal"; // 태극 휘장 - 무료 특별 강화 재화(캐릭터 레벨 돌파용)
        // [테스트용 임시 상향] 캐릭터 5종 전체 해금 비용 합(600) + 테크트리 등 다른 소모처를 감안해
        // 넉넉하게 잡음 - 실제 밸런스 확정 전 더미값이라 최종 출시 전에 반드시 재조정할 것.
        private const int DummyStartGold = 99999;
        private const int DummyStartGem = 99999;
        private const int DummyStartMedal = 99999; // [테스트용 임시] 돌파 비용(10/20/40/80)이 확정되기 전 더미값

        public int Gold { get; private set; }
        public int Gem { get; private set; }
        /// <summary>태극 휘장(무료 특별 강화 재화).</summary>
        public int Medal { get; private set; }

        public event Action<int> OnGoldChanged;
        public event Action<int> OnGemChanged;
        public event Action<int> OnMedalChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject); // 메인화면 <-> 인게임 씬 전환 중에도 재화 유지

            Gold = PlayerPrefs.GetInt(GoldKey, DummyStartGold);
            Gem = PlayerPrefs.GetInt(GemKey, DummyStartGem);
            Medal = PlayerPrefs.GetInt(MedalKey, DummyStartMedal);
        }

        public bool TrySpendGold(int amount)
        {
            if (amount <= 0 || Gold < amount) return false;
            Gold -= amount;
            PlayerPrefs.SetInt(GoldKey, Gold);
            OnGoldChanged?.Invoke(Gold);
            return true;
        }

        public bool TrySpendGem(int amount)
        {
            if (amount <= 0 || Gem < amount) return false;
            Gem -= amount;
            PlayerPrefs.SetInt(GemKey, Gem);
            OnGemChanged?.Invoke(Gem);
            return true;
        }

        public bool TrySpendMedal(int amount)
        {
            if (amount <= 0 || Medal < amount) return false;
            Medal -= amount;
            PlayerPrefs.SetInt(MedalKey, Medal);
            OnMedalChanged?.Invoke(Medal);
            return true;
        }

        public void AddMedal(int amount)
        {
            if (amount <= 0) return;
            Medal += amount;
            PlayerPrefs.SetInt(MedalKey, Medal);
            OnMedalChanged?.Invoke(Medal);
        }

        public void AddGold(int amount)
        {
            if (amount <= 0) return;
            Gold += amount;
            PlayerPrefs.SetInt(GoldKey, Gold);
            OnGoldChanged?.Invoke(Gold);
        }

        public void AddGem(int amount)
        {
            if (amount <= 0) return;
            Gem += amount;
            PlayerPrefs.SetInt(GemKey, Gem);
            OnGemChanged?.Invoke(Gem);
        }

        /// <summary>
        /// [디버그 전용] 재화를 더미 시작값으로 되돌림 - QA 테스트 중 해금/구매를 반복 확인하기 위한 기능.
        /// TechTree의 "[테스트] 초기화" 버튼(TechTreeManager.DebugResetAllPurchases)에서 호출됨.
        /// 출시 전에 호출부(UI 버튼 포함)와 함께 반드시 제거할 것.
        /// </summary>
        [ContextMenu("Debug: Reset Currency")]
        public void DebugResetCurrency()
        {
            Gold = DummyStartGold;
            Gem = DummyStartGem;
            Medal = DummyStartMedal;
            PlayerPrefs.SetInt(GoldKey, Gold);
            PlayerPrefs.SetInt(GemKey, Gem);
            PlayerPrefs.SetInt(MedalKey, Medal);
            OnGoldChanged?.Invoke(Gold);
            OnGemChanged?.Invoke(Gem);
            OnMedalChanged?.Invoke(Medal);
        }
    }
}
