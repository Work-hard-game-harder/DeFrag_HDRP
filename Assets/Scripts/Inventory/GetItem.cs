using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class GetItem : MonoBehaviour, IInteractable
{
    [Header("Inventory Item")]
    [Tooltip("The ScriptableObject containing this item's shared data.")]
    [FormerlySerializedAs("item")]
    [FormerlySerializedAs("itemName")]
    [SerializeField] private ItemData itemData;

    [Header("Interaction")]
    [SerializeField] private bool isHoldInteraction;

    [Header("Quest")]
    [Tooltip("Enable this only when picking up this item should advance the active quest.")]
    [SerializeField] private bool progressesQuest;
    [Tooltip("이 아이템 획득이 보고할 퀘스트 신호입니다. 비어 있으면 진행하지 않습니다.")]
    [SerializeField] private string questSignal;
    [SerializeField] private string questSourceId;
    [Min(1)]
    [SerializeField] private int questProgressAmount = 1;

    [Header("World Item")]
    [Tooltip("Whether this item may later be thrown back into the world.")]
    [SerializeField] private bool canThrow = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onPickedUp;

    public ItemData Data => itemData;
    public bool CanThrow => canThrow;

    public void Configure(ItemData data)
    {
        itemData = data;
    }

    public string GetInteractionText()
    {
        string displayName = itemData != null && !string.IsNullOrWhiteSpace(itemData.itemName)
            ? itemData.itemName
            : "아이템";

        return isHoldInteraction
            ? $"{displayName} 줍기 (E 꾹 누르기)"
            : $"{displayName} 줍기 (E)";
    }

    public bool IsHoldInteraction() => isHoldInteraction;

    public void Interact(PlayerInteraction player)
    {
        if (itemData == null)
        {
            Debug.LogError($"[GetItem] '{gameObject.name}'에 ItemData가 할당되지 않았습니다.", this);
            return;
        }

        NetworkWorldItem networkItem = GetComponentInParent<NetworkWorldItem>();
        if (networkItem != null && networkItem.IsSpawned)
        {
            if (player == null)
                return;

            NetworkPlayerInventory networkInventory =
                player.GetComponentInParent<NetworkPlayerInventory>();
            if (networkInventory == null)
            {
                Debug.LogError(
                    "[GetItem] 플레이어에 NetworkPlayerInventory가 없습니다.",
                    player);
                return;
            }

            networkInventory.RequestPickup(networkItem);
            player.CloseAllUI();
            return;
        }

        if (InventoryManager.Instance == null)
        {
            Debug.LogWarning("[GetItem] 씬에 InventoryManager가 없습니다.", this);
            return;
        }

        if (!InventoryManager.Instance.AddItem(itemData))
        {
            // Inventory full: leave the object in the world so it can be collected later.
            return;
        }

        if (player != null && GameDataManager.Instance != null)
        {
            PlayerStats playerStats = player.GetComponent<PlayerStats>();
            playerStats?.SaveData();
        }

        ReportQuestProgress(null);

        onPickedUp?.Invoke();

        if (player != null)
        {
            player.CloseAllUI();
        }

        Destroy(gameObject);
    }

    public void CompleteNetworkPickupPresentation(PlayerInteraction player)
    {
        if (player != null && GameDataManager.Instance != null)
        {
            PlayerStats playerStats = player.GetComponentInParent<PlayerStats>();
            playerStats?.SaveData();
        }

        ReportQuestProgress(GetComponentInParent<NetworkWorldItem>());

        onPickedUp?.Invoke();
        player?.CloseAllUI();
    }

    private void ReportQuestProgress(NetworkWorldItem networkItem)
    {
        if (!progressesQuest || QuestManager.Instance == null ||
            string.IsNullOrWhiteSpace(questSignal))
            return;

        string source = questSourceId;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = networkItem != null && networkItem.IsSpawned
                ? $"WORLD_ITEM_{networkItem.NetworkObjectId}"
                : gameObject.name;
        }

        QuestManager.Instance.ReportProgress(questSignal, source, questProgressAmount);
    }
}
