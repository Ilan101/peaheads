using UnityEngine;

namespace PeaHeads
{
    public class PeaProjectile : MonoBehaviour
    {
        [SerializeField] private float _speed = 10f;
        [SerializeField] private float _radius = 0.2f;
        [SerializeField] private float _lifetime = 4f;
        [SerializeField] private int _damage = 1;

        private Transform _owner;

        // sweepFrom: a point inside the shooter (e.g. head centre). The gap from there to the spawn point is
        // checked first, so a mouth poking through a wall or tree can't fire through it.
        public void Launch(Transform owner, Vector3 sweepFrom)
        {
            _owner = owner;
            Destroy(gameObject, _lifetime);

            Vector3 gap = transform.position - sweepFrom;
            float length = gap.magnitude;
            if (length > 0.001f && Sweep(sweepFrom, gap / length, length, out RaycastHit hit)) Impact(hit);
        }

        // Sweep ahead each frame instead of using a Rigidbody: no tunnelling, and easy to run server-side later.
        private void Update()
        {
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
            if (health) health.TakeDamage(_damage);
            Destroy(gameObject);
            enabled = false;
        }
    }
}
