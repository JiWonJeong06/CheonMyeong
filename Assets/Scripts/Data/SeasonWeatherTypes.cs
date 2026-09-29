using System.Collections.Generic;

namespace TowerDefense.Data
{
    /// <summary>계절 4종(천명.pptx 슬라이드 12 "SEASON SYSTEM" 확정) - 매치 시작 시 서버가 1개를
    /// 랜덤으로 고르고 한 판 내내 유지함(MatchController.SelectedSeason 참고).</summary>
    public enum SeasonType
    {
        Spring, // 봄
        Summer, // 여름
        Fall,   // 가을
        Winter, // 겨울
    }

    /// <summary>날씨 11종(맑음 포함, 천명.pptx 슬라이드 13 "SEASON & WEATHER" 확정) - 웨이브마다
    /// 서버가 그 계절의 후보(맑음 + 계절 전용 2종 + 공용 2종) 중 1개를 랜덤으로 고름
    /// (MatchController.RequestWeatherForWave 참고).</summary>
    public enum WeatherType
    {
        Clear,          // 맑음 - 기본값, 효과 없음
        Pollen,         // 꽃가루 (봄, 불리)
        FineDust,       // 미세먼지 (봄, 불리)
        Flood,          // 홍수 (여름, 유리)
        Heatwave,       // 폭염 (여름, 유리)
        FallenLeaves,   // 낙엽 (가을, 불리) - 시야 연출 전용, 수치 효과 없음
        Fog,            // 안개 (가을, 불리)
        Snow,           // 눈 (겨울, 불리)
        Hail,           // 우박 (겨울, 유리)
        Rain,           // 비 (공용)
        Wind,           // 바람 (공용)
    }

    /// <summary>
    /// 계절/날씨 수치 효과 테이블(천명.pptx 슬라이드 12/13 확정치 - 2026-09-29 pymupdf 4배율 고해상도
    /// 렌더 2회로 markitdown 추출과 교차 검증 완료, 사용자에게 3개 미확정 항목만 별도 확인받음).
    ///
    /// [ScriptableObject 대신 코드 상수로 관리하는 이유] MonsterDataSO/CharacterDataSO와 달리 날씨는
    /// 종류가 11개로 고정돼 있고 전부 확정 수치라(기획자가 인스턴스별로 값을 바꿀 일이 없음),
    /// 에셋 11개를 만들고 관리하는 오버헤드가 이 경우엔 득보다 실임 - 값이 바뀌면 이 파일만 고치면 됨.
    ///
    /// [사용자에게 확인받아 확정한 3가지 - 2026-09-29]
    /// 1) 홍수 넉백: 1회성(날씨가 뜨는 순간 그 보드에 살아있는 몬스터 전원에게 딱 한 번 적용,
    ///    이후 지속 적용 아님). 정확한 거리는 기획서에 없어 "웨이포인트 1개 뒤로" 되돌리는 것으로
    ///    잠정 구현함(밸런스 확정 필요 - MonsterPathFollower.ApplyKnockback 참고).
    /// 2) 비 보호막: 몬스터 최대체력의 10%.
    /// 3) 낙엽 시야 가림: 순수 시각 연출(게임플레이 수치 영향 없음) - InGameHUDController의
    ///    비전 블록 오버레이 참고.
    /// </summary>
    public static class WeatherEffectTable
    {
        // 계절별 전용 날씨 후보(2개씩, 천명.pptx 슬라이드 12 확정).
        private static readonly Dictionary<SeasonType, WeatherType[]> SeasonWeathers = new()
        {
            { SeasonType.Spring, new[] { WeatherType.Pollen, WeatherType.FineDust } },
            { SeasonType.Summer, new[] { WeatherType.Flood, WeatherType.Heatwave } },
            { SeasonType.Fall,   new[] { WeatherType.FallenLeaves, WeatherType.Fog } },
            { SeasonType.Winter, new[] { WeatherType.Snow, WeatherType.Hail } },
        };

        private static readonly WeatherType[] SharedWeathers = { WeatherType.Rain, WeatherType.Wind };

        /// <summary>이번 웨이브에 굴릴 날씨 후보 전체(맑음 + 이 계절 전용 2종 + 공용 2종, 총 5개) -
        /// MatchController.RequestWeatherForWave가 이 배열에서 균등 랜덤으로 하나를 뽑음
        /// ("기본 날씨 맑음을 포함하여, 그 계절의 날씨 2종 + 공용 날씨 2종 중 1종이 발생" - 슬라이드 12 원문).</summary>
        public static WeatherType[] GetCandidates(SeasonType season)
        {
            var seasonal = SeasonWeathers[season];
            return new[] { WeatherType.Clear, seasonal[0], seasonal[1], SharedWeathers[0], SharedWeathers[1] };
        }

        // ---- 타워(아군) 쪽 수치 - TowerUnit이 매 공격/타겟팅 시점마다 현재 날씨(MatchController.
        // CurrentWeather, 서버가 웨이브마다 갱신하는 전역값)를 그대로 읽어 적용함(타워는 몬스터와
        // 달리 웨이브 경계를 넘어 계속 존재하므로 "스폰 시점 고정"이 아니라 항상 최신값을 읽어야 함). ----

        /// <summary>공격속도 배율(초당 공격 횟수에 곱함) - 미세먼지 10% 감소, 바람 20% 증가.</summary>
        public static float AttackSpeedMultiplier(WeatherType w) => w switch
        {
            WeatherType.FineDust => 0.9f,
            WeatherType.Wind => 1.2f,
            _ => 1f,
        };

        /// <summary>공격력 배율 - 폭염 15% 증가.</summary>
        public static float AttackDamageMultiplier(WeatherType w) => w switch
        {
            WeatherType.Heatwave => 1.15f,
            _ => 1f,
        };

        /// <summary>사거리 가감(칸/유닛 단위, CharacterDataSO.range에 더함) - 안개 1 감소.</summary>
        public static float RangeDelta(WeatherType w) => w switch
        {
            WeatherType.Fog => -1f,
            _ => 0f,
        };

        // ---- 몬스터 쪽 수치 - MonsterPathFollower가 스폰 시점(Init)에 그 웨이브의 날씨를 한 번만
        // 받아 고정해서 씀(_spawnWeather) - 몬스터는 웨이브 하나 안에서만 살아있다 죽거나 다음
        // 보드로 넘어가므로(보스 처치 대기 후에만 다음 웨이브 진행), 스폰 시점 고정과 실시간 조회가
        // 실질적으로 항상 같은 값이라 매 프레임 전역값을 조회할 필요가 없어 더 가벼움. ----

        /// <summary>이동속도 배율 - 눈 15% 증가.</summary>
        public static float MoveSpeedMultiplier(WeatherType w) => w switch
        {
            WeatherType.Snow => 1.15f,
            _ => 1f,
        };

        /// <summary>초당 최대체력 대비 도트 데미지 비율(0.03 = 3%) - 우박 전용.</summary>
        public static float DotPercentPerSecond(WeatherType w) => w switch
        {
            WeatherType.Hail => 0.03f,
            _ => 0f,
        };

        /// <summary>체력이 최대체력의 50% 미만인 몬스터를 발견 즉시 풀피로 1회 회복시키는 날씨인지 - 꽃가루 전용.</summary>
        public static bool HasHealBelowHalfEffect(WeatherType w) => w == WeatherType.Pollen;

        /// <summary>스폰 시 최대체력 대비 이 비율만큼 보호막을 부여하는 날씨(0.1 = 10%, 사용자 확인치) - 비 전용.</summary>
        public static float ShieldFractionOnSpawn(WeatherType w) => w == WeatherType.Rain ? 0.1f : 0f;

        /// <summary>날씨가 뜨는 순간 그 보드에 살아있는 몬스터 전원에게 1회성 넉백을 적용하는지 - 홍수 전용(사용자 확인: 1회성).</summary>
        public static bool HasOneTimeKnockbackOnStart(WeatherType w) => w == WeatherType.Flood;

        /// <summary>순수 시각 연출(시야 가림)이 있는 날씨인지와 지속시간(초) - 낙엽 전용, 게임플레이 수치 영향 없음(사용자 확인).</summary>
        public static bool HasVisionBlockEffect(WeatherType w, out float durationSeconds)
        {
            durationSeconds = w == WeatherType.FallenLeaves ? 7f : 0f;
            return w == WeatherType.FallenLeaves;
        }
    }
}
