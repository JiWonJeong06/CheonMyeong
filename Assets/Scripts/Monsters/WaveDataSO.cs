using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Monsters
{
    /// <summary>웨이브 하나 안에서 한 몬스터 종류를 몇 마리, 몇 초 간격으로 낼지.</summary>
    [Serializable]
    public struct WaveEntry
    {
        public MonsterDataSO monster;
        public int count;
        [Tooltip("같은 종류 몬스터끼리 스폰되는 간격(초)")]
        public float spawnInterval;
    }

    /// <summary>
    /// 웨이브 하나의 구성. 보통 여러 종류의 몬스터가 섞여서 나오므로 WaveEntry 리스트로 구성함.
    /// MonsterSpawner가 이 리스트를 순서대로(entries[0] 다 스폰 후 entries[1]...) 처리함 -
    /// 동시에 여러 종류를 섞어 내야 하면 나중에 병렬 처리로 바꿔야 함(지금은 더미 스코프 밖).
    ///
    /// [버그 수정] 이 파일은 원래 WaveData.cs였는데, 파일명(WaveData)과 ScriptableObject를
    /// 상속하는 클래스명(WaveDataSO)이 서로 달라서 AssetDatabase.CreateAsset()으로 애셋을 만들
    /// 때마다 m_Script 참조가 fileID: 0(스크립트 없음)으로 저장되는 문제가 있었음 - SaveAssets/
    /// Refresh를 아무리 바로 호출해도 재현됨(Unity의 알려진 동작: ScriptableObject/MonoBehaviour를
    /// 상속하는 클래스는 파일명과 클래스명이 정확히 일치해야 스크립트 참조가 제대로 직렬화됨).
    /// 파일명을 클래스명(WaveDataSO)과 맞춰서 이 문제를 근본적으로 해결함.
    /// </summary>
    [CreateAssetMenu(menuName = "TowerDefense/Wave Data")]
    public class WaveDataSO : ScriptableObject
    {
        public string waveName;

        [Tooltip("이전 웨이브 종료(또는 게임 시작) 후 이 웨이브가 시작되기까지 대기 시간(초)")]
        public float delayBeforeWave = 5f;

        public List<WaveEntry> entries = new();
    }
}
