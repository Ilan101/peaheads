using PeaHeads.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace PeaHeads
{
    public struct PlayerInputFrame
    {
        public Vector2 Move;
        public Vector2 Look;
        public Vector2 Head;
        public bool PlantPressed;
        public bool FirePressed;
        public bool ZoomHeld;
    }

    // The only reader of keyboard/mouse (and later the webcam). Enabled only on the local player's plant.
    [DefaultExecutionOrder(-100)]
    public class PlayerInput : MonoBehaviour
    {
        [SerializeField, Range(0.01f, 1f)] private float _mouseSensitivity = 0.1f;
        [SerializeField, Range(0.1f, 2f)] private float _trackerTimeout = 0.5f;
        [Tooltip("Switched on/off with local ownership: own camera, HUD.")]
        [SerializeField] private GameObject[] _localOnlyObjects = new GameObject[0];
        [SerializeField] private Behaviour[] _localOnlyBehaviours = new Behaviour[0];

        public PlayerInputFrame Current { get; private set; }

        private Vector2 _trackerHead;
        private bool _trackerAim;
        private bool _trackerShootPrevious;
        private bool _trackerFirePending;
        private float _trackerTime = float.NegativeInfinity;

        public void SetLocal(bool isLocal)
        {
            foreach (GameObject go in _localOnlyObjects) go.SetActive(isLocal);
            foreach (Behaviour behaviour in _localOnlyBehaviours) behaviour.enabled = isLocal;
            enabled = isLocal;
            if (!isLocal) Current = default;
        }

        // Webcam receiver calls this on the main thread once per packet. head is -1..1.
        public void SetTrackerInput(Vector2 head, bool shoot, bool aim)
        {
            _trackerHead = new Vector2(Mathf.Clamp(head.x, -1f, 1f), Mathf.Clamp(head.y, -1f, 1f));
            _trackerAim = aim;
            if (shoot && !_trackerShootPrevious) _trackerFirePending = true;
            _trackerShootPrevious = shoot;
            _trackerTime = Time.time;
        }

        private void OnEnable()
        {
            SetCursorLocked(true);
            OverviewCamera.SetVisible(false);
        }

        private void OnDisable()
        {
            Current = default;
            SetCursorLocked(false);
            OverviewCamera.SetVisible(true);
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
            {
                Current = default;
                return;
            }

            bool click = mouse.leftButton.wasPressedThisFrame;
            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (click && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
                click = false;
            }
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool trackerFresh = Time.time - _trackerTime < _trackerTimeout;

            Current = new PlayerInputFrame
            {
                Move = new Vector2(Axis(kb.aKey, kb.dKey), Axis(kb.sKey, kb.wKey)),
                Look = locked ? mouse.delta.ReadValue() * _mouseSensitivity : Vector2.zero,
                Head = trackerFresh
                    ? _trackerHead
                    : new Vector2(Axis(kb.leftArrowKey, kb.rightArrowKey), Axis(kb.downArrowKey, kb.upArrowKey)),
                PlantPressed = kb.eKey.wasPressedThisFrame,
                FirePressed = locked && (click || _trackerFirePending),
                ZoomHeld = locked && (mouse.rightButton.isPressed || (trackerFresh && _trackerAim)),
            };
            _trackerFirePending = false;
        }

        private static float Axis(KeyControl negative, KeyControl positive) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
