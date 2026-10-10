using UnityEngine;

namespace PeaHeads
{
    // Mouth open / wink packed into one byte for PlayerNetSync.
    public static class FaceFlags
    {
        public static byte Pack(bool mouthOpen, bool wink) => (byte)((mouthOpen ? 1 : 0) | (wink ? 2 : 0));

        public static (bool mouthOpen, bool wink) Unpack(byte value) => ((value & 1) != 0, (value & 2) != 0);
    }

    // What the opponent sees: the snout flares while the mouth is open, one eye squints on a wink.
    public class PlantFace : MonoBehaviour
    {
        [SerializeField] private Transform _snout;
        [SerializeField] private Transform _mouthHole;
        [SerializeField] private Transform _winkEye;
        [SerializeField, Range(1f, 2f)] private float _openFlare = 1.22f;
        [Tooltip("Mouth hole scale on top of the flare; keep hole radius below the rim.")]
        [SerializeField, Range(1f, 2f)] private float _openHole = 1.3f;
        [SerializeField, Range(0.05f, 1f)] private float _winkSquint = 0.15f;
        [SerializeField, Range(1f, 40f)] private float _smoothing = 14f;

        private Vector3 _current = Vector3.one;
        private Vector3 _target = Vector3.one;
        private Vector3 _snoutRest;
        private Vector3 _holeRest;
        private Vector3 _eyeRest;

        private void Awake()
        {
            if (_snout) _snoutRest = _snout.localScale;
            if (_mouthHole) _holeRest = _mouthHole.localScale;
            if (_winkEye) _eyeRest = _winkEye.localScale;
        }

        public void SetFlags(bool mouthOpen, bool wink) =>
            _target = Targets(mouthOpen, wink, _openFlare, _openHole, _winkSquint);

        private void Update()
        {
            _current = Vector3.Lerp(_current, _target, 1f - Mathf.Exp(-_smoothing * Time.deltaTime));
            if (_snout) _snout.localScale = new Vector3(_snoutRest.x * _current.x, _snoutRest.y * _current.x, _snoutRest.z);
            if (_mouthHole)
                _mouthHole.localScale = new Vector3(_holeRest.x * _current.y, _holeRest.y * _current.y, _holeRest.z);
            if (_winkEye) _winkEye.localScale = new Vector3(_eyeRest.x, _eyeRest.y * _current.z, _eyeRest.z);
        }

        // x = snout flare, y = mouth-hole scale, z = wink-eye height.
        public static Vector3 Targets(bool mouthOpen, bool wink, float openFlare, float openHole, float winkSquint) =>
            new(mouthOpen ? openFlare : 1f, mouthOpen ? openHole : 1f, wink ? winkSquint : 1f);
    }
}
