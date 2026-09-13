using System;
using System.Collections.Generic;

namespace TowerDefense.Data
{
    // 노드트리(테크트리)는 캐릭터 해금(보관함/상점 쪽)과는 별개의 시스템 - 무료 재화(Gold)로
    // 여는 "강화용 그리드 노드"임. 캐릭터 데이터처럼 스프라이트 등 애셋 참조가 필요 없어서
    // ScriptableObject로 안 만들고 JSON을 런타임에 바로 읽어서 씀 (별도 임포터/에디터 툴 불필요).

    [Serializable]
    public class TechTreeFile
    {
        public List<TechNodeData> nodes;
    }

    [Serializable]
    public class TechNodeData
    {
        public string nodeId;
        public string displayName;
        public string description;

        public int cost; // Gold 고정 (노드트리는 무료 재화 전용, 기획 확정 사항)

        // 그리드 상의 칸 좌표 (정수). 실제 픽셀 위치 변환은 TechTreeController가 셀 크기를 곱해서 계산함.
        public int gridX;
        public int gridY;

        // 이 노드를 열려면 먼저 해금돼 있어야 하는 노드 id 목록. 비어있으면 최초 노드(선행 조건 없음).
        public List<string> prerequisiteNodeIds;

        // 비어있으면 순수 스탯 강화 노드. 값이 있으면 "캐릭터 해금 노드"라는 뜻이고, 이 필드는
        // CharacterDataSO.characterId를 가리킴 - 이런 노드는 TechTreeManager가 JSON이 아니라
        // CharacterDatabase를 보고 캐릭터 수만큼 런타임에 자동으로 만들어서 넣어줌(JSON엔 안 써도 됨).
        // 해금 여부/비용/통화(Gold or Gem)는 전부 CharacterUnlockManager/CharacterDataSO 쪽 값을 그대로 따름.
        public string linkedCharacterId;
    }
}
