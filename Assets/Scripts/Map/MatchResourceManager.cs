using System;
using UnityEngine;
using Unity.Netcode;

namespace TowerDefense.Map
{
    /// <summary>
    /// 인게임(대전) SP 지갑. EconomyManager의 골드/보석과는 별개 자원임.
    ///
    /// [규칙 - 천명_통합.xlsx A7 기준] 시작 SP 500, 자동 충전 없음(몬스터 처치 보상으로만 증가, 상한 없음).
    /// 소환 가격은 100에서 시작해 소환할 때마다 +10(NextSummonCost). 소환 흐름은
    /// "TrySpendSP(NextSummonCost) → 성공 시 배치 → CommitSummon()"이고, 배치가 실패해서 환불(AddSP)한
    /// 경우에는 CommitSummon을 부르지 않아 가격이 오르지 않음. SP 강화/합성 비용도 같은 TrySpendSP를 씀.
    ///
    /// [네트워킹] SP는 승패에 직결되므로 서버 사본만 값을 바꿈(IsAuthoritative). 화면 표시용 값은
    /// MatchController의 NetworkVariable(BoardSpA/B, BoardSummonCostA/B)이 복제함 - 이 컴포넌트의
    /// 이벤트(OnSPChanged/OnSummonCostChanged)를 서버가 거기로 복사함.
    ///
    /// [1:1 대전] 보드마다 하나씩 존재하며 PlayerBoard가 참조를 들고 있음(싱글턴 아님).
    /// </summary>
    public class MatchResourceManager : MonoBehaviour
    {
        public const float StartingSP = 500f;
        public const int SummonBaseCost = 100;
        public const int SummonCostStep = 10;

        public float CurrentSP { get; private set; } = StartingSP;
        public int SummonCount { get; private set; }

        /// <summary>다음 소환에 필요한 SP (100 + 10 × 지금까지 소환 횟수).</summary>
        public int NextSummonCost => SummonBaseCost + SummonCostStep * SummonCount;

        public event Action<float> OnSPChanged;
        public event Action<int> OnSummonCostChanged;

        private bool IsAuthoritative => NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;

        /// <summary>[서버 전용] 소환/강화/합성 같은 SP 차감의 단일 진입점.</summary>
        public bool TrySpendSP(float amount)
        {
            if (!IsAuthoritative) return false; // 클라이언트 사본에서 직접 호출돼도 무시
            if (amount <= 0f || CurrentSP < amount) return false;
            CurrentSP -= amount;
            OnSPChanged?.Invoke(CurrentSP);
            return true;
        }

        /// <summary>[서버 전용] 처치 보상/환불용 SP 가산.</summary>
        public void AddSP(float amount)
        {
            if (!IsAuthoritative) return;
            if (amount <= 0f) return;
            CurrentSP += amount;
            OnSPChanged?.Invoke(CurrentSP);
        }

        // ===== SP 수입 보너스 (천희재 '수입': 30초마다 10초 동안 처치 SP +n%) =====
        private struct IncomeBuff
        {
            public object key;
            public float percent;
            public float expireAt;
        }
        private readonly System.Collections.Generic.List<IncomeBuff> _incomeBuffs = new(2);

        /// <summary>[서버 전용] 처치 SP 수입 +percent%를 duration초 동안 건다. 같은 key로 다시 걸면 갱신, 다른 key끼리는 합산.</summary>
        public void SetIncomeBonus(object key, float percent, float duration)
        {
            if (!IsAuthoritative || key == null) return;
            float expireAt = Time.time + duration;
            for (int i = 0; i < _incomeBuffs.Count; i++)
            {
                if (_incomeBuffs[i].key.Equals(key))
                {
                    _incomeBuffs[i] = new IncomeBuff { key = key, percent = percent, expireAt = expireAt };
                    return;
                }
            }
            _incomeBuffs.Add(new IncomeBuff { key = key, percent = percent, expireAt = expireAt });
        }

        /// <summary>지금 적용 중인 SP 수입 보너스 합(%). 만료된 항목은 이때 정리함.</summary>
        public float IncomeBonusPercent
        {
            get
            {
                float now = Time.time;
                float sum = 0f;
                for (int i = _incomeBuffs.Count - 1; i >= 0; i--)
                {
                    if (now >= _incomeBuffs[i].expireAt) _incomeBuffs.RemoveAt(i);
                    else sum += _incomeBuffs[i].percent;
                }
                return sum;
            }
        }

        /// <summary>[서버 전용] 몬스터 처치 보상 지급 - 수입 보너스를 곱해서 AddSP. (환불 같은 보상이 아닌 SP는 AddSP를 그대로 씀)</summary>
        public void AddRewardSP(float amount)
        {
            AddSP(amount * (1f + IncomeBonusPercent / 100f));
        }

        /// <summary>[서버 전용] 소환이 실제로 성공한 뒤 1회 호출 - 다음 소환 가격을 +10 올림.</summary>
        public void CommitSummon()
        {
            if (!IsAuthoritative) return;
            SummonCount++;
            OnSummonCostChanged?.Invoke(NextSummonCost);
        }
    }
}
