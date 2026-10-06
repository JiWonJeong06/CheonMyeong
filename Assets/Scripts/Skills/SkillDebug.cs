using System.Diagnostics;

namespace TowerDefense.Skills
{
    /// <summary>스킬 진단 로그. 에디터에서만 컴파일됨(빌드에서는 호출·인자 계산까지 통째로 사라짐). 테스트 끝나면 호출부와 함께 지우면 됨.</summary>
    public static class SkillDebug
    {
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message) => UnityEngine.Debug.Log("[Skill][진단] " + message);
    }
}
