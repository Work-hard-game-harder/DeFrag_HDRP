using Unity.Netcode;
using UnityEngine;

namespace DeFrag.Combat
{
    /// <summary>
    /// Synchronizes health from the server while PlayerStats remains the
    /// gameplay-facing model used by UI and local presentation.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerStats))]
    public sealed class NetworkPlayerHealth : NetworkBehaviour, IPlayerDamageReceiver
    {
        private readonly NetworkVariable<int> currentHealth = new NetworkVariable<int>(
            100,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [SerializeField] private PlayerStats playerStats;

        public int CurrentHealth => currentHealth.Value;
        public bool IsAlive => playerStats != null && !playerStats.IsDead;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void Start()
        {
            // PlayerStats currently restores scene data in Start. Re-apply the
            // synchronized value afterwards so clients cannot overwrite it locally.
            if (IsSpawned)
                playerStats.ApplyHealth(currentHealth.Value);
        }

        public override void OnNetworkSpawn()
        {
            currentHealth.OnValueChanged += HandleHealthChanged;

            if (IsServer)
                currentHealth.Value = playerStats.MaxHealth;

            playerStats.ApplyHealth(currentHealth.Value);
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= HandleHealthChanged;
        }

        public bool TryApplyDamage(in DamageRequest request)
        {
            if (request.Amount <= 0 || !IsAlive)
                return false;

            // Allow the same prefab to be tested without a running network session.
            if (!IsSpawned)
                return playerStats.TakeDamage(request.Amount);

            if (!IsServer)
                return false;

            int nextHealth = Mathf.Max(0, currentHealth.Value - request.Amount);
            if (nextHealth == currentHealth.Value)
                return false;

            currentHealth.Value = nextHealth;
            return true;
        }

        [ContextMenu("Debug/Apply 10 Damage (Server Only)")]
        private void ApplyDebugDamage()
        {
            TryApplyDamage(new DamageRequest(10, gameObject, transform.position, -1));
        }

        private void HandleHealthChanged(int previousHealth, int newHealth)
        {
            playerStats.ApplyHealth(newHealth);
        }

        private void ResolveReferences()
        {
            if (playerStats == null)
                playerStats = GetComponent<PlayerStats>();
        }
    }
}
