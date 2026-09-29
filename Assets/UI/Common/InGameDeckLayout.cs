using System;

namespace TowerDefense.UI
{
    /// <summary>
    /// InGameDeckController가 실제 UI Toolkit 레이아웃이 해석된 뒤(첫 GeometryChangedEvent) 측정한
    /// "덱 패널(.deck-root)이 화면에서 실제로 차지하는 비율"을, 카메라 레이아웃 보정을 담당하는
    /// TowerDefense.Map.InGameCameraLayout에 전달하는 정적 브릿지.
    ///
    /// [왜 비율을 하드코딩(예: 160/1200)하지 않고 매번 측정하는가] PanelSettings의 Scale With
    /// Screen Size 스케일 계산이 실제로 정확히 어떤 값을 만들어내는지(해상도/화면비/DPI 조합에 따라)
    /// 코드만 보고 미리 정확히 예측하기 어려움이 실제로 확인됨 - 그래서 값을 미리 계산해서 넘기는
    /// 대신, 덱 패널의 최종 레이아웃 결과(.deck-root.resolvedStyle.width)와 패널 전체 폭
    /// (panel.visualTree.resolvedStyle.width)의 "비율"만 씀. 패널 공간 -> 실제 화면 픽셀 변환은
    /// 항상 균일한(선형) 스케일이라, 이 비율 자체는 스케일 계수를 몰라도 화면 비율과 정확히 같음
    /// (스케일 계수가 뭐든 상쇄됨) - 그래서 해상도/화면비가 달라져도 항상 정확함.
    ///
    /// [왜 Map 레이어(InGameCameraLayout)가 UI 쪽을 직접 참조하지 않고 이 브릿지를 거치는가]
    /// MatchController.OnMatchEnded 등 기존 컨벤션과 동일 - "Map 레이어가 UI를 직접 참조하지 않도록
    /// 이벤트로만 알려주고, 실제로 뭘 할지는 구독하는 쪽 책임으로 남겨둠"는 원칙을 그대로 따름.
    ///
    /// [초기화 순서 문제 없음] InGameDeckController와 InGameCameraLayout은 서로 다른 GameObject라
    /// 어느 쪽이 먼저 Awake/OnEnable/Start가 도는지 보장이 안 됨. 그래서 값은 정적 필드
    /// (ReservedScreenFraction)로도 남겨두고, 이벤트(OnReservedFractionMeasured)로도 알림 - 카메라
    /// 쪽이 자기 Start()보다 먼저 측정이 끝났으면 정적 필드를 바로 읽고, 아직이면 OnEnable에서
    /// 미리 구독해둔 이벤트로 나중에 받음(둘 중 어느 순서로 실행되든 안전).
    /// </summary>
    public static class InGameDeckLayout
    {
        /// <summary>아직 측정 전이면 -1. 덱 패널이 화면 오른쪽에서 실제로 차지하는 비율(0~1).</summary>
        public static float ReservedScreenFraction { get; private set; } = -1f;

        public static event Action<float> OnReservedFractionMeasured;

        public static void ReportReservedFraction(float fraction)
        {
            ReservedScreenFraction = fraction;
            OnReservedFractionMeasured?.Invoke(fraction);
        }
    }
}
