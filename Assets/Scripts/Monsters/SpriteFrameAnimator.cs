using UnityEngine;

namespace TowerDefense.Monsters
{
    /// <summary>
    /// 스프라이트 프레임을 일정 fps로 순환시키는 가벼운 클라이언트 전용 애니메이터.
    /// Animator/AnimationClip 대신 쓰는 이유: 몬스터는 오브젝트 풀링으로 많이 떠 있어서 Animator
    /// 인스턴스(컨트롤러/상태머신 평가)를 몬스터마다 두는 것보다, Update에서 시간만 누적해
    /// SpriteRenderer.sprite만 교체하는 쪽이 CPU/메모리 비용이 훨씬 낮음. 네트워크와 무관한 순수
    /// 시각 컴포넌트라(서버가 프레임까지 동기화하지 않음) 대역폭 비용도 없음.
    ///
    /// fps는 Unity Animation 창의 "Samples" 값과 같은 의미(초당 프레임 수)임 - 기획 전달 값
    /// (일반병 3 / 기마병 5 / 방패병 1)을 그대로 입력하면 됨.
    ///
    /// [풀링 대응] 풀에서 재사용될 때마다 OnEnable이 불리므로 거기서 상태를 초기화함 - 이벤트
    /// 구독이 없어서 누수 걱정이 없음. 시작 프레임을 랜덤으로 줘서 같은 종류 몬스터 여럿이 똑같은
    /// 동작으로 일제히 움직여 보이는 걸 방지함.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteFrameAnimator : MonoBehaviour
    {
        [Tooltip("걷기 프레임 순서대로 (Sprite Editor로 자른 스프라이트를 순서대로 드래그)")]
        [SerializeField] private Sprite[] frames;

        [Tooltip("초당 프레임 수(Animation 창의 Samples와 동일 의미). 일반병 3 / 기마병 5 / 방패병 1")]
        [SerializeField, Min(0.01f)] private float framesPerSecond = 3f;

        [Tooltip("켜면 활성화될 때마다 시작 프레임을 랜덤으로 잡아 여러 마리가 동기화돼 보이지 않게 함")]
        [SerializeField] private bool randomizeStartFrame = true;

        [Tooltip("켜면 이동 방향이 왼쪽일 때 스프라이트를 좌우 반전(원본 아트는 오른쪽을 보는 모습)")]
        [SerializeField] private bool flipByMoveDirection = true;

        // 한 프레임에 이만큼 이상 움직였다면 이동이 아니라 풀 재사용/스폰 순간이동으로 보고 방향 판정에서 제외함
        private const float TeleportThreshold = 1f;
        private const float FlipDeadZone = 0.0005f;

        private SpriteRenderer _renderer;
        private float _timer;
        private int _index;
        private float _lastX;

        private void Awake() => _renderer = GetComponent<SpriteRenderer>();

        private void OnEnable()
        {
            _timer = 0f;
            _index = (randomizeStartFrame && frames != null && frames.Length > 0)
                ? Random.Range(0, frames.Length)
                : 0;
            _lastX = transform.position.x;
            ApplyFrame();
        }

        private void Update()
        {
            if (frames != null && frames.Length > 1)
            {
                _timer += Time.deltaTime;
                float interval = 1f / framesPerSecond;
                if (_timer >= interval)
                {
                    // 프레임 시간이 길 때(방패병 1fps)도, 프레임 드롭 시에도 밀린 만큼 한 번에 따라잡음
                    int steps = (int)(_timer / interval);
                    _timer -= steps * interval;
                    _index = (_index + steps) % frames.Length;
                    ApplyFrame();
                }
            }

            if (flipByMoveDirection)
            {
                float x = transform.position.x;
                float dx = x - _lastX;
                _lastX = x;
                if (Mathf.Abs(dx) > FlipDeadZone && Mathf.Abs(dx) < TeleportThreshold)
                    _renderer.flipX = dx < 0f;
            }
        }

        private void ApplyFrame()
        {
            if (frames == null || frames.Length == 0) return;
            _renderer.sprite = frames[_index];
        }
    }
}
