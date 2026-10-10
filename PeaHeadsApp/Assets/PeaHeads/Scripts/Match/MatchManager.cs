using System;
using System.Collections.Generic;
using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    public enum MatchState : byte { Playing, Ended }

    // Host keeps score (kills + mushrooms). The match ends at the target score or when the clock runs out (most
    // points wins, a tie is a draw), then restarts after a delay.
    public class MatchManager : NetworkBehaviour
    {
        [SerializeField, Range(1, 100)] private int _targetScore = 20;
        [Tooltip("Match length in seconds.")]
        [SerializeField, Range(30f, 600f)] private float _matchDuration = 120f;
        [SerializeField, Range(0, 10)] private int _killPoints = 3;
        [SerializeField, Range(0, 10)] private int _mushroomPoints = 1;
        [SerializeField, Range(1f, 30f)] private float _restartDelay = 5f;
        [SerializeField] private GameObject _confettiPrefab;
        [SerializeField] private AudioClip _endSfx;

        private readonly NetworkVariable<int> _score0 = new();
        private readonly NetworkVariable<int> _score1 = new();
        // Server-time timestamp, so every machine counts down to the same moment.
        private readonly NetworkVariable<double> _endTime = new(); // 0 = waiting for the intro story
        // Lets each player's story start, and its shield reach the host, before the clock may start.
        private const float ClockGrace = 1f;
        private float _clockArmedAt;
        // Declared before _state so clients already have the winner when the state callback runs.
        private readonly NetworkVariable<sbyte> _winner = new(-1);
        private readonly NetworkVariable<byte> _state = new();
        // Client ids per team, shuffled by the host at every match start. Allocated in Awake (native memory).
        private NetworkList<ulong> _team0;
        private NetworkList<ulong> _team1;
        private readonly System.Random _rng = new();

        public static MatchManager Instance { get; private set; }
        // Raised on every machine when the team lists change.
        public static event Action TeamsChanged;

        // Server only.
        public event Action RoundStarted;

        public int TargetScore => _targetScore;
        public int MushroomPoints => _mushroomPoints;
        public int KillPoints => _killPoints;
        public float MatchDuration => _matchDuration;
        public bool IsEnded => _state.Value == (byte)MatchState.Ended;
        // -1 with IsEnded = draw.
        public int Winner => _winner.Value;
        // Full duration while the clock waits for the intro story, 0 once ended.
        public float TimeRemaining => !IsSpawned || IsEnded ? 0f
            : ClockWaiting ? _matchDuration
            : Mathf.Max(0f, (float)(_endTime.Value - NetworkManager.ServerTime.Time));

        private bool ClockWaiting => _endTime.Value <= 0d;
        public int Score(int team) => team == 0 ? _score0.Value : _score1.Value;

        // 0 = Bloom, 1 = Blight. Unknown ids (and scenes without a match) count as Bloom.
        public static int TeamOf(ulong clientId) => Instance && Instance._team1.Contains(clientId) ? 1 : 0;

        public static bool HasTeam(ulong clientId) =>
            Instance && (Instance._team0.Contains(clientId) || Instance._team1.Contains(clientId));

        // Shuffle, then deal alternately: team sizes differ by at most one and nobody is tied to a team.
        public static int[] AssignTeams(IReadOnlyList<ulong> clientIds, System.Random rng)
        {
            int count = clientIds.Count;
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var teams = new int[count];
            for (int k = 0; k < count; k++) teams[order[k]] = k % 2;
            return teams;
        }

        private void Awake()
        {
            _team0 = new NetworkList<ulong>();
            _team1 = new NetworkList<ulong>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _team0.OnListChanged += OnTeamsChanged;
            _team1.OnListChanged += OnTeamsChanged;
            _state.OnValueChanged += OnStateChanged;
            if (!IsServer) return;
            ShuffleTeams();
            // In-scene object: values survive a previous host session, so start clean.
            _score0.Value = 0;
            _score1.Value = 0;
            _winner.Value = -1;
            _state.Value = (byte)MatchState.Playing;
            ArmClock();
            PlayerHealth.Killed += OnKilled;
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            _team0.OnListChanged -= OnTeamsChanged;
            _team1.OnListChanged -= OnTeamsChanged;
            _state.OnValueChanged -= OnStateChanged;
            PlayerHealth.Killed -= OnKilled;
            if (NetworkManager)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            CancelInvoke();
        }

        public void AddPoints(int team, int points)
        {
            if (!IsServer || IsEnded || points <= 0) return;
            NetworkVariable<int> score = team == 0 ? _score0 : _score1;
            score.Value = Mathf.Min(_targetScore, score.Value + points);
            if (score.Value >= _targetScore) EndMatch(team);
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || IsEnded) return;
            if (ClockWaiting)
            {
                if (Time.time >= _clockArmedAt + ClockGrace && !AnyoneInStory())
                    _endTime.Value = NetworkManager.ServerTime.Time + _matchDuration;
                return;
            }
            if (NetworkManager.ServerTime.Time < _endTime.Value) return;
            int winner = _score0.Value == _score1.Value ? -1 : _score0.Value > _score1.Value ? 0 : 1;
            EndMatch(winner);
        }

        private void EndMatch(int winner)
        {
            _winner.Value = (sbyte)winner;
            _state.Value = (byte)MatchState.Ended;
            Invoke(nameof(Restart), _restartDelay);
        }

        // The countdown starts once nobody is watching the intro story (PlayerHealth is shielded while it plays).
        private void ArmClock()
        {
            _endTime.Value = 0d;
            _clockArmedAt = Time.time;
        }

        private bool AnyoneInStory()
        {
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && client.PlayerObject.TryGetComponent(out PlayerHealth health) &&
                    health.IsShielded)
                    return true;
            return false;
        }

        private void OnKilled(PlayerHealth victim, ulong attacker)
        {
            if (!victim.TryGetComponent(out PlayerNetSync _)) return; // dummies don't score
            int team = TeamOf(attacker);
            if (team != TeamOf(victim.OwnerClientId)) AddPoints(team, _killPoints);
        }

        // A rival arriving starts a fresh match, so points the host scored alone don't count.
        private void OnClientConnected(ulong clientId)
        {
            if (clientId != NetworkManager.ServerClientId) RestartNow();
        }

        // A leaver's slot is dropped; the remaining players keep their teams until the next round.
        private void OnClientDisconnected(ulong clientId)
        {
            _team0.Remove(clientId);
            _team1.Remove(clientId);
        }

        private void ShuffleTeams()
        {
            IReadOnlyList<ulong> ids = NetworkManager.ConnectedClientsIds;
            int[] teams = AssignTeams(ids, _rng);
            _team0.Clear();
            _team1.Clear();
            for (int i = 0; i < ids.Count; i++) (teams[i] == 0 ? _team0 : _team1).Add(ids[i]);
        }

        private void OnTeamsChanged(NetworkListEvent<ulong> _) => TeamsChanged?.Invoke();

        // Host's menu button. Also cancels the automatic restart if the match had already ended.
        public void RestartNow()
        {
            if (!IsServer || !IsSpawned) return;
            CancelInvoke(nameof(Restart));
            Restart();
        }

        private void Restart()
        {
            if (!IsSpawned) return;
            ShuffleTeams();
            _score0.Value = 0;
            _score1.Value = 0;
            _winner.Value = -1;
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && client.PlayerObject.TryGetComponent(out PlayerHealth health))
                    health.ResetHealth();
            RoundStarted?.Invoke();
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject && client.PlayerObject.TryGetComponent(out PlayerNetSync sync))
                    sync.RespawnOnSideRpc((byte)TeamOf(client.ClientId));
            ArmClock();
            _state.Value = (byte)MatchState.Playing;
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
