using System.Collections.Generic;

namespace TowerDefense.Monsters
{
    /// <summary>
    /// 현재 씬에 살아있는(활성 상태인) 몬스터 목록을 들고 있는 정적 레지스트리.
    /// 타워(TowerUnit)가 매 프레임 타겟을 찾을 때 FindObjectsOfType&lt;MonsterPathFollower&gt;()를
    /// 쓰면 씬에 몬스터가 많아질수록 비용이 커지고 GC 압박도 생기므로, 대신 몬스터 스스로
    /// OnEnable/OnDisable에서 이 레지스트리에 등록/해제하는 방식(관찰자 패턴에 가까움)을 씀.
    ///
    /// 오브젝트 풀링과 함께 쓰이므로(MonsterSpawner) SetActive(false)로 반납될 때 OnDisable이
    /// 불려서 자동으로 빠지고, 풀에서 다시 꺼내 SetActive(true)할 때 OnEnable로 다시 들어옴 -
    /// 별도 해제 호출을 잊어버릴 걱정이 없음.
    ///
    /// 정적 컬렉션이라 씬 전환 시 원소가 안 비워질 위험이 있는데, 여기 들어가는 항목은 전부
    /// MonoBehaviour의 OnDisable에서만 Unregister되므로 오브젝트가 파괴되면(씬 전환으로 파괴돼도)
    /// Unity가 OnDisable을 호출해준 뒤 파괴 흐름이 진행됨 - 정상 흐름에서는 누수가 없음.
    /// </summary>
    public static class MonsterRegistry
    {
        private static readonly HashSet<MonsterPathFollower> _active = new();

        public static void Register(MonsterPathFollower monster) => _active.Add(monster);

        public static void Unregister(MonsterPathFollower monster) => _active.Remove(monster);

        public static IReadOnlyCollection<MonsterPathFollower> GetAll() => _active;
    }
}
