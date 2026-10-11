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

        public static bool Contains(Vector3 position) => (position - Origin).sqrMagnitude < Radius * Radius;

        // Plants face the camera (−Z). Slot 0 is +X, which the mirrored view shows on the left.
        public Pose Slot(int index) => new(
            Origin + new Vector3(((SlotCount - 1) * 0.5f - index) * _slotSpacing, 0f, 0f),
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
        }

        // The texture follows the screen size; call every frame while the lobby is open.
        public void SetRendering(bool on)
        {
            if (on && (!Texture || Texture.width != Screen.width || Texture.height != Screen.height))
            {
                ReleaseTexture();
                Texture = new RenderTexture(Screen.width, Screen.height, 24);
                Camera.targetTexture = Texture;
            }
            Camera.enabled = on;
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
