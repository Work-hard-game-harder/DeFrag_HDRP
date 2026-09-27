using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    /// <summary>
    /// Screen-space treatment for cutscenes: letterbox, CCTV feed dressing, signal-loss static,
    /// RGB tear glitches, impact flash, lens dust, vignette and fade. Built at runtime, local only.
    /// </summary>
    public sealed class CutsceneOverlay : MonoBehaviour
    {
        private const int GlitchBars = 24;
        private static readonly Color GlitchRed = new(1f, 0.16f, 0.2f);
        private static readonly Color GlitchCyan = new(0.2f, 0.95f, 1f);

        private Canvas canvas;
        private RectTransform barTop;
        private RectTransform barBottom;
        private CanvasGroup cctv;
        private TMP_Text cctvLabel;
        private TMP_Text cctvTime;
        private TMP_Text cctvRec;
        private RawImage grain;
        private RawImage staticNoise;
        private TMP_Text noSignal;
        private Image flash;
        private RawImage lensDust;
        private RawImage vignette;
        private Image fade;
        private readonly List<Image> glitchBars = new();
        private Texture2D noiseTexture;
        private Texture2D dustTexture;
        private Texture2D vignetteTexture;

        private float letterbox;
        private float letterboxGoal;
        private float staticAmount;
        private float glitch;
        private float flashAmount;
        private float fadeAmount;
        private float fadeGoal;
        private float fadeSpeed = 3f;
        private float cctvClock;

        public Canvas Canvas => canvas;

        public static CutsceneOverlay Create(Transform parent, int sortingOrder)
        {
            var root = new GameObject("Cutscene Overlay");
            root.transform.SetParent(parent, false);
            var overlay = root.AddComponent<CutsceneOverlay>();
            overlay.Build(sortingOrder);
            return overlay;
        }

        private void Build(int sortingOrder)
        {
            canvas = RuntimeUi.Canvas("Cutscene Canvas", transform, sortingOrder);
            Transform root = canvas.transform;
            noiseTexture = CutsceneTextures.Noise(160, 90);
            dustTexture = CutsceneTextures.LensDust(256, 144);
            vignetteTexture = CutsceneTextures.Vignette(128);

            vignette = RawFull("Vignette", root, vignetteTexture, new Color(0f, 0f, 0f, 0f));

            cctv = new GameObject("CCTV", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
            cctv.transform.SetParent(root, false);
            RuntimeUi.Stretch((RectTransform)cctv.transform);
            grain = RawFull("Grain", cctv.transform, noiseTexture, new Color(0.75f, 0.85f, 0.8f, 0.16f));
            RuntimeUi.Scanlines(cctv.transform, 0.22f);
            var frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(cctv.transform, false);
            RuntimeUi.Stretch(frame, 56f);
            RuntimeUi.AddCornerBrackets(frame, 70f, 4f, new Color(0.85f, 0.95f, 0.9f, 0.85f));
            cctvRec = Label(cctv.transform, "● REC", 34f, TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.84f), new Vector2(0.4f, 0.91f));
            cctvRec.color = new Color(1f, 0.25f, 0.25f);
            cctvLabel = Label(cctv.transform, "", 30f, TextAlignmentOptions.TopRight, new Vector2(0.45f, 0.84f), new Vector2(0.94f, 0.91f));
            cctvTime = Label(cctv.transform, "", 30f, TextAlignmentOptions.BottomRight, new Vector2(0.45f, 0.09f), new Vector2(0.94f, 0.16f));
            cctv.alpha = 0f;

            staticNoise = RawFull("Static", root, noiseTexture, new Color(1f, 1f, 1f, 0f));
            noSignal = Label(root, "신호 없음\n<size=55%>NO SIGNAL  //  CAM 07</size>", 64f, TextAlignmentOptions.Center, new Vector2(0.2f, 0.4f), new Vector2(0.8f, 0.6f));
            noSignal.alpha = 0f;

            for (int i = 0; i < GlitchBars; i++)
            {
                Image bar = RuntimeUi.Panel($"Glitch {i}", root, Color.clear);
                bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = new Vector2(0f, 0f);
                bar.rectTransform.pivot = Vector2.zero;
                glitchBars.Add(bar);
            }

            lensDust = RawFull("Lens Dust", root, dustTexture, new Color(0.62f, 0.56f, 0.52f, 0f));
            flash = RuntimeUi.Panel("Flash", root, new Color(1f, 0.95f, 0.9f, 0f));
            RuntimeUi.Stretch(flash.rectTransform);
            barTop = Bar(root, true);
            barBottom = Bar(root, false);
            fade = RuntimeUi.Panel("Fade", root, Color.black);
            RuntimeUi.Stretch(fade.rectTransform);
            fadeAmount = fadeGoal = 1f;
        }

        private static RawImage RawFull(string name, Transform parent, Texture texture, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(parent, false);
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            RuntimeUi.Stretch(image.rectTransform);
            return image;
        }

        private static TMP_Text Label(Transform parent, string text, float size, TextAlignmentOptions alignment, Vector2 min, Vector2 max)
        {
            TMP_Text label = RuntimeUi.Text(text.Length > 0 ? text.Split('\n')[0] : "Label", parent, size, alignment, null, new Color(0.9f, 0.97f, 0.93f));
            RuntimeUi.Place(label.rectTransform, min, max);
            label.text = text;
            return label;
        }

        private static RectTransform Bar(Transform parent, bool top)
        {
            Image bar = RuntimeUi.Panel(top ? "Letterbox Top" : "Letterbox Bottom", parent, Color.black);
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rect.sizeDelta = Vector2.zero;
            return bar.rectTransform;
        }

        public void SetLetterbox(float amount) => letterboxGoal = amount;
        public void SetStatic(float amount) => staticAmount = Mathf.Clamp01(amount);
        public void SetGlitch(float amount) => glitch = Mathf.Clamp01(amount);
        public void Flash(float amount) => flashAmount = Mathf.Max(flashAmount, amount);
        public void SetLensDust(float amount) => lensDust.color = new Color(lensDust.color.r, lensDust.color.g, lensDust.color.b, Mathf.Clamp01(amount));
        public void SetVignette(float amount) => vignette.color = new Color(0f, 0f, 0f, Mathf.Clamp01(amount));
        public void ShowNoSignal(bool visible) => noSignal.alpha = visible ? 1f : 0f;

        public void FadeTo(float amount, float perSecond)
        {
            fadeGoal = Mathf.Clamp01(amount);
            fadeSpeed = perSecond;
        }

        public void CutToBlack() => fadeAmount = fadeGoal = 1f;

        public void SetCctv(bool visible, string label)
        {
            cctv.alpha = visible ? 1f : 0f;
            cctvLabel.text = label;
            cctvClock = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float height = ((RectTransform)canvas.transform).rect.height;
            letterbox = Mathf.MoveTowards(letterbox, letterboxGoal, dt * 0.35f);
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, height * letterbox);

            fadeAmount = Mathf.MoveTowards(fadeAmount, fadeGoal, dt * fadeSpeed);
            fade.color = new Color(0f, 0f, 0f, fadeAmount);
            flashAmount = Mathf.MoveTowards(flashAmount, 0f, dt * 5f);
            flash.color = new Color(1f, 0.95f, 0.9f, flashAmount);

            bool noisy = staticAmount > 0.001f || cctv.alpha > 0f || glitch > 0.001f;
            if (noisy)
            {
                CutsceneTextures.FillNoise(noiseTexture, 0.25f + 0.75f * staticAmount);
                Rect jitter = new(Random.value, Random.value, 1f, 1f);
                grain.uvRect = jitter;
                staticNoise.uvRect = new Rect(Random.value, Random.value, 1f, 1f);
            }
            staticNoise.color = new Color(0.85f, 0.88f, 0.9f, staticAmount);

            if (cctv.alpha > 0f)
            {
                cctvClock += dt;
                cctvRec.alpha = Mathf.Repeat(Time.time, 1f) < 0.6f ? 1f : 0.15f;
                int frames = Mathf.FloorToInt(cctvClock * 30f) % 30;
                int seconds = 7 + Mathf.FloorToInt(cctvClock);
                cctvTime.text = $"2026-09-27  03:14:{seconds:00}:{frames:00}";
            }

            UpdateGlitch();
        }

        private void UpdateGlitch()
        {
            Vector2 size = ((RectTransform)canvas.transform).rect.size;
            for (int i = 0; i < glitchBars.Count; i++)
            {
                Image bar = glitchBars[i];
                bool on = glitch > 0.001f && Random.value < glitch * 0.9f;
                if (!on)
                {
                    bar.color = Color.clear;
                    continue;
                }
                float h = Mathf.Lerp(2f, 10f + 90f * glitch * glitch, Random.value * Random.value);
                float w = size.x * Mathf.Lerp(0.08f, 1f, Random.value);
                bar.rectTransform.sizeDelta = new Vector2(w, h);
                bar.rectTransform.anchoredPosition = new Vector2(Random.Range(-0.2f, 1f) * size.x, Random.value * size.y);
                float pick = Random.value;
                Color c = pick < 0.4f ? GlitchRed : pick < 0.8f ? GlitchCyan : Color.white;
                c.a = Mathf.Lerp(0.15f, 0.75f, Random.value) * glitch;
                bar.color = c;
            }
        }

        private void OnDestroy()
        {
            if (noiseTexture != null) Destroy(noiseTexture);
            if (dustTexture != null) Destroy(dustTexture);
            if (vignetteTexture != null) Destroy(vignetteTexture);
        }
    }

    /// <summary>Small procedural textures for cutscene overlays and prop screens.</summary>
    public static class CutsceneTextures
    {
        public static Texture2D Noise(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Cutscene Noise",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            FillNoise(texture, 1f);
            return texture;
        }

        private static Color32[] noiseBuffer;

        public static void FillNoise(Texture2D texture, float contrast)
        {
            int count = texture.width * texture.height;
            if (noiseBuffer == null || noiseBuffer.Length != count) noiseBuffer = new Color32[count];
            int width = texture.width;
            for (int y = 0; y < texture.height; y++)
            {
                // Rolling bright bands like a de-synced analogue feed.
                float band = Mathf.Sin((y + Time.time * 60f) * 0.21f) * 0.12f;
                for (int x = 0; x < width; x++)
                {
                    float v = Mathf.Clamp01(0.5f + (Random.value - 0.5f) * contrast + band);
                    byte b = (byte)(v * 255f);
                    noiseBuffer[y * width + x] = new Color32(b, b, b, 255);
                }
            }
            texture.SetPixels32(noiseBuffer);
            texture.Apply(false);
        }

        public static Texture2D LensDust(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Cutscene Lens Dust", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[width * height];
            float ox = Random.value * 100f, oy = Random.value * 100f;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)width, v = y / (float)height;
                    float n = Mathf.PerlinNoise(u * 4f + ox, v * 3f + oy) * 0.6f + Mathf.PerlinNoise(u * 11f + oy, v * 9f + ox) * 0.4f;
                    // Heavier toward the bottom, where debris settles on the lens.
                    float a = Mathf.Clamp01((n - 0.35f) * 2.2f) * Mathf.Lerp(1f, 0.55f, v);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static Texture2D Vignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Cutscene Vignette", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f;
                    float a = Mathf.SmoothStep(0.35f, 1.25f, Mathf.Sqrt(u * u + v * v));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
