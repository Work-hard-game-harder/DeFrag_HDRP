using UnityEngine;

public enum ItemType
{
    Consumable,
    Key,
    Equipment,
    Clue,
    Props,
}

[CreateAssetMenu(fileName = "New ItemData", menuName = "Inventory/ItemData")]
public class ItemData : ScriptableObject
{
    [Header("아이템 기본 정보")]
    public string itemName;
    public string itemID;
    public Sprite icon;
    public ItemType type;

    [Header("이 아이템을 버리거나 던질 때 필드에 새로 스폰할 프리펩 에셋을 연결해주세요")]
    public GameObject itemPrefab;

    [Header("Held Visual")]
    [Tooltip("Optional first-person visual. The world prefab is used as a fallback.")]
    public GameObject heldPrefab;
    public Vector3 heldLocalPosition = new Vector3(0.35f, -0.3f, 0.65f);
    public Vector3 heldLocalEulerAngles;
    public Vector3 heldLocalScale = Vector3.one;
    [Tooltip("Adds the reusable close-inspection behaviour to the equipped visual.")]
    public bool supportsCloseInspection;

    [Header("Third Person Hand Visual")]
    [Tooltip("상대 플레이어 화면에서 손에 표시할 프리팹입니다. 비어 있으면 Held Prefab, Item Prefab 순서로 사용합니다.")]
    public GameObject worldHeldPrefab;
    public HumanBodyBones attachmentBone = HumanBodyBones.RightHand;
    public Vector3 worldHandLocalPosition;
    public Vector3 worldHandLocalEulerAngles;
    public Vector3 worldHandLocalScale = Vector3.one;

    [Header("Thrown Impact")]
    public AudioClip impactSound;
    [Range(0f, 1f)] public float impactVolume = 1f;
    [Min(0f)] public float impactNoiseRadius = 15f;

    [Header("아이템 설명")]
    [TextArea]
    public string description;
}
