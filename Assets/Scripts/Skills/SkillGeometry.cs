using UnityEngine;

namespace TowerDefense.Skills
{
    /// <summary>스킬 범위 판정용 2D(XY) 기하 도우미 - 이 프로젝트는 2D라 z 차이는 무시함. 전부 할당 없음.</summary>
    public static class SkillGeometry
    {
        public static float SqrDistXY(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return dx * dx + dy * dy;
        }

        /// <summary>점 p에서 선분 a–b까지 거리의 제곱.</summary>
        public static float SqrDistanceToSegmentXY(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x;
            float aby = b.y - a.y;
            float lenSqr = abx * abx + aby * aby;
            float t = 0f;
            if (lenSqr > 0.000001f)
            {
                t = ((p.x - a.x) * abx + (p.y - a.y) * aby) / lenSqr;
                t = Mathf.Clamp01(t);
            }
            float cx = a.x + abx * t;
            float cy = a.y + aby * t;
            float dx = p.x - cx;
            float dy = p.y - cy;
            return dx * dx + dy * dy;
        }

        /// <summary>v를 degrees도 회전.</summary>
        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        /// <summary>from에서 to로 향하는 단위 방향(XY). 같은 위치면 오른쪽.</summary>
        public static Vector2 DirectionXY(Vector3 from, Vector3 to)
        {
            Vector2 d = new Vector2(to.x - from.x, to.y - from.y);
            float m = d.magnitude;
            return m > 0.0001f ? d / m : Vector2.right;
        }
    }
}
