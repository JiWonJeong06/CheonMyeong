using System.Collections.Generic;
using UnityEngine;
using TowerDefense.Monsters;

namespace TowerDefense.Skills
{
    /// <summary>
    /// 3001 전민재 (풍류단, 공속형, 원거리)
    /// 스킬 [부메랑 방패] : 평타 = 방패를 던져 공격력의 n% 피해, 돌아오는 방패는 n×0.7% 피해.  n = boomerangDamagePercent (단일 대상 2타)
    /// 궁극기 [방패] : 사거리 안 경로에 방패 벽 3개를 소환해 n초 동안 길을 막음(보스도 막힘, 체력 없음).  n = blockSeconds
    ///   구현: 사거리 안 경로 점들 중 균등 간격 3곳에 반경 0.4칸 차단 구역을 둠 - 그 안에 들어온 몬스터는 이동을 멈춤.
    /// </summary>
    public sealed class JeonMinjaeSkill : CharacterSkill
    {
        private const float ReturnDamageRatio = 0.7f;
        private const int WallCount = 3;
        private const float WallRadiusCells = 0.4f;
        private const float PathSampleStepCells = 0.25f;
        private static readonly Color WallColor = new Color(0.6f, 0.7f, 1f);

        private readonly List<Vector3> _points = new(32);

        public override bool HandleBasicAttack(MonsterPathFollower target, float damage)
        {
            float outbound = damage * Skill("boomerangDamagePercent") / 100f;
            Tower.DealDamage(target, outbound);
            if (target.IsAlive) Tower.DealDamage(target, outbound * ReturnDamageRatio);
            return true;
        }

        public override bool CanCastUltimate()
        {
            var spawner = Tower.OwnBoardSpawner;
            if (spawner == null) return false;
            return spawner.GetPathPointsInRange(_points, Tower.transform.position, Tower.RangeWorld, PathSampleStepCells * Tower.CellSize) > 0;
        }

        public override void CastUltimate()
        {
            var spawner = Tower.OwnBoardSpawner;
            if (spawner == null) return;

            int count = spawner.GetPathPointsInRange(_points, Tower.transform.position, Tower.RangeWorld, PathSampleStepCells * Tower.CellSize);
            if (count == 0) return;

            float seconds = Ultimate("blockSeconds");
            float radius = WallRadiusCells * Tower.CellSize;
            int walls = Mathf.Min(WallCount, count);
            for (int i = 0; i < walls; i++)
            {
                Vector3 p = _points[(int)((i + 0.5f) * count / walls)];
                spawner.AddBlockZone(p, radius, seconds);
                Tower.PlayZoneEffectRpc(p, radius, seconds, WallColor);
            }
            _points.Clear();
            SkillDebug.Log($"전민재 방패: 벽 {walls}개, {seconds:0.#}초");
        }
    }
}
