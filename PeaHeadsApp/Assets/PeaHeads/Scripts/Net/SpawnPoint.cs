using System.Collections.Generic;
using UnityEngine;

namespace PeaHeads.Net
{
    public class SpawnPoint : MonoBehaviour
    {
        private static readonly List<SpawnPoint> All = new();

        [SerializeField] private int _index;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public static SpawnPoint Get(int index) => All.Find(point => point._index == index);
    }
}
