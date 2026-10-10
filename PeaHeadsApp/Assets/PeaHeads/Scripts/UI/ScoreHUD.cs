using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // ponytail: IMGUI score bars + winner banner, like PlayerHUD; swap for a Canvas with the UI pass.
    [RequireComponent(typeof(MatchManager))]
    public class ScoreHUD : MonoBehaviour
    {
        [SerializeField] private Color _team0Color = new(0.55f, 0.85f, 0.25f);
        [SerializeField] private Color _team1Color = new(0.95f, 0.6f, 0.2f);
        [SerializeField, Range(100f, 600f)] private float _barWidth = 320f;

        private MatchManager _match;
        private GUIStyle _label;
        private GUIStyle _banner;

        private void Awake() => _match = GetComponent<MatchManager>();

        private void OnGUI()
        {
            if (!_match.IsSpawned) return;
            _label ??= new GUIStyle(GUI.skin.label)
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _banner ??= new GUIStyle(_label) { fontSize = 48 };

            int localTeam = MatchManager.TeamOf(NetworkManager.Singleton.LocalClientId);
            float mid = Screen.width / 2f;
            DrawBar(new Rect(mid - _barWidth - 10f, 12f, _barWidth, 26f), 0, false, localTeam);
            DrawBar(new Rect(mid + 10f, 12f, _barWidth, 26f), 1, true, localTeam);

            int seconds = Mathf.CeilToInt(_match.TimeRemaining);
            GUI.Label(new Rect(mid - 60f, 42f, 120f, 30f), $"{seconds / 60}:{seconds % 60:00}", _label);

            if (!_match.IsEnded) return;
            bool draw = _match.Winner < 0;
            string winner = draw ? "DRAW" : _match.Winner == 0 ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
            string result = draw ? "Tied on points" : _match.Winner == localTeam ? "You win!" : "You lose";
            float y = Screen.height * 0.3f;
            GUI.Label(new Rect(0f, y, Screen.width, 60f), winner, _banner);
            GUI.Label(new Rect(0f, y + 64f, Screen.width, 30f), $"{result}   Restarting...", _label);
        }

        private void DrawBar(Rect rect, int team, bool fillFromRight, int localTeam)
        {
            int score = _match.Score(team);
            float fill = Mathf.Clamp01(score / (float)_match.TargetScore);
            float width = rect.width * fill;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = team == 0 ? _team0Color : _team1Color;
            GUI.DrawTexture(new Rect(fillFromRight ? rect.xMax - width : rect.x, rect.y, width, rect.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            string you = team == localTeam ? "  (YOU)" : "";
            GUI.Label(rect, $"P{team + 1}  {score} / {_match.TargetScore}{you}", _label);
        }
    }
}
