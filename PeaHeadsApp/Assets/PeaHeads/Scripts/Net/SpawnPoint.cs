using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PeaHeads.Net
{
    // Team 0 = host, 1 = client. Players (re)spawn at a random point of their team.
    public class SpawnPoint : MonoBehaviour
    {
        private static readonly List<SpawnPoint> All = new();

        [FormerlySerializedAs("_index")]
        [SerializeField, Range(0, 1)] private int _team;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        // Never returns `avoid` unless it is the team's only point.
        public static SpawnPoint GetRandom(int team, SpawnPoint avoid = null)
        {
            List<SpawnPoint> candidates = All.FindAll(point => point._team == team && point != avoid);
            if (candidates.Count == 0) candidates = All.FindAll(point => point._team == team);
            return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
        }
    }
}
