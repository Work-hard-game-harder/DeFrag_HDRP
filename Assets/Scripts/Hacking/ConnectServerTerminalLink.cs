using UnityEngine;

[DisallowMultipleComponent]
public sealed class ConnectServerTerminalLink : MonoBehaviour
{
    [SerializeField] private ConnectServerCoordinator coordinator;

    public ConnectServerCoordinator Coordinator => coordinator;
}
