using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Map;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 캐릭터별 스킬/궁극기 로직의 공통 규격. 캐릭터 하나 = 이 클래스를 상속한 클래스 하나(예: HayoungSkill, YuraSkill).
    ///
    /// [구조] 타워 프리팹은 제네릭 하나를 모두가 공유하므로(CharacterDataSO에 프리팹 참조가 없음) 캐릭터마다 TowerUnit을
    /// 상속하지 않고, TowerUnit이 배치 때 CharacterSkillFactory로 스킬 객체(순수 C#, MonoBehaviour 아님)를 하나 만들어
    /// 들고 있는 "조합" 방식을 씀. 공통 동작(타겟 찾기·평타·게이지·궁극기 자동 발동·합성/강화·날씨)은 전부 TowerUnit이 하고,
    /// 스킬 클래스는 아래 훅만 덮어씀. 네트워크 동기화 상태가 필요 없는 서버 로직이라 NetworkBehaviour가 필요 없음.
    ///
    /// [규칙]
    /// 1) 모든 훅은 서버에서만 호출됨(IsServer 확인 불필요). 클라이언트는 이 객체 자체가 없음.
    /// 2) 수치는 캐시하지 말고 쓸 때마다 Skill("키") / Ultimate("키")로 읽을 것 - 강화(Lv)가 바뀌면 바로 반영됨.
    ///    키 이름은 Characters.json(= 스킬 밸런싱 시트 '변수 (코드용)')의 key와 같아야 함.
    /// 3) 몬스터에 피해를 줄 땐 반드시 Tower.DealDamage(처형은 Tower.Execute) - 히트 플래시, 처치 게이지, OnKill 훅이
    ///    평타와 똑같이 처리됨. 몬스터 목록을 돌며 피해를 줄 땐 Tower.CollectOwnBoardMonsters로 먼저 리스트에 복사할 것
    ///    (피해로 몬스터가 죽으면 레지스트리가 바뀌어 순회 중 예외가 남). 리스트는 필드로 한 번만 만들어 재사용(할당 방지).
    /// 4) 지속 스탯 버프는 Tower.SetBuff(키, StatBuff, 지속시간) - 같은 키로 다시 걸면 갱신, 지속시간 생략 시 영구.
    ///    Lv에 따라 값이 바뀌는 버프는 OnProgressionChanged에서 다시 걸 것.
    /// 5) 시간이 흐르는 상태는 Time.time 기준 만료 시각으로 들고, OnTick에서 갱신(코루틴 쓰지 않음 - 디스폰 때 정리가 자동).
    /// 6) 이벤트/정적 구독은 OnBind에서 하고 OnUnbind에서 반드시 해제(타워는 합성·파괴로 사라짐).
    /// </summary>
    public abstract class CharacterSkill
    {
        /// <summary>이 스킬을 쓰는 타워(Bind 이후 유효).</summary>
        protected TowerUnit Tower { get; private set; }

        /// <summary>캐릭터 마스터 데이터.</summary>
        protected CharacterDataSO Data => Tower.Data;

        internal void Bind(TowerUnit tower)
        {
            Tower = tower;
            OnBind();
        }

        internal void Unbind()
        {
            OnUnbind();
            Tower = null;
        }

        /// <summary>스킬(패시브) 변수의 현재 강화 레벨 값. 키가 없으면 0 + 경고.</summary>
        protected float Skill(string key) => Tower.GetSkillValue(key);

        /// <summary>궁극기 변수의 현재 강화 레벨 값. 키가 없으면 0 + 경고.</summary>
        protected float Ultimate(string key) => Tower.GetUltimateValue(key);

        // ---- 스킬 구현용 공용 작업 리스트 (할당 방지 - 처음 쓸 때 한 번만 만들어 재사용) ----
        private List<MonsterPathFollower> _targets;
        private List<MonsterPathFollower> _probe;

        /// <summary>순회하며 피해를 주는 용도의 몬스터 리스트(Tower.CollectOwnBoardMonsters* 로 채워 쓸 것). 중첩 사용 금지.</summary>
        protected List<MonsterPathFollower> Targets => _targets ??= new List<MonsterPathFollower>(32);

        /// <summary>"쓸 대상이 있는가" 판정 전용 임시 리스트(피해를 주지 않는 조회용).</summary>
        protected List<MonsterPathFollower> Probe => _probe ??= new List<MonsterPathFollower>(32);

        /// <summary>내 보드에 사거리 안 몬스터가 하나라도 있는가 - 지속형/범위형 궁극기의 CanCastUltimate 기본 조건.</summary>
        protected bool AnyEnemyInRange() => Tower.CollectOwnBoardMonstersInRange(Probe) > 0;

        /// <summary>내 보드에 몬스터가 하나라도 있는가(맵 전체 대상 궁극기용).</summary>
        protected bool AnyEnemyOnBoard() => Tower.CollectOwnBoardMonsters(Probe) > 0;

        // ---- 캐릭터별로 덮어쓰는 훅 (전부 서버 전용) ----

        /// <summary>타워가 배치된 직후(1★·강화 Lv1 기준). 패시브 버프 걸기 등 초기화.</summary>
        protected virtual void OnBind() { }

        /// <summary>타워가 사라질 때(합성·파괴·매치 종료). 걸어둔 버프 제거, 구독 해제 등.</summary>
        protected virtual void OnUnbind() { }

        /// <summary>합성 별 또는 SP 강화 레벨이 바뀐 직후.</summary>
        public virtual void OnProgressionChanged() { }

        /// <summary>매 프레임(버프형·딜러 모두). 시간 기반 스킬, 버프형 궁극기 시간 충전 등.</summary>
        public virtual void OnTick(float deltaTime) { }

        /// <summary>공격이 시작될 때마다(대상 확정 직후, 피해 전). 공격 횟수 기반 스택(홍한 연타·강민준 열기)이나 주변 광역(김형욱 내려찍기)에 사용.</summary>
        public virtual void OnAttackStart(MonsterPathFollower target) { }

        /// <summary>평타 피해 방식을 스킬이 대신 처리하려면 여기서 처리하고 true를 돌려줌(정다희 표창 3발, 전민재 부메랑, 백하은 관통, 임서연 레이저 등).
        /// false(기본)면 TowerUnit이 단일 대상에 damage(=현재 공격력)를 그대로 줌. true로 처리할 땐 Tower.DealDamage로 직접 피해를 주고,
        /// 필요하면 OnBasicHit에 해당하는 효과도 직접 처리할 것(이 경우 OnBasicHit 훅은 불리지 않음).</summary>
        public virtual bool HandleBasicAttack(MonsterPathFollower target, float damage) => false;

        /// <summary>평타가 명중했고 대상이 아직 살아 있을 때(막타는 OnKill). damage = 방금 준 피해.</summary>
        public virtual void OnBasicHit(MonsterPathFollower target, float damage) { }

        /// <summary>이 타워가 몬스터를 처치했을 때(평타·스킬 모두). target은 이미 풀에 반납됐으므로 Position/Data 읽기만 할 것.</summary>
        public virtual void OnKill(MonsterPathFollower target) { }

        /// <summary>궁극기를 쓸 대상/조건이 있는가. false면 게이지가 가득 찬 채로 대기함(엑셀 구현 규칙).</summary>
        public virtual bool CanCastUltimate() => true;

        /// <summary>궁극기 발동(게이지가 차면 TowerUnit이 자동 호출, 남은 게이지는 이월).</summary>
        public virtual void CastUltimate() { }
    }
}
