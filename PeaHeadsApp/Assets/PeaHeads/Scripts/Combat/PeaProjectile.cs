using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // Host moves the pea and resolves hits; clients only see it through NetworkTransform.
    public class PeaProjectile : NetworkBehaviour
    {
        [SerializeField, Range(1f, 50f)] private float _speed = 10f;
        [SerializeField, Range(0.05f, 1f)] private float _radius = 0.2f;
        [SerializeField, Range(0.5f, 10f)] private float _lifetime = 4f;
        [SerializeField, Range(1, 5)] private int _damage = 1;

        private Transform _owner;
        private ulong _attacker;
        private PlayerShooting _shooter;
        private float _despawnTime;
        private bool _done;
        // Shooter's team; set by the host before Spawn so every copy has it on its first frame.
        private readonly NetworkVariable<byte> _team = new();

        private void Awake() => transform.localScale = Vector3.one * (_radius * 2f);

        // Server only, before NetworkObject.Spawn().
        public void SetTeam(int team) => _team.Value = (byte)team;

        // Same colour as the shooter's head.
        public override void OnNetworkSpawn()
        {
            if (TryGetComponent(out Renderer peaRenderer)) peaRenderer.material.color = Faction.Color(_team.Value);
        }

        // Server only, right after Spawn(). sweepFrom is a point inside the shooter (head centre): the gap from
        // there to the spawn point is checked first, so a mouth poking through a wall can't fire through it.
        public void Launch(Transform owner, Vector3 sweepFrom)
        {
            _owner = owner;
            _attacker = owner.GetComponent<NetworkObject>().OwnerClientId;
            _shooter = owner.GetComponent<PlayerShooting>();
            _despawnTime = Time.time + _lifetime;

            Vector3 gap = transform.position - sweepFrom;
            float length = gap.magnitude;
            if (length > 0.001f && Sweep(sweepFrom, gap / length, length, out RaycastHit hit)) Impact(hit);
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _done) return;
            if (Time.time >= _despawnTime)
            {
                Finish();
                return;
            }

            float step = _speed * Time.deltaTime;
            if (Sweep(transform.position, transform.forward, step, out RaycastHit hit))
            {
                Impact(hit);
                return;
            }
            transform.position += transform.forward * step;
        }

        private bool Sweep(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit) =>
            PhysicsUtil.CastIgnoring(_owner, origin, _radius, direction, distance, out hit,
                QueryTriggerInteraction.Collide);

        private void Impact(RaycastHit hit)
        {
            PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();
            bool damaged = health && health.TakeDamage(_damage, _attacker);
            // The pea despawns now, so the (still spawned) shooter broadcasts the splat and the hit marker.
            if (_shooter && _shooter.IsSpawned) _shooter.ReportImpact(hit.point, hit.normal, damaged, damaged && health.IsDead);
            Finish();
        }

        private void Finish()
        {
            _done = true;
            NetworkObject.Despawn();
        }
    }
}
