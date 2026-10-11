using UnityEngine;

namespace PeaHeads.Net
{
    // Scene camera for the connect screen; hidden while a local player's own camera is active. Pans slowly from side
    // to side (rotation only, so it never drifts into scenery) to keep the arena behind the menu alive.
    public class OverviewCamera : MonoBehaviour
    {
        private static OverviewCamera _instance;

        [SerializeField, Range(0f, 20f)] private float _panDegrees = 7f;
        [Tooltip("Seconds for one full pan left and back.")]
        [SerializeField, Range(5f, 120f)] private float _panPeriod = 40f;

        private Quaternion _rest;

        private void Awake()
        {
            _instance = this;
            _rest = transform.localRotation;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void LateUpdate()
        {
            float t = Time.unscaledTime / _panPeriod * Mathf.PI * 2f;
            transform.localRotation = _rest * Quaternion.Euler(Mathf.Sin(t * 2f) * _panDegrees * 0.12f,
                Mathf.Sin(t) * _panDegrees, 0f);
        }

        public static void SetVisible(bool visible)
        {
            if (_instance) _instance.gameObject.SetActive(visible);
        }
    }
}
