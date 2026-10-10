using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeaHeads.Net.Test
{
    // Hour 0-2 netcode validation only. Real player = Person 2's PlayerMovement/PlayerShooting.
    public class NetTestPlayer : NetworkBehaviour
    {
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _turnSpeed = 150f;
        [SerializeField] private NetworkObject _peaPrefab;
        [SerializeField] private float _muzzleDistance = 0.8f;
        [SerializeField] private float _fireCooldown = 0.6f;

        private readonly NetworkVariable<int> _timesHit = new();
        private float _nextFireTime;

        public override void OnNetworkSpawn()
        {
            GetComponent<Renderer>().material.color = OwnerClientId == 0
                ? new Color(0.3f, 0.8f, 0.3f)
                : new Color(0.9f, 0.6f, 0.2f);

            if (IsOwner)
            {
                float side = OwnerClientId == 0 ? -1f : 1f;
                transform.SetPositionAndRotation(
                    new Vector3(6f * side, 1f, 0f),
                    Quaternion.LookRotation(Vector3.right * -side));
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            float turn = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float move = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            transform.Rotate(0f, turn * _turnSpeed * Time.deltaTime, 0f);
            transform.position += transform.forward * (move * _moveSpeed * Time.deltaTime);

            if (kb.spaceKey.wasPressedThisFrame)
                FireRpc(transform.position + transform.forward * _muzzleDistance, transform.forward);
        }

        [Rpc(SendTo.Server)]
        private void FireRpc(Vector3 origin, Vector3 direction)
        {
            if (Time.time < _nextFireTime) return;
            _nextFireTime = Time.time + _fireCooldown;

            var pea = Instantiate(_peaPrefab, origin, Quaternion.LookRotation(direction));
            pea.GetComponent<NetTestPea>().Init(OwnerClientId, direction);
            pea.Spawn(destroyWithScene: true);
        }

        public void ServerRegisterHit()
        {
            if (IsServer) _timesHit.Value++;
        }

        private void OnGUI()
        {
            if (!IsSpawned) return;
            float scale = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float y = 260f + 24f * OwnerClientId;
            GUI.Label(new Rect(10, y, 340, 22),
                $"Player {OwnerClientId}{(IsOwner ? " (you)" : "")} hit {_timesHit.Value}x");
        }
    }
}
