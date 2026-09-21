using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TowerDefense.Map
{
    /// <summary>
    /// 타워 배치 가능 영역을 관리하는 그리드. "배치 가능" 타일맵(디자이너가 칠해둔 레이어) 위에서만
    /// 타워를 지을 수 있고, 이미 다른 타워가 있는 칸엔 중복 배치가 안 됨.
    ///
    /// [1:1 대전 구조 확정에 따른 변경] 예전엔 Instance 싱글턴으로 "씬에 그리드 하나"를 가정했는데,
    /// 이제 대전 보드가 두 개(내 보드/상대 보드) 동시에 씬에 존재하므로 싱글턴을 없앰 - 대신
    /// PlayerBoard가 자기 보드의 PlacementGrid를 직접 들고 있다가 필요한 쪽(TowerPlacementController,
    /// MatchController)에 넘겨주는 방식으로 바뀜.
    ///
    /// [네트워킹 변경] 타워 인스턴스 생성/파괴는 더 이상 여기서 안 함 - Netcode로 여러 클라이언트에
    /// 같은 타워가 보이려면 반드시 서버에서 NetworkObject.Spawn()을 거쳐야 하는데, 그 책임은
    /// MatchController(서버 권위)가 가짐. 이 그리드는 "이 칸이 비어있는지/막혀있는지"만 판단하고
    /// 점유 여부만 기록하는 순수 로컬 자료구조로 역할을 좁힘(칸 점유 사실 자체는 모든 클라이언트가
    /// 각자 로컬로도 계산 가능한 정보라 네트워크 동기화가 따로 필요 없음 - 실제 타워 존재 여부는
    /// NetworkObject 스폰으로 이미 동기화되니까 이중 동기화를 피함).
    /// </summary>
    public class PlacementGrid : MonoBehaviour
    {
        [Tooltip("배치 가능한 칸에 타일이 칠해진 타일맵 (디자이너가 레벨 에디터에서 칠함)")]
        [SerializeField] private Tilemap buildableTilemap;

        [Tooltip("몬스터가 지나가는 길 타일맵 - 여기엔 타워를 못 지음. 비워두면 경로 차단 검사를 안 함.")]
        [SerializeField] private Tilemap pathTilemap;

        // 셀 좌표 -> 그 칸에 놓인 타워 인스턴스. 타워를 철거할 때 이 딕셔너리로 조회함.
        // 인스턴스 자체의 생성/파괴는 MatchController가 하고, 여기는 "어느 칸이 찼는지"만 기록함.
        private readonly Dictionary<Vector3Int, GameObject> _occupiedCells = new();

        private void Awake()
        {
            if (buildableTilemap == null)
            {
                Debug.LogWarning("[PlacementGrid] buildableTilemap이 연결 안 돼있음 - 인스펙터에서 연결할 것.");
            }
        }

        public Vector3Int WorldToCell(Vector3 worldPosition)
        {
            return buildableTilemap.WorldToCell(worldPosition);
        }

        public Vector3 GetCellCenterWorld(Vector3Int cell)
        {
            return buildableTilemap.GetCellCenterWorld(cell);
        }

        public bool IsOccupied(Vector3Int cell) => _occupiedCells.ContainsKey(cell);

        /// <summary>이 칸에 새로 타워를 지을 수 있는지 - 배치 가능 타일 위에 있고, 경로가 아니고, 비어있어야 함.</summary>
        public bool IsBuildable(Vector3Int cell)
        {
            if (buildableTilemap == null) return false;
            if (!buildableTilemap.HasTile(cell)) return false;
            if (pathTilemap != null && pathTilemap.HasTile(cell)) return false;
            if (IsOccupied(cell)) return false;
            return true;
        }

        /// <summary>
        /// [서버 전용] MatchController가 NetworkObject.Spawn()으로 타워를 실제로 만든 직후 호출함 -
        /// 이 칸을 점유 상태로 기록만 함(인스턴스 생성은 여기서 안 함).
        /// </summary>
        public void MarkOccupied(Vector3Int cell, GameObject towerInstance)
        {
            _occupiedCells[cell] = towerInstance;
        }

        /// <summary>[서버 전용] 타워가 철거/파괴됐을 때 칸 점유만 해제함(인스턴스 파괴는 호출 쪽 책임).</summary>
        public void ReleaseCell(Vector3Int cell)
        {
            _occupiedCells.Remove(cell);
        }
    }
}
