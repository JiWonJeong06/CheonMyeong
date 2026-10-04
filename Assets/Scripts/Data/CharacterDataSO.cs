using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Data
{
    public enum CurrencyType
    {
        Gold,
        Gem
    }

    /// <summary>근접/원거리 구분 (엑셀 '근/원').</summary>
    public enum CharacterAttackType
    {
        Melee,
        Ranged
    }

    /// <summary>타겟 우선순위 (엑셀 F열): 맨 앞 / 체력 높은 적 / 체력 낮은 적.</summary>
    public enum TargetPriority
    {
        Front,
        HighestHp,
        LowestHp
    }

    /// <summary>
    /// 합성·SP 강화로 세질 때 증가분을 받는 방식 (엑셀 '공격력형/공속형').
    /// Power = 공격력만, Speed = 증가분의 60%를 공격력, 40%를 공격 간격으로 받음.
    /// </summary>
    public enum MergeType
    {
        Power,
        Speed
    }

    /// <summary>처치 유형 - 처치 게이지 배수(기본 1 / 광역 0.5 / 저격 1.5)에 쓰임.</summary>
    public enum KillType
    {
        Normal,
        Area,
        Sniper
    }

    /// <summary>스킬/궁극기 변수 하나. values는 강화 Lv1~maxSkillLevel 값(배열 인덱스 0 = Lv1).</summary>
    [Serializable]
    public class SkillParameter
    {
        public string key;      // 예: "attackBonusPercent" - 스킬 코드는 이 이름으로 값을 꺼냄
        public string meaning;
        public float[] values;

        /// <summary>강화 레벨(1부터)에 해당하는 값. 범위를 벗어나면 양 끝 값으로 clamp함.</summary>
        public float GetValue(int level)
        {
            if (values == null || values.Length == 0) return 0f;
            int index = Mathf.Clamp(level - 1, 0, values.Length - 1);
            return values[index];
        }
    }

    /// <summary>스킬 또는 궁극기 하나(이름, 설명, 변수 목록).</summary>
    [Serializable]
    public class SkillDefinition
    {
        public string name;
        public string description;
        public List<SkillParameter> parameters = new();

        /// <summary>key에 해당하는 변수의 강화 레벨별 값. 키가 없으면 false.</summary>
        public bool TryGetValue(string key, int level, out float value)
        {
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    if (parameters[i].key == key)
                    {
                        value = parameters[i].GetValue(level);
                        return true;
                    }
                }
            }
            value = 0f;
            return false;
        }

        /// <summary>TryGetValue의 간단 버전 - 키가 없으면 0을 돌려주고 경고 로그를 남김.</summary>
        public float GetValue(string key, int level)
        {
            if (TryGetValue(key, level, out float v)) return v;
            Debug.LogWarning($"[SkillDefinition] '{name}'에 변수 '{key}'가 없음.");
            return 0f;
        }
    }

    /// <summary>
    /// 캐릭터 하나의 마스터 데이터. CharacterJsonImporter가 Assets/Data/Characters.json을 읽어서 이
    /// 애셋들을 자동으로 생성/갱신함 - 그러니까 데이터 필드는 인스펙터에서 손으로 고치지 말고 항상
    /// JSON(엑셀 '캐릭터 스탯' + '스킬 밸런싱')을 고친 다음 재임포트할 것.
    /// (iconSprite/portraitSprite는 JSON에 없는 영역이라 인스펙터에서 연결하고, 재임포트해도 유지됨.)
    ///
    /// 모든 수치는 노드 Lv1 · SP 강화 Lv1 · 1★ 기준(엑셀 구현 규칙). 노드 레벨/강화/합성 배율은
    /// 이 데이터 위에 별도 계층에서 곱함.
    /// </summary>
    [CreateAssetMenu(menuName = "TowerDefense/Character Data")]
    public class CharacterDataSO : ScriptableObject
    {
        [Header("식별")]
        public int code;              // 엑셀 코드(1000~). 소속 구간: 1000 천명회 / 1500 흑연회 / 2000 비선청 / 2500 지하문 / 3000 풍류단 / 3500 화랑회 / 4000~ 무소속
        public string characterId;    // "char_" + code - 덱/해금 저장(PlayerPrefs)과 DB 조회에 쓰는 키
        public string displayName;
        public string faction;        // 원래 소속(코드 구간에서 계산). 노드 트리 라인과는 다를 수 있음(무소속 3명)
        [TextArea] public string description;

        [Header("분류")]
        public string weapon;
        public CharacterAttackType attackType;
        [Tooltip("버프형 - 기본 공격을 하지 않고 스킬·궁극기만 사용 (서윤아·윤재훈·진혜영·유하린·천희재)")]
        public bool isSupport;
        public MergeType mergeType;
        public TargetPriority targetPriority;

        [Header("전투 스탯 (노드 Lv1 · 강화 Lv1 · 1★)")]
        [Tooltip("엑셀 '공격력'")]
        public float baseDamage;
        [Tooltip("공격 간격(초) - 낮을수록 빠름. 초당 공격 횟수는 AttacksPerSecond")]
        public float attackInterval = 1f;
        [Tooltip("사거리(칸 단위 실수). 캐릭터 중심에서 원형으로 잼")]
        public float range;

        [Header("합성 별당 증가량")]
        public float starAttackPower;
        public float starAttackInterval; // 음수 = 간격 감소(빨라짐)

        [Header("궁극기 게이지")]
        public int gaugePerHit;
        public KillType killType;
        public int gaugePerKill;
        public int ultimateGauge;

        [Header("스킬 / 궁극기 (변수는 강화 Lv1~5)")]
        public SkillDefinition skill = new();
        public SkillDefinition ultimate = new();

        [Header("해금")]
        [Tooltip("시작 캐릭터 5명(하영·이유라·김도현·윤재훈·박성훈)은 처음부터 보유")]
        public bool isStarter;
        [Tooltip("[임시] 노드 트리 해금 구조가 정해지면 교체될 값")]
        public int unlockCost;
        public CurrencyType unlockCurrency;

        [Header("비주얼")]
        public Sprite iconSprite;
        public Sprite portraitSprite;

        /// <summary>초당 공격 횟수(= 1 / 공격 간격). 간격이 0 이하로 잘못 들어오면 0.</summary>
        public float AttacksPerSecond => attackInterval > 0f ? 1f / attackInterval : 0f;
    }
}
