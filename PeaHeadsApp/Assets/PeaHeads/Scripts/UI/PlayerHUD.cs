using UnityEngine;

namespace PeaHeads
{
    // ponytail: IMGUI placeholder HUD; swap for a Canvas when the art pass happens.
    public class PlayerHUD : MonoBehaviour
    {
        private PlantingController _planting;
        private PlayerHealth _health;
        private PlayerCameraRig _rig;
        private GUIStyle _style;

        private void Awake()
        {
            _planting = GetComponent<PlantingController>();
            _health = GetComponent<PlayerHealth>();
            _rig = GetComponent<PlayerCameraRig>();
        }

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };

            string ready = _planting.CanShoot ? "  -  READY TO FIRE" : "";
            GUI.Label(new Rect(20, 20, 600, 30), $"HP {_health.Current}    {_planting.State}{ready}", _style);
            GUI.Label(new Rect(20, Screen.height - 40, 1200, 30),
                "WASD move   E plant/unplant   LMB fire (planted)   RMB zoom   Arrows lean head   Esc free cursor", _style);

            float size = _rig.IsZoomed ? 10f : 6f;
            GUI.DrawTexture(new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f, size, size),
                Texture2D.whiteTexture);
        }
    }
}
