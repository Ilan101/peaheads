using PeaHeads.Net;
using UnityEngine;

namespace PeaHeads
{
    // The lobby's 3D set, 3 km from the arena (past every player camera's far plane and the minimap): a soil strip,
    // four slots in a row and a local camera that renders into a texture LobbyView shows mirrored. Built in code by
    // LobbyView; renders only while the lobby is open.
    public class LobbyStage : MonoBehaviour
    {
        public const int SlotCount = SessionController.MaxPlayers;
        public static readonly Vector3 Origin = new(3000f, 0f, 0f);
        private const float Radius = 40f;

        [SerializeField, Range(2f, 6f)] private float _slotSpacing = 3.6f;
        [SerializeField, Range(4f, 30f)] private float _cameraDistance = 12f;
        [SerializeField, Range(0f, 5f)] private float _cameraHeight = 2.2f;
        [SerializeField, Range(0f, 3f)] private float _lookHeight = 1.4f;
        [SerializeField, Range(10f, 90f)] private float _fieldOfView = 40f;
        [SerializeField, Range(0f, 50f)] private float _lightIntensity = 12f;
        [SerializeField] private Color _background = new(0.06f, 0.12f, 0.08f);
        [SerializeField] private Color _soil = new(0.24f, 0.16f, 0.1f);

        public Camera Camera { get; private set; }
        public RenderTexture Texture { get; private set; }

        // Draws nothing; it only keeps one camera on the display while the player camera is off (else Unity shows
        // "No cameras rendering" over the lobby UI).
        private Camera _screen;

        public static bool Contains(Vector3 position) => (position - Origin).sqrMagnitude < Radius * Radius;

        // Slots fill from the middle out: centre-left, centre-right, then the two edges (columns counted from the left).
        // ponytail: written for SlotCount = 4.
        private static readonly int[] Columns = { 1, 2, 0, 3 };

        // Plants face the camera (−Z). Column 0 is +X, which the mirrored view shows on the left.
        public Pose Slot(int index) => new(
            Origin + new Vector3(((SlotCount - 1) * 0.5f - Columns[index]) * _slotSpacing, 0f, 0f),
            Quaternion.Euler(0f, 180f, 0f));

        private void Awake()
        {
            transform.position = Origin;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(SlotCount * _slotSpacing + 6f, 0.2f, 8f);
            floor.GetComponent<Renderer>().material.color = _soil;

            var lightGo = new GameObject("Light", typeof(Light));
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 5f, -5f);
            Light fill = lightGo.GetComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 30f;
            fill.intensity = _lightIntensity;

            var cameraGo = new GameObject("LobbyCamera", typeof(Camera));
            cameraGo.transform.SetParent(transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, _cameraHeight, -_cameraDistance);
            cameraGo.transform.LookAt(Origin + Vector3.up * _lookHeight);
            Camera = cameraGo.GetComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = _background;
            Camera.fieldOfView = _fieldOfView;
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 60f;
            Camera.enabled = false;

            _screen = new GameObject("ScreenCamera", typeof(Camera)).GetComponent<Camera>();
            _screen.transform.SetParent(transform, false); // under LobbyStage, so AtmosphereController leaves it alone
            _screen.cullingMask = 0;
            _screen.clearFlags = CameraClearFlags.SolidColor;
            _screen.backgroundColor = Color.black;
            _screen.enabled = false;
        }

        // The texture follows the screen size; call every frame while the lobby is open.
        public void SetRendering(bool on)
        {
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            if (on && (!Texture || Texture.width != w || Texture.height != h))
            {
                ReleaseTexture();
                Texture = new RenderTexture(w, h, 24);
                Camera.targetTexture = Texture;
            }
            Camera.enabled = on;
            _screen.enabled = on;
        }

        private void OnDestroy() => ReleaseTexture();

        private void ReleaseTexture()
        {
            if (!Texture) return;
            if (Camera) Camera.targetTexture = null;
            Texture.Release();
            Destroy(Texture);
            Texture = null;
        }
    }
}
