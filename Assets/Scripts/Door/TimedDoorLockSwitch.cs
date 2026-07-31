using Unity.Netcode;
using UnityEngine;

namespace DeFrag.Doors
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class TimedDoorLockSwitch : NetworkBehaviour, IInteractable
    {
        [Header("Interaction")]
        [SerializeField] private string interactionText = "문 잠그기 (E)";
        [SerializeField] private string lockedText = "문이 잠겨 있습니다";

        [Header("Target")]
        [SerializeField] private VerticalDoorMotor targetDoor;
        [SerializeField, Min(0f)] private float lockDuration = 3f;

        public string GetInteractionText()
        {
            return targetDoor.IsTemporarilyLocked ? lockedText : interactionText;
        }

        public bool IsHoldInteraction() => false;

        public void Interact(PlayerInteraction player)
        {
            if (targetDoor.IsTemporarilyLocked)
                return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (IsServer)
                    LockDoorClientRpc();
                else
                    RequestLockServerRpc();

                return;
            }

            targetDoor.LockClosed(lockDuration);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestLockServerRpc()
        {
            if (!targetDoor.IsTemporarilyLocked)
                LockDoorClientRpc();
        }

        [ClientRpc]
        private void LockDoorClientRpc()
        {
            targetDoor.LockClosed(lockDuration);
        }
    }
}
