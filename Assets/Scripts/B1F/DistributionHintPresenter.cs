using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    public sealed class DistributionHintPresenter : MonoBehaviour
    {
        private static DistributionHintPresenter instance;
        private TMP_Text hintText;
        private TMP_Text timerText;
        private float remaining;

        public static DistributionHintPresenter GetOrCreate()
        {
            if (instance != null) return instance;

            GameObject canvasObject = new(
                "Distribution Hint Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 125;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            instance = canvasObject.AddComponent<DistributionHintPresenter>();
            instance.Build();
            return instance;
        }

        public static void TryHide()
        {
            if (instance != null) instance.gameObject.SetActive(false);
        }

        public void Show(ushort mask, int bankIndex, float duration)
        {
            remaining = duration;
            hintText.text = BuildHint(mask, bankIndex);
            gameObject.SetActive(true);
            UpdateTimer();
        }

        private void Update()
        {
            remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
            UpdateTimer();
        }

        private void Build()
        {
            GameObject panel = new("Hint Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.73f, 0.2f);
            panelRect.anchorMax = new Vector2(0.98f, 0.88f);
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0f, 0.04f, 0.01f, 0.92f);

            hintText = CreateText("Switch Hint", panel.transform, 26f, TextAlignmentOptions.TopLeft);
            hintText.rectTransform.anchorMin = new Vector2(0.08f, 0.13f);
            hintText.rectTransform.anchorMax = new Vector2(0.92f, 0.9f);
            hintText.rectTransform.offsetMin = hintText.rectTransform.offsetMax = Vector2.zero;

            timerText = CreateText("Timer", panel.transform, 22f, TextAlignmentOptions.BottomLeft);
            timerText.rectTransform.anchorMin = new Vector2(0.08f, 0.03f);
            timerText.rectTransform.anchorMax = new Vector2(0.92f, 0.13f);
            timerText.rectTransform.offsetMin = timerText.rectTransform.offsetMax = Vector2.zero;
        }

        private void UpdateTimer()
        {
            if (timerText != null)
                timerText.text = $"SIGNAL UPDATE: {remaining:00.0}s";
        }

        private static string BuildHint(ushort mask, int bankIndex)
        {
            char bankName = (char)('A' + Mathf.Clamp(bankIndex, 0, 2));
            int firstSwitch = Mathf.Clamp(bankIndex, 0, 2) * 5;
            string result = $"DISTRIBUTION BOX A\nBANK {bankName} SWITCH MAP\n\n";
            for (int i = firstSwitch; i < firstSwitch + 5; i++)
            {
                bool on = (mask & (1 << i)) != 0;
                result += $"KNOB {i + 1:000}  {(on ? "ON" : "OFF")}\n";
            }
            return result;
        }

        private static TMP_Text CreateText(
            string name, Transform parent, float size, TextAlignmentOptions alignment)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = new Color(0.1f, 1f, 0.2f);
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }
    }
}
