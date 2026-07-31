using System;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public sealed class ConnectionDevice : MonoBehaviour, IInteractable
{
    [Header("Terminal Identity")]
    [SerializeField] private string terminalId = "terminal_01";
    [SerializeField] private string displayName = "TERMINAL 01";

    [Header("Access")]
    [SerializeField] private ItemData requiredHackingPad;
    [SerializeField] private string interactionText = "해킹패드 연결 (E 길게 누르기)";

    [Header("Command Minigames")]
    [SerializeField] private bool unlockDoorEnabled;
    [Tooltip("Leave empty to show DENIED ACCESS for this command.")]
    [SerializeField] private HackingMinigameBase unlockDoorMinigame;
    [SerializeField] private bool downloadDataEnabled;
    [Tooltip("Leave empty to show DENIED ACCESS for this command.")]
    [SerializeField] private HackingMinigameBase downloadDataMinigame;
    [SerializeField] private bool connectServerEnabled;
    [Tooltip("Leave empty to show DENIED ACCESS for this command.")]
    [SerializeField] private HackingMinigameBase connectServerMinigame;

    [Header("Authoritative Result Requests")]
    [SerializeField] private UnityEvent onUnlockDoorRequested = new();
    [SerializeField] private UnityEvent onDownloadDataRequested = new();
    [SerializeField] private UnityEvent onConnectServerRequested = new();

    private EquipmentController equipment;
    private int interactableLayer;
    private int inactiveLayer;
    private TerminalCommands completedCommands;

    public string TerminalId => terminalId;
    public string DisplayName => displayName;
    public int ArchiveNumber => TerminalArchiveNumberRegistry.GetNumber(this);
    public event Action<ConnectionDevice, TerminalCommands> CommandCompletionRequested;

    public bool IsCompleted(TerminalCommands command)
    {
        return (completedCommands & command) != 0;
    }

    public HackingMinigameBase GetMinigame(TerminalCommands command)
    {
        return command switch
        {
            TerminalCommands.UnlockDoor => unlockDoorMinigame,
            TerminalCommands.DownloadData => downloadDataMinigame,
            TerminalCommands.ConnectServer => connectServerMinigame,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };
    }

    public bool IsCommandEnabled(TerminalCommands command)
    {
        return command switch
        {
            TerminalCommands.UnlockDoor => unlockDoorEnabled,
            TerminalCommands.DownloadData => downloadDataEnabled,
            TerminalCommands.ConnectServer => connectServerEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };
    }

    private void Awake()
    {
        interactableLayer = LayerMask.NameToLayer("Interactable");
        inactiveLayer = LayerMask.NameToLayer("Default");
        FitColliderToModel();
    }

    private void Update()
    {
        if (equipment == null && Camera.main != null)
            equipment = Camera.main.GetComponent<EquipmentController>();

        int desiredLayer = IsHackingPadBeingInspected() ? interactableLayer : inactiveLayer;
        if (gameObject.layer != desiredLayer)
            gameObject.layer = desiredLayer;
    }

    public string GetInteractionText() => $"{displayName} - {interactionText}";
    public bool IsHoldInteraction() => true;

    public void Interact(PlayerInteraction player)
    {
        if (!IsHackingPadBeingInspected())
            return;

        player.CloseAllUI();
        Camera.main.GetComponent<HackingSessionController>()
            .Begin(equipment.HeldVisual, this);
    }

    public void RequestCommandCompletion(TerminalCommands command)
    {
        if (IsCompleted(command))
            return;

        completedCommands |= command;
        CommandCompletionRequested?.Invoke(this, command);

        switch (command)
        {
            case TerminalCommands.UnlockDoor:
                onUnlockDoorRequested.Invoke();
                break;
            case TerminalCommands.DownloadData:
                onDownloadDataRequested.Invoke();
                break;
            case TerminalCommands.ConnectServer:
                onConnectServerRequested.Invoke();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private bool IsHackingPadBeingInspected()
    {
        if (equipment == null || equipment.EquippedData != requiredHackingPad)
            return false;

        return equipment.HeldVisual.GetComponent<HackingPadHeldController>().IsInspecting;
    }

    private void FitColliderToModel()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        box.center = transform.InverseTransformPoint(bounds.center);
        Vector3 scale = transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Abs(scale.x),
            bounds.size.y / Mathf.Abs(scale.y),
            bounds.size.z / Mathf.Abs(scale.z));
        box.isTrigger = false;
    }
}
