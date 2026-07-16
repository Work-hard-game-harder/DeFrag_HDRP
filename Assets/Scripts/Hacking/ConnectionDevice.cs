using UnityEngine;

/// <summary>
/// First hacking vertical slice: only becomes interactable while the hacking
/// pad is equipped and being inspected, then requests a local hacking session.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class ConnectionDevice : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData requiredHackingPad;
    [SerializeField] private string interactionText = "해킹패드 연결 (E 길게 누르기)";
    [Tooltip("Optional full-screen minigame prefab for this device. Assign one when it is ready.")]
    [SerializeField] private HackingMinigameBase minigamePrefab;

    private EquipmentController equipment;
    private int interactableLayer;
    private int inactiveLayer;

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
        if (desiredLayer >= 0 && gameObject.layer != desiredLayer)
            gameObject.layer = desiredLayer;
    }

    public string GetInteractionText() => interactionText;

    public bool IsHoldInteraction() => true;

    public void Interact(PlayerInteraction player)
    {
        if (!IsHackingPadBeingInspected() || equipment.HeldVisual == null) return;

        Camera playerCamera = Camera.main;
        if (playerCamera == null) return;

        HackingSessionController session = playerCamera.GetComponent<HackingSessionController>();
        if (session == null) session = playerCamera.gameObject.AddComponent<HackingSessionController>();

        player?.CloseAllUI();
        session.Begin(equipment.HeldVisual, this, minigamePrefab);
    }

    private bool IsHackingPadEquipped()
    {
        ItemData equipped = equipment != null ? equipment.EquippedData : null;
        if (equipped == null) return false;
        return requiredHackingPad != null && equipped == requiredHackingPad;
    }

    private bool IsHackingPadBeingInspected()
    {
        if (!IsHackingPadEquipped() || equipment.HeldVisual == null) return false;
        HackingPadHeldController heldController =
            equipment.HeldVisual.GetComponent<HackingPadHeldController>();
        return heldController != null && heldController.IsInspecting;
    }

    private void FitColliderToModel()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (box == null || renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        box.center = transform.InverseTransformPoint(bounds.center);
        Vector3 scale = transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        box.isTrigger = false;
    }
}
