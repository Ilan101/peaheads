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
        private readonly NetworkVariable<bool> _shielded = new(writePerm: NetworkVariableWritePermission.Owner);

        public int Current => _current.Value;
        public int Max => _maxHealth;
        public bool IsDead => _current.Value <= 0;
        public float RespawnDelay => _respawnDelay;
        // True while the owner is watching the story intro: peas don't hurt.
        public bool IsShielded => _shielded.Value;
        public event Action<PlayerHealth> Died;
        public event Action Revived;
        // Server only: victim, attacker client id.
        public static event Action<PlayerHealth, ulong> Killed;

        private bool _flashing;
        private Color _flashOriginal;
        // Death tint to the killer's colour; renderers hide right after it (colliders go off immediately).
        public const float CleanseTime = 0.3f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private Coroutine _hide;

        public override void OnNetworkSpawn()
        {
            if (IsServer) _current.Value = _maxHealth;
            _current.OnValueChanged += OnHealthChanged;
            SetAlive(!IsDead);
        }

        public override void OnNetworkDespawn() => _current.OnValueChanged -= OnHealthChanged;

        // Server only. Returns whether damage was applied (false if dead or shielded).
        public bool TakeDamage(int amount, ulong attackerClientId)
        {
            if (!IsServer || IsDead || IsShielded) return false;
            _current.Value = Mathf.Max(0, _current.Value - amount);
            if (!IsDead) return true;

            Died?.Invoke(this);
            Killed?.Invoke(this, attackerClientId);
            // Host position: every machine seeds the same patch layout from it.
            CleansedRpc((byte)MatchManager.TeamOf(attackerClientId), transform.position);
            if (_respawnDelay > 0f) Invoke(nameof(ResetHealth), _respawnDelay);
            return true;
        }

        public void SetShielded(bool shielded)
        {
            if (IsSpawned && IsOwner && _shielded.Value != shielded) _shielded.Value = shielded;
        }

        // Server only. Living players only, capped at max.
        public void Heal(int amount)
        {
            if (IsServer && !IsDead) _current.Value = Mathf.Min(_maxHealth, _current.Value + amount);
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
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c is not CharacterController) c.enabled = alive;

            if (_hide != null)
            {
                StopCoroutine(_hide);
                _hide = null;
            }
            if (alive)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(null);
                ShowRenderers(true);
            }
            else if (isActiveAndEnabled) _hide = StartCoroutine(HideAfterCleanse());
            else ShowRenderers(false);
        }

        private IEnumerator HideAfterCleanse()
        {
            yield return new WaitForSeconds(CleanseTime + 0.05f);
            ShowRenderers(false);
            _hide = null;
        }

        private void ShowRenderers(bool visible)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CleansedRpc(byte killerTeam, Vector3 position) => StartCoroutine(Cleanse(killerTeam, position));

        // Tint every visible part toward the killer's colour, then burst and leave the killer's ground behind.
        private IEnumerator Cleanse(int killerTeam, Vector3 position)
        {
            Color target = Faction.Color(killerTeam);
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            var from = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].sharedMaterial;
                from[i] = material && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.clear;
            }

            var block = new MaterialPropertyBlock();
            for (float t = 0f; t < CleanseTime; t += Time.deltaTime)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (!renderers[i] || from[i] == Color.clear) continue;
                    renderers[i].GetPropertyBlock(block);
                    block.SetColor(BaseColorId, Color.Lerp(from[i], target, t / CleanseTime));
                    renderers[i].SetPropertyBlock(block);
                }
                yield return null;
            }

            KillSites sites = KillSites.Instance;
            if (!sites) yield break;
            sites.Burst(position + Vector3.up, killerTeam);
            sites.Cleanse(position, killerTeam);
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
