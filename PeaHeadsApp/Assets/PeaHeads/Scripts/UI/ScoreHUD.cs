using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // ponytail: IMGUI score bars + winner banner, like PlayerHUD; swap for a Canvas with the UI pass.
    [RequireComponent(typeof(MatchManager))]
    public class ScoreHUD : MonoBehaviour
    {
        [SerializeField, Range(100f, 600f)] private float _barWidth = 320f;

        private MatchManager _match;
        private GUIStyle _label;
        private GUIStyle _banner;
        private GUIStyle _line;
        private bool _rivalShielded;
        private float _nextRivalCheck;

        private void Awake() => _match = GetComponent<MatchManager>();

        private void Update()
        {
            if (!_match.IsSpawned || Time.unscaledTime < _nextRivalCheck) return;
            _nextRivalCheck = Time.unscaledTime + 0.5f;
            _rivalShielded = false;
            foreach (PlayerNetSync player in FindObjectsByType<PlayerNetSync>(FindObjectsSortMode.None))
                if (!player.IsOwner && player.TryGetComponent(out PlayerHealth health) && health.IsShielded)
                    _rivalShielded = true;
        }

        private void OnGUI()
        {
            if (!_match.IsSpawned || StoryIntro.IsPlaying) return;
            _label ??= new GUIStyle(GUI.skin.label)
<<<<<<< HEAD
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _banner ??= new GUIStyle(_label) { fontSize = 52 };
            _line ??= new GUIStyle(_label) { fontSize = 24 };
=======
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } };
            _banner ??= new GUIStyle(_label) { fontSize = 48 };
>>>>>>> fddbf20c3d6b70f7abdb081a8659f6a1b4a5d97a

            int localTeam = MatchManager.TeamOf(NetworkManager.Singleton.LocalClientId);
            float mid = Screen.width / 2f;
            DrawBar(new Rect(mid - _barWidth - 10f, 12f, _barWidth, 26f), 0, false, localTeam);
            DrawBar(new Rect(mid + 10f, 12f, _barWidth, 26f), 1, true, localTeam);

<<<<<<< HEAD
            if (_rivalShielded && !_match.IsEnded)
                GUI.Label(new Rect(0f, 42f, Screen.width, 24f),
                    $"{Faction.Name(1 - localTeam)} is still hearing the story... (can't be hit yet)", _label);

            if (!_match.IsEnded) return;
            int winner = _match.Winner;
            string result = winner == localTeam ? "You win!" : "You lose";
            float y = Screen.height * 0.28f;
            _banner.normal.textColor = Faction.Color(winner);
            GUI.Label(new Rect(0f, y, Screen.width, 64f), $"{Faction.Name(winner)} WINS", _banner);
            GUI.Label(new Rect(0f, y + 68f, Screen.width, 34f), Faction.WinLine(winner), _line);
            GUI.Label(new Rect(0f, y + 106f, Screen.width, 30f), $"{result}   Restarting...", _label);
=======
            int seconds = Mathf.CeilToInt(_match.TimeRemaining);
            GUI.Label(new Rect(mid - 60f, 42f, 120f, 30f), $"{seconds / 60}:{seconds % 60:00}", _label);

            if (!_match.IsEnded) return;
            bool draw = _match.Winner < 0;
            string winner = draw ? "DRAW" : _match.Winner == 0 ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
            string result = draw ? "Tied on points" : _match.Winner == localTeam ? "You win!" : "You lose";
            float y = Screen.height * 0.3f;
            GUI.Label(new Rect(0f, y, Screen.width, 60f), winner, _banner);
            GUI.Label(new Rect(0f, y + 64f, Screen.width, 30f), $"{result}   Restarting...", _label);
>>>>>>> 30a6db55afce18d6027465428d01898033b4c9a5
        }

        private void DrawBar(Rect rect, int team, bool fillFromRight, int localTeam)
        {
            int score = _match.Score(team);
            float fill = Mathf.Clamp01(score / (float)_match.TargetScore);
            float width = rect.width * fill;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Faction.Color(team);
            GUI.DrawTexture(new Rect(fillFromRight ? rect.xMax - width : rect.x, rect.y, width, rect.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            string you = team == localTeam ? "  (YOU)" : "";
            GUI.Label(rect, $"{Faction.Name(team)}  {score} / {_match.TargetScore}{you}", _label);
        }
    }
}
