using UnityEngine;
using TowerDefense.Data;

namespace TowerDefense.UI
{
    /// <summary>
    /// 노드 트리 조직(라인)별 색 - 엑셀 '조직' 시트의 대표색(천명회 파랑 / 흑연회 검정 / 비선청 초록 / 지하문 빨강 /
    /// 풍류단 하양 / 화랑회 갈색)을 어두운 배경(노드 트리 캔버스)에서 읽히게 조정한 값. 스탯 노드는 조직색이 없는
    /// "무색"이라 여기서 다루지 않음. 색을 바꾸고 싶으면 이 파일만 고치면 됨.
    /// </summary>
    public static class NodeTreeLineColors
    {
        /// <summary>허브 채움색 / 캐릭터 노드 테두리·연결선 색. 알 수 없는 라인이면 false.</summary>
        public static bool TryGet(string line, out Color fill, out Color ring)
        {
            switch (line)
            {
                case NodeTreeLines.Cheonmyeonghoe: fill = Rgb(45, 95, 190);  ring = Rgb(110, 165, 255); return true; // 파랑
                case NodeTreeLines.Heugyeonhoe:    fill = Rgb(28, 28, 32);   ring = Rgb(135, 135, 150); return true; // 검정(배경과 구분되도록 회색 테두리)
                case NodeTreeLines.Biseoncheong:   fill = Rgb(40, 130, 70);  ring = Rgb(105, 220, 130); return true; // 초록
                case NodeTreeLines.Jihamun:        fill = Rgb(175, 45, 45);  ring = Rgb(255, 120, 120); return true; // 빨강
                case NodeTreeLines.Pungnyudan:     fill = Rgb(215, 215, 220); ring = Rgb(255, 255, 255); return true; // 하양
                case NodeTreeLines.Hwarranghoe:    fill = Rgb(125, 85, 50);  ring = Rgb(205, 150, 100); return true; // 갈색
                default:
                    fill = Color.clear; ring = Color.clear; return false;
            }
        }

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
