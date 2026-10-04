namespace TowerDefense.Data
{
    /// <summary>
    /// 캐릭터 코드 → 노드 트리 라인(소속) 매핑 (엑셀 '노드 트리' 시트).
    /// 코드 구간: 1000 천명회 / 1500 흑연회 / 2000 비선청 / 2500 지하문 / 3000 풍류단 / 3500 화랑회 / 4000~ 무소속.
    /// 무소속 3명(강민준→천명회, 임서연→풍류단, 쇼타→지하문)은 라인에만 있고 소속 효과는 절대 받지 않음.
    /// </summary>
    public static class NodeTreeLines
    {
        public const string Cheonmyeonghoe = "천명회";
        public const string Heugyeonhoe = "흑연회";
        public const string Biseoncheong = "비선청";
        public const string Jihamun = "지하문";
        public const string Pungnyudan = "풍류단";
        public const string Hwarranghoe = "화랑회";

        /// <summary>노드 트리 라인 이름. 알 수 없는 코드면 null.</summary>
        public static string GetLine(int characterCode)
        {
            if (characterCode < 1000) return null;
            if (characterCode < 1500) return Cheonmyeonghoe;
            if (characterCode < 2000) return Heugyeonhoe;
            if (characterCode < 2500) return Biseoncheong;
            if (characterCode < 3000) return Jihamun;
            if (characterCode < 3500) return Pungnyudan;
            if (characterCode < 4000) return Hwarranghoe;

            switch (characterCode)
            {
                case 4000: return Cheonmyeonghoe; // 강민준
                case 4001: return Pungnyudan;     // 임서연
                case 4002: return Jihamun;        // 쇼타
                default: return null;
            }
        }

        /// <summary>소속 효과(소속 공격력/공속/강화 가격)를 받는지. 무소속(4000~)은 항상 false.</summary>
        public static bool ReceivesLineEffect(int characterCode) => characterCode >= 1000 && characterCode < 4000;
    }
}
