using UnityEngine;
using UnityEngine.UI;
using DeFrag.UI;

/// <summary>
/// 플레이 중 항상 표시되는 최소 형태의 마이크 입력 HUD입니다.
/// 화면 오른쪽 아래의 흰색 Bar 길이로 현재 입력 레벨을 보여줍니다.
/// </summary>
public class MicInputLevelMeter : MonoBehaviour
{
    [SerializeField] private Vector2 size = new Vector2(180f, 10f);
    [SerializeField] private Vector2 screenMargin = new Vector2(32f, 32f);
    [SerializeField] private Color backgroundColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color fillColor = Color.white;

    private RectTransform fillRect;

    private void Awake()
    {
        BuildHUD();
    }

    private void Update()
    {
        if (fillRect == null) return;

        SettingManager manager = SettingManager.Instance;
        float level = manager != null ? Mathf.Clamp01(manager.MicInputLevel) : 0f;
        fillRect.anchorMax = new Vector2(level, 1f);
    }

    private void BuildHUD()
    {
        GameObject canvasObject = new GameObject("InGame Mic Level HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.layer = LayerMask.NameToLayer("UI");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        ResponsiveCanvasUtility.Configure(scaler);

        GameObject barObject = CreateImage("Mic Level", canvasObject.transform, backgroundColor);
        RectTransform barRect = (RectTransform)barObject.transform;
        barRect.anchorMin = Vector2.one;
        barRect.anchorMax = Vector2.one;
        barRect.pivot = Vector2.one;
        barRect.anchoredPosition = -screenMargin;
        barRect.sizeDelta = size;

        GameObject fillObject = CreateImage("Fill", barRect, fillColor);
        fillRect = (RectTransform)fillObject.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    private static GameObject CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.layer = parent.gameObject.layer;
        imageObject.transform.SetParent(parent, false);

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return imageObject;
    }
}
