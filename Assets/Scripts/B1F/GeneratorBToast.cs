using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    // Local, per-player objective callouts so first-time players always know their next action.
    public sealed class GeneratorBToast : MonoBehaviour
    {
        public static readonly Color Info = new(0.35f, 1f, 0.55f, 1f);
        public static readonly Color Warning = new(1f, 0.35f, 0.2f, 1f);

        private static GeneratorBToast instance;

        private CanvasGroup group;
        private TMP_Text label;
        private Image frame;
        private Coroutine routine;

        public static void Show(string message, Color color, float seconds, TMP_FontAsset font)
        {
            if (instance == null)
                instance = Create();
            instance.Present(message, color, seconds, font);
        }

        private static GeneratorBToast Create()
        {
            GameObject canvasObject = new("Generator B Toast", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 170;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GeneratorBToast toast = canvasObject.AddComponent<GeneratorBToast>();
            toast.group = canvasObject.GetComponent<CanvasGroup>();
            toast.group.blocksRaycasts = false;
            toast.group.interactable = false;

            GameObject panel = new("Toast Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.24f, 0.74f);
            rect.anchorMax = new Vector2(0.76f, 0.9f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            toast.frame = panel.GetComponent<Image>();
            toast.frame.color = new Color(0f, 0.035f, 0.02f, 0.88f);
            OperationPanelStyle.Frame(panel);

            GameObject text = new("Toast Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(panel.transform, false);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28f, 12f);
            textRect.offsetMax = new Vector2(-28f, -12f);
            toast.label = text.GetComponent<TMP_Text>();
            toast.label.alignment = TextAlignmentOptions.Center;
            toast.label.fontSize = 30f;
            toast.label.enableAutoSizing = true;
            toast.label.fontSizeMin = 18f;
            toast.label.fontSizeMax = 30f;
            toast.label.raycastTarget = false;

            toast.group.alpha = 0f;
            return toast;
        }

        private void Present(string message, Color color, float seconds, TMP_FontAsset font)
        {
            if (font != null)
                label.font = font;
            label.text = message;
            label.color = color;
            if (routine != null)
                StopCoroutine(routine);
            routine = StartCoroutine(Animate(seconds));
        }

        private IEnumerator Animate(float seconds)
        {
            for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime)
            {
                group.alpha = t / 0.2f;
                yield return null;
            }
            group.alpha = 1f;
            yield return new WaitForSecondsRealtime(seconds);
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                group.alpha = 1f - t / 0.5f;
                yield return null;
            }
            group.alpha = 0f;
            routine = null;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
