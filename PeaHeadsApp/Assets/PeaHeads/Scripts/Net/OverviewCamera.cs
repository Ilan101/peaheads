using UnityEngine;

namespace PeaHeads.Net
{
    // Scene camera for the connect screen; hidden while a local player's own camera is active.
    public class OverviewCamera : MonoBehaviour
    {
        private static OverviewCamera _instance;

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void SetVisible(bool visible)
        {
            if (_instance) _instance.gameObject.SetActive(visible);
        }
    }
}
