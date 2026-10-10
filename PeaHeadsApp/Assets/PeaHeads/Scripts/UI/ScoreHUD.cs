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
        [SerializeField] private Color _collectColor = new(0.5f, 0.3f, 0.12f);

        private MatchManager _match;
        private MushroomSpawner _mushrooms;
        private GUIStyle _label;
        private GUIStyle _banner;
        private GUIStyle _line;
        private bool _rivalShielded;
        private static Texture2D _shield;
        private float _nextRivalCheck;

        private void Awake()
        {
            _match = GetComponent<MatchManager>();
            _mushrooms = GetComponent<MushroomSpawner>();
        }

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
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } };
            _banner ??= new GUIStyle(_label) { fontSize = 52 };
            _line ??= new GUIStyle(_label) { fontSize = 24 };

            int localTeam = MatchManager.TeamOf(NetworkManager.Singleton.LocalClientId);
            float mid = Screen.width / 2f;
            DrawBar(new Rect(mid - _barWidth - 10f, 12f, _barWidth, 26f), 0, false, localTeam);
            DrawBar(new Rect(mid + 10f, 12f, _barWidth, 26f), 1, true, localTeam);

            int seconds = Mathf.CeilToInt(_match.TimeRemaining);
            GUI.Label(new Rect(mid - 60f, 42f, 120f, 30f), $"{seconds / 60}:{seconds % 60:00}", _label);

            float collect = _mushrooms ? _mushrooms.CollectProgress(localTeam) : 0f;
            if (collect > 0f) DrawCollectBar(collect);

            // Rival still in the intro story (can't be hit yet): pulsing shield beside their bar.
            if (_rivalShielded && !_match.IsEnded)
            {
                _shield ??= UiSprites.Shield().texture;
                const float size = 26f;
                float x = 1 - localTeam == 0 ? mid - _barWidth - 16f - size : mid + _barWidth + 16f;
                GUI.color = new Color(1f, 1f, 1f, 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f));
                GUI.DrawTexture(new Rect(x, 12f, size, size), _shield);
                GUI.color = Color.white;
            }

            if (!_match.IsEnded) return;
            int winner = _match.Winner;
            float y = Screen.height * 0.28f;
            bool draw = winner < 0;
            _banner.normal.textColor = draw ? Color.black : Faction.Color(winner);
            GUI.Label(new Rect(0f, y, Screen.width, 64f), draw ? "DRAW" : $"{Faction.Name(winner)} WINS", _banner);
            GUI.Label(new Rect(0f, y + 68f, Screen.width, 34f), draw ? "Tied on points" : Faction.WinLine(winner), _line);
            string result = draw ? "" : winner == localTeam ? "You win!   " : "You lose   ";
            GUI.Label(new Rect(0f, y + 106f, Screen.width, 30f), $"{result}Restarting...", _label);
        }

        // Bottom-centre progress of the mushroom the local player is planted on.
        private void DrawCollectBar(float progress)
        {
            var rect = new Rect((Screen.width - _barWidth) / 2f, Screen.height - 70f, _barWidth, 22f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = _collectColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * progress, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x, rect.y - 26f, rect.width, 24f), "Collecting mushroom...", _label);
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
