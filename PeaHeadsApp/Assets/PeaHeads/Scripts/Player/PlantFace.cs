using UnityEngine;

namespace PeaHeads
{
    // Mouth open / wink packed into one byte for PlayerNetSync.
    public static class FaceFlags
    {
        public static byte Pack(bool mouthOpen, bool wink) => (byte)((mouthOpen ? 1 : 0) | (wink ? 2 : 0));

        public static (bool mouthOpen, bool wink) Unpack(byte value) => ((value & 1) != 0, (value & 2) != 0);
    }

    // What the opponent sees: the snout flares while the mouth is open, an eyelid swings down over one eye on a wink.
    public class PlantFace : MonoBehaviour
    {
        // Largest hole scale that keeps the disc inside the snout's outer wall (see PlantMeshesTests).
        public const float DefaultOpenHole = 1.15f;
        // Lid rotation (about the eye's X axis) when open: swung back so it sits inside the head, out of sight.
        public const float OpenLidAngle = -160f;

        [SerializeField] private Transform _snout;
        [SerializeField] private Transform _mouthHole;
        [SerializeField] private Transform _winkLid;
        [SerializeField, Range(1f, 2f)] private float _openFlare = 1.22f;
        [Tooltip("Mouth hole scale on top of the flare; keep hole radius below the rim.")]
        [SerializeField, Range(1f, 2f)] private float _openHole = DefaultOpenHole;
        [SerializeField, Range(1f, 40f)] private float _smoothing = 14f;

        // x = snout flare, y = mouth-hole scale, z = lid closure (0 open, 1 closed).
        private Vector3 _current = new(1f, 1f, 0f);
        private Vector3 _target = new(1f, 1f, 0f);
        private Vector3 _snoutRest;
        private Vector3 _holeRest;

        private void Awake()
        {
            if (_snout) _snoutRest = _snout.localScale;
            if (_mouthHole) _holeRest = _mouthHole.localScale;
        }

        public void SetFlags(bool mouthOpen, bool wink) =>
            _target = Targets(mouthOpen, wink, _openFlare, _openHole);

        private void Update()
        {
            _current = Vector3.Lerp(_current, _target, 1f - Mathf.Exp(-_smoothing * Time.deltaTime));
            if (_snout) _snout.localScale = new Vector3(_snoutRest.x * _current.x, _snoutRest.y * _current.x, _snoutRest.z);
            if (_mouthHole)
                _mouthHole.localScale = new Vector3(_holeRest.x * _current.y, _holeRest.y * _current.y, _holeRest.z);
            if (_winkLid) _winkLid.localRotation = Quaternion.Euler(LidAngle(_current.z), 0f, 0f);
        }

        public static Vector3 Targets(bool mouthOpen, bool wink, float openFlare, float openHole) =>
            new(mouthOpen ? openFlare : 1f, mouthOpen ? openHole : 1f, wink ? 1f : 0f);

        public static float LidAngle(float closure) => Mathf.LerpUnclamped(OpenLidAngle, 0f, closure);
    }
}
