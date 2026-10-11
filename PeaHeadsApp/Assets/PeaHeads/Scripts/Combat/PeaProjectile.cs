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
        [Tooltip("Metres the pea flies straight before gravity kicks in (not zoomed).")]
        [SerializeField, Range(0f, 50f)] private float _dropAfter = 5f;
        [Tooltip("Metres the pea flies straight before gravity kicks in when fired zoomed.")]
        [SerializeField, Range(0f, 50f)] private float _zoomedDropAfter = 10f;
        [Tooltip("Launch speed multiplier when fired zoomed.")]
        [SerializeField, Range(1f, 3f)] private float _zoomedSpeedMultiplier = 1.25f;
        [Tooltip("Downward acceleration (m/s²) once past the drop distance.")]
        [SerializeField, Range(0f, 50f)] private float _gravity = 9.81f;

        private Transform _owner;
        private ulong _attacker;
        private PlayerShooting _shooter;
        private float _despawnTime;
        private Vector3 _velocity;
        private float _straightLeft;
        private bool _done;
        // Shooter's team; set by the host before Spawn so every copy has it on its first frame.
        private readonly NetworkVariable<byte> _team = new();

        private void Awake() => transform.localScale = Vector3.one * (_radius * 2f);

        // Server only, before NetworkObject.Spawn().
        public void SetTeam(int team) => _team.Value = (byte)team;

        // Same colour as the shooter's head.
        public override void OnNetworkSpawn()
        {
            TryGetComponent(out Renderer peaRenderer);
            if (peaRenderer) peaRenderer.material.color = Faction.Color(_team.Value);
            WorldPolish.DecoratePea(gameObject, peaRenderer, Faction.Color(_team.Value));
        }

        // Server only, right after Spawn(). sweepFrom is a point inside the shooter (head centre): the gap from
        // there to the spawn point is checked first, so a mouth poking through a wall can't fire through it.
        public void Launch(Transform owner, Vector3 sweepFrom, bool zoomed)
        {
            _velocity = transform.forward * (zoomed ? _speed * _zoomedSpeedMultiplier : _speed);
            _straightLeft = zoomed ? _zoomedDropAfter : _dropAfter;
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

            float dt = Time.deltaTime;
            if (_straightLeft > 0f) _straightLeft -= _velocity.magnitude * dt;
            else _velocity += Vector3.down * (_gravity * dt);

            float step = _velocity.magnitude * dt;
            Vector3 direction = _velocity.normalized;
            if (Sweep(transform.position, direction, step, out RaycastHit hit))
            {
                Impact(hit);
                return;
            }
            transform.position += direction * step;
        }

        private bool Sweep(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit) =>
            PhysicsUtil.CastIgnoring(_owner, origin, _radius, direction, distance, out hit,
                QueryTriggerInteraction.Collide);

        private void Impact(RaycastHit hit)
        {
            PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();
            bool damaged = health && health.TakeDamage(_damage, _attacker);
            // The pea despawns now, so the (still spawned) shooter broadcasts the splat and the hit marker.
            if (_shooter && _shooter.IsSpawned)
            {
                // Victim's client id for the killcam (dummies have no PlayerNetSync and get none).
                ulong victim = health && health.TryGetComponent(out Net.PlayerNetSync _) ? health.OwnerClientId : KillcamView.NoVictim;
                _shooter.ReportImpact(hit.point, hit.normal, damaged, damaged && health.IsDead, victim);
            }
            Finish();
        }

        private void Finish()
        {
            _done = true;
            NetworkObject.Despawn();
        }
    }
}
