using Unity.Netcode;
using UnityEngine;

namespace PeaHeads.Net.Test
{
    // Server moves the pea and resolves hits; clients only see it via NetworkTransform.
    public class NetTestPea : NetworkBehaviour
    {
        [SerializeField] private float _speed = 10f;
        [SerializeField] private float _radius = 0.2f;
        [SerializeField] private float _lifetime = 4f;

        private ulong _shooterId;
        private Vector3 _direction;
        private float _despawnTime;

        public void Init(ulong shooterId, Vector3 direction)
        {
            _shooterId = shooterId;
            _direction = direction.normalized;
            _despawnTime = Time.time + _lifetime;
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned) return;

            if (Time.time >= _despawnTime)
            {
                NetworkObject.Despawn();
                return;
            }

            float step = _speed * Time.fixedDeltaTime;
            if (Physics.SphereCast(transform.position, _radius, _direction, out var hit, step,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var player = hit.collider.GetComponentInParent<NetTestPlayer>();
                if (player == null || player.OwnerClientId != _shooterId)
                {
                    if (player != null) player.ServerRegisterHit();
                    NetworkObject.Despawn();
                    return;
                }
            }

            transform.position += _direction * step;
        }
    }
}
