using System;
using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    public enum MatchState : byte { Playing, Ended }

    // Host keeps score (kills + mushrooms), ends the match at the target score and restarts it after a delay.
    public class MatchManager : NetworkBehaviour
    {
        [SerializeField, Range(1, 100)] private int _targetScore = 20;
        [SerializeField, Range(0, 10)] private int _killPoints = 3;
        [SerializeField, Range(0, 10)] private int _mushroomPoints = 1;
        [SerializeField, Range(1f, 30f)] private float _restartDelay = 5f;
        [SerializeField] private GameObject _confettiPrefab;
        [SerializeField] private AudioClip _endSfx;

        private readonly NetworkVariable<int> _score0 = new();
        private readonly NetworkVariable<int> _score1 = new();
        // Declared before _state so clients already have the winner when the state callback runs.
        private readonly NetworkVariable<sbyte> _winner = new(-1);
        private readonly NetworkVariable<byte> _state = new();

        // Server only.
        public event Action RoundStarted;

        public int TargetScore => _targetScore;
        public int MushroomPoints => _mushroomPoints;
        public bool IsEnded => _state.Value == (byte)MatchState.Ended;
        public int Winner => _winner.Value;
        public int Score(int team) => team == 0 ? _score0.Value : _score1.Value;

        public static int TeamOf(ulong clientId) => clientId == NetworkManager.ServerClientId ? 0 : 1;

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += OnStateChanged;
            if (!IsServer) return;
            // In-scene object: values survive a previous host session, so start clean.
            _score0.Value = 0;
            _score1.Value = 0;
            _winner.Value = -1;
            _state.Value = (byte)MatchState.Playing;
            PlayerHealth.Killed += OnKilled;
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            PlayerHealth.Killed -= OnKilled;
            CancelInvoke();
        }

        public void AddPoints(int team, int points)
        {
            if (!IsServer || IsEnded || points <= 0) return;
            NetworkVariable<int> score = team == 0 ? _score0 : _score1;
            score.Value = Mathf.Min(_targetScore, score.Value + points);
            if (score.Value < _targetScore) return;

            _winner.Value = (sbyte)team;
            _state.Value = (byte)MatchState.Ended;
            Invoke(nameof(Restart), _restartDelay);
        }

        private void OnKilled(PlayerHealth victim, ulong attacker)
        {
            if (!victim.TryGetComponent(out PlayerNetSync _)) return; // dummies don't score
            int team = TeamOf(attacker);
            if (team != TeamOf(victim.OwnerClientId)) AddPoints(team, _killPoints);
        }

        private void Restart()
        {
            if (!IsSpawned) return;
            _score0.Value = 0;
            _score1.Value = 0;
            _winner.Value = -1;
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && client.PlayerObject.TryGetComponent(out PlayerHealth health))
                    health.ResetHealth();
            RoundStarted?.Invoke();
            RoundStartedRpc();
            _state.Value = (byte)MatchState.Playing;
        }

        [Rpc(SendTo.Everyone)]
        private void RoundStartedRpc()
        {
            NetworkObject player = NetworkManager.LocalClient?.PlayerObject;
            if (player && player.TryGetComponent(out PlayerNetSync sync)) sync.Respawn();
        }

        private void OnStateChanged(byte previous, byte current)
        {
            bool ended = current == (byte)MatchState.Ended;
            NetworkObject local = NetworkManager.LocalClient?.PlayerObject;
            if (local && local.TryGetComponent(out PlayerInput input)) input.Frozen = ended;
            if (!ended) return;

            if (_endSfx && local) AudioSource.PlayClipAtPoint(_endSfx, local.transform.position);
            if (!_confettiPrefab) return;
            foreach (PlayerNetSync player in FindObjectsByType<PlayerNetSync>(FindObjectsSortMode.None))
                if (TeamOf(player.OwnerClientId) == _winner.Value)
                    Destroy(Instantiate(_confettiPrefab, player.transform.position, Quaternion.identity), _restartDelay);
        }
    }
}
