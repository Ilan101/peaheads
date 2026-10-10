using System;
using System.Collections;
using UnityEngine;

namespace PeaHeads
{
    public class PlayerHealth : MonoBehaviour
    {
        [SerializeField] private int _maxHealth = 3;
        [Tooltip("> 0: come back after this many seconds (practice dummies).")]
        [SerializeField] private float _respawnDelay;
        [SerializeField] private Renderer _flashRenderer;
        [SerializeField] private AudioClip _hitSfx;

        public int Current { get; private set; }
        public bool IsDead => Current <= 0;
        public event Action<PlayerHealth> Died;

        private void Awake() => Current = _maxHealth;

        public void TakeDamage(int amount)
        {
            if (IsDead) return;
            Current = Mathf.Max(0, Current - amount);
            if (_hitSfx) AudioSource.PlayClipAtPoint(_hitSfx, transform.position);
            if (_flashRenderer) StartCoroutine(Flash());
            if (!IsDead) return;

            SetAlive(false);
            Died?.Invoke(this);
            if (_respawnDelay > 0f) Invoke(nameof(ResetHealth), _respawnDelay);
        }

        public void ResetHealth()
        {
            Current = _maxHealth;
            SetAlive(true);
        }

        private void SetAlive(bool alive)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = alive;
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c is not CharacterController) c.enabled = alive;
        }

        private IEnumerator Flash()
        {
            Material material = _flashRenderer.material;
            Color original = material.color;
            material.color = Color.red;
            yield return new WaitForSeconds(0.12f);
            material.color = original;
        }
    }
}
