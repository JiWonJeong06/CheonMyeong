using UnityEngine;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 스킬 임시 연출(전용 이펙트 아트가 나오기 전 테스트 확인용). 반투명 원 하나를 잠시 띄웠다 지움.
    /// 원 스프라이트는 한 번만 만들어 재사용하고, 연출 오브젝트는 지속 시간 뒤 Destroy(스킬 발동 때만 생성되는 소량이라 풀링 불필요).
    /// 클라이언트 연출 전용이라 게임 판정에는 영향이 없음.
    /// </summary>
    public static class SkillVfx
    {
        private static Sprite s_circle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_circle = null;

        private static Sprite GetCircle()
        {
            if (s_circle != null) return s_circle;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    byte a = d <= 1f ? (byte)255 : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true); // 읽기 불필요 - CPU 사본 해제
            s_circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size); // 월드 크기 1
            return s_circle;
        }

        /// <summary>center 중심 반지름 radius(월드)의 반투명 원을 seconds 동안 표시.</summary>
        public static void SpawnZone(Vector3 center, float radius, float seconds, Color color)
        {
            var go = new GameObject("SkillZoneVfx");
            go.transform.position = new Vector3(center.x, center.y, center.z);
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetCircle();
            sr.color = new Color(color.r, color.g, color.b, 0.35f);
            sr.sortingOrder = 50;

            Object.Destroy(go, Mathf.Max(0.05f, seconds));
        }
    }
}
