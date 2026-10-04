using UnityEngine;
using TowerDefense.UI;

namespace TowerDefense.Map
{
    /// <summary>
    /// 인게임 화면 오른쪽에 항상 떠있는 덱 패널(InGameDeck, Assets/UI/Screens/InGame/InGameDeck.uss의
    /// .deck-root width: 160px)이 화면 오른쪽 폭을 차지하기 때문에, 메인 카메라가 그 영역까지 그대로
    /// 그리면 보드가 덱 패널 밑에 깔려서 시각적으로 겹침. 그래서 카메라 뷰포트를 왼쪽 (1 - 예약비율)
    /// 만큼만 그리게 줄임.
    ///
    /// [카메라 위치는 옮기지 않음 - 이전에 왼쪽으로 밀었던 게 오히려 버그였음] 처음엔 "잃어버리는
    /// 가로 시야를 전부 오른쪽에서만 잘라내자"는 생각으로 카메라를 왼쪽으로 lostWorldWidth*0.5만큼
    /// 밀었는데, 그러면 보드가 (덱 폭만큼 줄어든) 실제 뷰포트 안에서 중앙이 아니라 오른쪽으로
    /// 치우쳐 보임 - 뷰포트 밖 절대 화면 좌표 기준으로는 그대로였지만, 화면 위쪽 일시정지 버튼은
    /// InGameHUD.uss의 .hud-root(마찬가지로 right: 160px)가 flex-direction: row + justify-content:
    /// space-between 가운데 자식이라 이미 "줄어든 뷰포트의 중앙"에 정확히 떠 있었음. 그래서 보드와
    /// 일시정지 버튼이 서로 다른 기준점(전체 화면 중앙 vs 줄어든 뷰포트 중앙)으로 정렬돼 어긋났음.
    /// 카메라 위치를 전혀 옮기지 않고 rect만 줄이면(대칭으로 좌우 시야가 똑같이 줄어듦), 원래
    /// 카메라가 보드 중앙을 조준하고 있었다는 전제하에 보드 중앙이 항상 "줄어든 뷰포트의 중앙"에
    /// 오게 되므로 일시정지 버튼과 자동으로 정렬됨 - 별도 보정 계산이 필요 없어짐.
    ///
    /// [예약 비율을 하드코딩하지 않는 이유] 처음엔 PanelSettings의 Reference Resolution(1200)을
    /// 근거로 "덱 폭 160은 항상 화면의 160/1200=13.33%"라고 계산해서 썼는데, 실제로 Play해보니
    /// 이 비율이 실측(덱 패널이 실제로 화면에서 차지하는 폭)과 안 맞았음(Scale With Screen Size의
    /// 실제 스케일 계산이 이론 계산과 다르게 나옴). 그래서 지금은 이론 계산을 버리고, 덱 패널 쪽
    /// (InGameDeckController)이 자기 UI 레이아웃이 실제로 해석된 뒤 측정해서 넘겨주는 값
    /// (InGameDeckLayout.ReservedScreenFraction/OnReservedFractionMeasured)만 신뢰함 - 실측이라
    /// 해상도/화면비/DPI가 뭐든 항상 정확함.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class InGameCameraLayout : MonoBehaviour
    {
        private Camera _camera;
        private bool _applied;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            InGameDeckLayout.OnReservedFractionMeasured += OnReservedFractionMeasured;
        }

        private void OnDisable()
        {
            InGameDeckLayout.OnReservedFractionMeasured -= OnReservedFractionMeasured;
        }

        private void Start()
        {
            // 덱 패널 쪽이 카메라보다 먼저 측정을 끝냈을 수도 있음 - 그 경우 이벤트를 기다릴 필요 없이
            // 이미 저장된 값을 바로 씀(초기화 순서가 보장 안 되므로 둘 다 커버해야 함, 클래스 doc 참고).
            if (InGameDeckLayout.ReservedScreenFraction >= 0f)
            {
                ApplyLayout(InGameDeckLayout.ReservedScreenFraction);
            }
        }

        private void OnReservedFractionMeasured(float fraction)
        {
            ApplyLayout(fraction);
        }

        private void ApplyLayout(float reservedFraction)
        {
            if (_applied) return; // 덱 폭은 런타임 중 안 바뀌므로 최초 1회만 적용하면 충분함
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera == null) return;

            reservedFraction = Mathf.Clamp01(reservedFraction);
            float viewportWidth = 1f - reservedFraction;

            if (!_camera.orthographic)
            {
                Debug.LogWarning("[InGameCameraLayout] 오소그래픽 카메라 기준으로 계산됨 - 퍼스펙티브 카메라에는 안 맞을 수 있음.");
            }

            // rect만 줄이고 카메라 위치는 절대 건드리지 않음 - 좌우 시야가 대칭으로 줄어들어서
            // 카메라가 원래 조준하던 보드 중앙이 항상 줄어든 뷰포트의 중앙에 그대로 남음
            // (일시정지 버튼도 같은 폭만큼 줄어든 영역의 중앙에 뜨므로 자동으로 정렬됨 - 클래스 doc 참고).
            _camera.rect = new Rect(0f, 0f, viewportWidth, 1f);

            _applied = true;

            Debug.Log($"[InGameCameraLayout] 덱 패널 실측 비율({reservedFraction:P1}) 확보 - 뷰포트 폭 {viewportWidth:F4} (카메라 위치는 그대로 둠).");
        }
    }
}
