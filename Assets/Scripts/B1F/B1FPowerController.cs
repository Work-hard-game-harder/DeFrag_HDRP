using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum B1FPowerState : byte
    {
        PowerOff,
        EmergencyPower,
        FullPower
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class B1FPowerController : NetworkBehaviour
    {
        [Header("Power Roots")]
        [SerializeField] private GameObject powerOff;
        [SerializeField] private GameObject emergencyPower;
        [SerializeField] private GameObject fullPower;

        [Header("Emergency Spawn")]
        [SerializeField] private NetworkObject tvMonsterPrefab;
        [SerializeField] private Transform tvMonsterSpawnPoint;

        private readonly NetworkVariable<B1FPowerState> currentState = new(
            B1FPowerState.PowerOff,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private bool tvMonsterSpawned;

        public B1FPowerState CurrentState => currentState.Value;

        private void Awake() => ApplyState(currentState.Value);

        public override void OnNetworkSpawn()
        {
            currentState.OnValueChanged += OnPowerStateChanged;
            ApplyState(currentState.Value);
        }

        public override void OnNetworkDespawn()
        {
            currentState.OnValueChanged -= OnPowerStateChanged;
        }

        public bool CanUseBoxA => CurrentState == B1FPowerState.PowerOff;
        public bool CanUseBoxB => CurrentState == B1FPowerState.EmergencyPower;

        public void SetEmergencyPowerServer()
        {
            if (!IsServer || currentState.Value != B1FPowerState.PowerOff)
                return;

            currentState.Value = B1FPowerState.EmergencyPower;
            SpawnTvMonsterOnce();
        }

        public void SetFullPowerServer()
        {
            if (!IsServer || currentState.Value != B1FPowerState.EmergencyPower)
                return;

            currentState.Value = B1FPowerState.FullPower;
        }

        private void OnPowerStateChanged(B1FPowerState previous, B1FPowerState next) =>
            ApplyState(next);

        private void ApplyState(B1FPowerState state)
        {
            if (powerOff != null) powerOff.SetActive(state == B1FPowerState.PowerOff);
            if (emergencyPower != null) emergencyPower.SetActive(state == B1FPowerState.EmergencyPower);
            if (fullPower != null) fullPower.SetActive(state == B1FPowerState.FullPower);
        }

        private void SpawnTvMonsterOnce()
        {
            if (tvMonsterSpawned || tvMonsterPrefab == null || tvMonsterSpawnPoint == null)
                return;

            NetworkObject monster = Instantiate(
                tvMonsterPrefab,
                tvMonsterSpawnPoint.position,
                tvMonsterSpawnPoint.rotation);
            monster.Spawn(true);
            tvMonsterSpawned = true;
        }
    }
}
