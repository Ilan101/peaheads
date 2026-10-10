using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PeaHeads
{
    // Host owns the HP value; every instance reacts to changes (flash, sound, hide/show).
    public class PlayerHealth : NetworkBehaviour
    {
        private const int DefaultMaxHealth = 3;

        [SerializeField, Range(1, 10)] private int _maxHealth = DefaultMaxHealth;
        [Tooltip("> 0: host restores HP after this many seconds.")]
        [SerializeField, Range(0f, 10f)] private float _respawnDelay;
        [SerializeField] private Renderer _flashRenderer;
        [SerializeField] private AudioClip _hitSfx;

        private readonly NetworkVariable<int> _current = new(DefaultMaxHealth);

        public int Current => _current.Value;
        public bool IsDead => _current.Value <= 0;
        public event Action<PlayerHealth> Died;
        public event Action Revived;

        private bool _flashing;
        private Color _flashOriginal;

        public override void OnNetworkSpawn()
        {
            if (IsServer) _current.Value = _maxHealth;
            _current.OnValueChanged += OnHealthChanged;
            SetAlive(!IsDead);
        }

        public override void OnNetworkDespawn() => _current.OnValueChanged -= OnHealthChanged;

        public void TakeDamage(int amount)
        {
            if (!IsServer || IsDead) return;
            _current.Value = Mathf.Max(0, _current.Value - amount);
            if (!IsDead) return;

            Died?.Invoke(this);
            if (_respawnDelay > 0f) Invoke(nameof(ResetHealth), _respawnDelay);
        }

        public void ResetHealth()
        {
            if (IsServer) _current.Value = _maxHealth;
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous)
            {
                if (_hitSfx) AudioSource.PlayClipAtPoint(_hitSfx, transform.position);
                if (_flashRenderer) StartCoroutine(Flash());
            }

            if (current <= 0) SetAlive(false);
            else if (previous <= 0)
            {
                SetAlive(true);
                Revived?.Invoke();
            }
        }

        private void SetAlive(bool alive)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = alive;
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c is not CharacterController) c.enabled = alive;
        }

        // Original color captured only by the first of overlapping flashes, so red never sticks.
        private IEnumerator Flash()
        {
            Material material = _flashRenderer.material;
            if (!_flashing) _flashOriginal = material.color;
            _flashing = true;
            material.color = Color.red;
            yield return new WaitForSeconds(0.12f);
            material.color = _flashOriginal;
            _flashing = false;
        }
    }
}
