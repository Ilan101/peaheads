using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // ponytail: IMGUI placeholder HUD; swap for a Canvas when the art pass happens.
    public class PlayerHUD : MonoBehaviour
    {
        private PlantingController _planting;
        private PlayerHealth _health;
        private PlayerCameraRig _rig;
        private HeadTrackingInput _tracking;
        private NetworkObject _networkObject;
        private GUIStyle _style;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _health = GetComponent<PlayerHealth>();
            _rig = GetComponent<PlayerCameraRig>();
            _tracking = GetComponent<HeadTrackingInput>();
            _networkObject = GetComponent<NetworkObject>();
        }

        private void OnGUI()
        {
            if (StoryIntro.IsPlaying) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };

            int team = _networkObject && _networkObject.IsSpawned ? MatchManager.TeamOf(_networkObject.OwnerClientId) : 0;
            string ready = _planting.CanShoot ? "  -  READY TO SPIT" : "";
            GUI.color = Faction.Color(team);
            GUI.Label(new Rect(20, 240, 600, 30), Faction.Name(team), _style);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 270, 600, 30), $"HP {_health.Current}    {_planting.State}{ready}", _style);
            GUI.Label(new Rect(20, 300, 700, 30), TrackingLabel(), _style);
            if (_tracking && _tracking.State == TrackingState.Tracking) DrawHeadDot(new Rect(20, 334, 60, 60), _tracking.Head);
            GUI.Label(new Rect(20, Screen.height - 40, 1200, 30),
                "WASD move   E plant/uproot   LMB / open mouth: spit (planted)   RMB / wink: zoom   Arrows / real head: lean   C recenter   Esc menu", _style);

            float size = _rig.IsZoomed ? 10f : 6f;
            GUI.DrawTexture(new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f, size, size),
                Texture2D.whiteTexture);
        }

        private string TrackingLabel() => !_tracking ? "Webcam: off (keyboard)" : _tracking.State switch
        {
            TrackingState.Tracking => "Webcam: tracking",
            TrackingState.NoFace => "Webcam: no face (keyboard)",
            _ => "Webcam: off (keyboard) - run head_tracker.py",
        };

        // Where the webcam currently puts the head: centre = neutral, edges = full lean / rise / duck.
        private static void DrawHeadDot(Rect box, Vector2 head)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            float x = box.center.x + head.x * box.width * 0.5f;
            float y = box.center.y - head.y * box.height * 0.5f;
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, 6f, 6f), Texture2D.whiteTexture);
        }
    }
}
