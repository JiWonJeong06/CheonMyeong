using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// 인게임 성장(합성 별 + SP 강화)의 수치 규칙 - 천명_통합.xlsx '강화 테이블' / '기준값' / '캐릭터 스탯' 시트 기준.
    /// 서버(TowerUnit 스탯 계산, MatchController 강화 비용)와 UI(강화 패널 표시)가 같은 값을 보도록 한 곳에 모음.
    /// 이 파일의 숫자/식만 바꾸면 합성·강화 밸런스가 바뀌도록 로직(RPC/입력)과 분리해 둠(담당: 정지원 PART B 조정용).
    ///
    /// 규칙 요약
    /// - 강화: 캐릭터 종류별 Lv1~5(소환 직후 Lv1). 구간 가격 500/1000/2500/5000. 진행도 P = 구간 가격의 제곱근 누적 비율,
    ///   DPS 배율 D = 1 + 0.5 × P (Lv5 = 1.5배). 공격력형은 공격력 × D, 공속형은 Lv5에서 공격력 × D^0.6, 간격 × D^-0.4가 되도록 P에 따라 변함.
    /// - 합성: 같은 캐릭터 + 같은 별끼리 별 +1(최대 5★). 별당 증가량은 캐릭터 스탯 H·I열(CharacterDataSO.starAttackPower/starAttackInterval).
    /// - 최종 피해/간격 = (합성·강화 반영 값) × 노드 트리 계층 배율(TowerStatModifiers) × 날씨.
    /// </summary>
    public static class TowerProgression
    {
        public const int MaxStars = 5;
        public const int MaxEnhanceLevel = 5;

        private const float EnhanceDpsAtMax = 0.5f;       // 기준값 '공격력 강화 Lv5 (DPS)'
        private const float SpeedTypeDpsToAttack = 0.6f;   // 공속형: DPS 증가의 60%를 공격력으로
        private const float SpeedTypeDpsToInterval = 0.4f; // 40%를 공격 간격으로
        private const float MinInterval = 0.05f;

        // Lv1→2, 2→3, 3→4, 4→5 구간 가격(기준값 시트 - A안 확정)
        private static readonly int[] StepPrices = { 500, 1000, 2500, 5000 };

        // 레벨별 진행도 P(인덱스 0 = Lv1 → 0, Lv5 → 1). 정적 초기화 한 번만 계산해 매 호출 할당/계산이 없음.
        private static readonly float[] EnhanceProgress = BuildEnhanceProgress();

        private static float[] BuildEnhanceProgress()
        {
            var weights = new float[StepPrices.Length];
            float total = 0f;
            for (int i = 0; i < StepPrices.Length; i++)
            {
                weights[i] = Mathf.Sqrt(StepPrices[i]); // 효과 가중 지수 0.5
                total += weights[i];
            }

            var table = new float[MaxEnhanceLevel];
            table[0] = 0f;
            float cumulative = 0f;
            for (int level = 2; level <= MaxEnhanceLevel; level++)
            {
                cumulative += weights[level - 2];
                table[level - 1] = cumulative / total;
            }
            return table;
        }

        /// <summary>현재 강화 레벨에서 다음 레벨로 올리는 데 드는 기본 SP. 최대 레벨이면 -1.</summary>
        public static int GetEnhanceCost(int currentLevel)
        {
            currentLevel = Mathf.Clamp(currentLevel, 1, MaxEnhanceLevel);
            return currentLevel >= MaxEnhanceLevel ? -1 : StepPrices[currentLevel - 1];
        }

        /// <summary>강화 레벨의 DPS 배율(Lv1 = 1, Lv5 = 1.5).</summary>
        public static float GetEnhanceDpsMultiplier(int level)
        {
            return 1f + EnhanceDpsAtMax * EnhanceProgress[Mathf.Clamp(level, 1, MaxEnhanceLevel) - 1];
        }

        /// <summary>
        /// 합성 별과 강화 레벨을 반영한 "기본" 공격력/공격 간격(노드 트리 계층 배율과 날씨는 아직 안 곱함).
        /// 별 증가분은 캐릭터 데이터(H·I열)를 그대로 쓰고, 강화는 위 표의 배율을 적용함.
        /// </summary>
        public static void ComputeBaseStats(CharacterDataSO data, int stars, int enhanceLevel, out float damage, out float interval)
        {
            stars = Mathf.Clamp(stars, 1, MaxStars);
            float progress = EnhanceProgress[Mathf.Clamp(enhanceLevel, 1, MaxEnhanceLevel) - 1];

            damage = data.baseDamage + (stars - 1) * data.starAttackPower;
            interval = Mathf.Max(MinInterval, data.attackInterval + (stars - 1) * data.starAttackInterval);

            if (data.mergeType == MergeType.Speed)
            {
                // 엑셀 '강화 테이블' 공속형 열과 같은 식: Lv5 목표(공격력 D^0.6, 간격 D^-0.4)를 향해
                // 공격력은 진행도에 선형, 간격은 1 ÷ (1 + k × 진행도)로 변함(Lv5에서 정확히 목표값).
                float maxDps = 1f + EnhanceDpsAtMax;
                float attackAtMax = Mathf.Pow(maxDps, SpeedTypeDpsToAttack);
                float intervalAtMax = Mathf.Pow(maxDps, -SpeedTypeDpsToInterval);
                damage *= 1f + (attackAtMax - 1f) * progress;
                interval /= 1f + (1f / intervalAtMax - 1f) * progress;
            }
            else
            {
                damage *= 1f + EnhanceDpsAtMax * progress;
            }
        }
    }
}
