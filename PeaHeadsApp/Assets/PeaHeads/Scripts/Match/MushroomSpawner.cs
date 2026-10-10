using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // Host keeps a few mushrooms active at random spots; a player who finishes planting on one scores it.
    [RequireComponent(typeof(MatchManager))]
    public class MushroomSpawner : NetworkBehaviour
    {
        private const int MaxSpots = 32; // one bit each in _activeMask

        [SerializeField, Range(1, 10)] private int _activeCount = 3;
        [SerializeField, Range(0f, 30f)] private float _respawnDelay = 5f;
        [SerializeField, Range(0.5f, 4f)] private float _collectRadius = 1.5f;

        private readonly NetworkVariable<int> _activeMask = new();
        private readonly List<float> _respawnTimes = new();
        private MushroomSpot[] _spots = new MushroomSpot[0];
        private MatchManager _match;

        private void Awake() => _match = GetComponent<MatchManager>();

        public override void OnNetworkSpawn()
        {
            _spots = FindObjectsByType<MushroomSpot>(FindObjectsSortMode.None)
                .OrderBy(s => s.Index).Take(MaxSpots).ToArray();
            _activeMask.OnValueChanged += OnMaskChanged;
            ApplyMask(0, _activeMask.Value, false);
            if (!IsServer) return;
            _match.RoundStarted += ResetField;
            ResetField();
        }

        public override void OnNetworkDespawn()
        {
            _activeMask.OnValueChanged -= OnMaskChanged;
            _match.RoundStarted -= ResetField;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _match.IsEnded) return;
            for (int i = _respawnTimes.Count - 1; i >= 0; i--)
                if (Time.time >= _respawnTimes[i] && TryActivateRandom()) _respawnTimes.RemoveAt(i);
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList) TryCollect(client.PlayerObject);
        }

        private void TryCollect(NetworkObject player)
        {
            if (!player || !player.TryGetComponent(out PlantingController planting)) return;
            if (planting.State != PlantState.Planted) return;
            if (player.TryGetComponent(out PlayerHealth health) && health.IsDead) return;

            for (int i = 0; i < _spots.Length; i++)
            {
                if (!IsActive(i) || !IsNear(i, player.transform.position)) continue;
                _activeMask.Value &= ~(1 << i);
                _respawnTimes.Add(Time.time + _respawnDelay);
                _match.AddPoints(MatchManager.TeamOf(player.OwnerClientId), _match.MushroomPoints);
                return;
            }
        }

        private void ResetField()
        {
            _respawnTimes.Clear();
            _activeMask.Value = 0;
            for (int i = 0; i < _activeCount; i++) TryActivateRandom();
        }

        // Free = inactive and nobody standing on it, so a mushroom never appears under a planted player.
        private bool TryActivateRandom()
        {
            var free = new List<int>();
            for (int i = 0; i < _spots.Length; i++)
                if (!IsActive(i) && !AnyPlayerNear(i)) free.Add(i);
            if (free.Count == 0) return false;
            _activeMask.Value |= 1 << free[Random.Range(0, free.Count)];
            return true;
        }

        private bool AnyPlayerNear(int spot)
        {
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && IsNear(spot, client.PlayerObject.transform.position)) return true;
            return false;
        }

        private bool IsActive(int spot) => (_activeMask.Value & (1 << spot)) != 0;

        private bool IsNear(int spot, Vector3 position)
        {
            Vector3 d = position - _spots[spot].transform.position;
            return d.x * d.x + d.z * d.z <= _collectRadius * _collectRadius;
        }

        private void OnMaskChanged(int previous, int current) => ApplyMask(previous, current, true);

        private void ApplyMask(int previous, int current, bool effects)
        {
            for (int i = 0; i < _spots.Length; i++)
            {
                bool was = (previous & (1 << i)) != 0, now = (current & (1 << i)) != 0;
                if (now && !was) _spots[i].Show();
                if (now || !was) continue;
                _spots[i].Hide();
                if (effects) _spots[i].PlayCollect();
            }
        }
    }
}
