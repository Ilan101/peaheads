using UnityEngine;

namespace PeaHeads
{
    public static class PhysicsUtil
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        // Closest hit that isn't part of ignoreRoot. radius 0 = plain raycast.
        public static bool CastIgnoring(Transform ignoreRoot, Vector3 origin, float radius, Vector3 direction,
            float maxDistance, out RaycastHit closest,
            QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
        {
            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, direction, Hits, maxDistance, ~0, triggers)
                : Physics.RaycastNonAlloc(origin, direction, Hits, maxDistance, ~0, triggers);

            closest = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (ignoreRoot && hit.transform.IsChildOf(ignoreRoot)) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    closest = hit;
                }
            }
            return best < float.MaxValue;
        }
    }
}
