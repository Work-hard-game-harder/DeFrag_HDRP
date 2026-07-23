using UnityEngine;

namespace DeFrag.Doors
{
    [RequireComponent(typeof(VerticalDoorMotor))]
    public sealed class TerminalDoorLock : MonoBehaviour
    {
        private VerticalDoorMotor door;

        public bool IsUnlocked { get; private set; }

        private void Awake()
        {
            door = GetComponent<VerticalDoorMotor>();
        }

        public void Unlock()
        {
            if (IsUnlocked)
                return;

            IsUnlocked = true;
            door.Open();
        }
    }
}
