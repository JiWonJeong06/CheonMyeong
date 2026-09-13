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
        private const int DummyStartGold = 1000;
        private const int DummyStartGem = 100;

        public int Gold { get; private set; }
        public int Gem { get; private set; }

        public event Action<int> OnGoldChanged;
        public event Action<int> OnGemChanged;

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
    }
}
