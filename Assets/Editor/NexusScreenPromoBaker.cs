using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

/// <summary>
/// Bakes the LobbyF wall-screen loops: a white, corporate "Nexus" promo in the spirit of a
/// clean lab-company advert. Two Artlist stills (Source~ folder, ignored by Unity) are framed at
/// each screen's own aspect ratio, slowly moved and overlaid with type, then encoded as H.264
/// Baseline so the existing <c>MaterialVideoPlaylistPlayer</c> setup plays them without bars.
/// Menu: DEFRAG > LobbyF > Bake Nexus Screen Videos.
/// </summary>
public static class NexusScreenPromoBaker
{
    public const string WideVideoPath = "Assets/Movies/Screens1080/Nexus_Wide_Promo.mp4";
    public const string PortraitVideoPath = "Assets/Movies/Screens1080/Nexus_Portrait_Atlas.mp4";
    // The large screen is ~3.08:1 and the five wall screens ~0.72:1; sizes are multiples of 16.
    public static readonly Vector2Int WideSize = new(2464, 800);
    public static readonly Vector2Int PortraitCellSize = new(720, 992);
    public const int PortraitCells = 5;

    private const string SourceFolder = "Assets/Art/LobbyF/Screens/Source~/";
    private const string FontPath = "Assets/UISource/NanumSquareB SDF.asset";
    private const int FrameRate = 30;
    private const double FrameBudgetSeconds = 0.25;

    private static BakeJob job;

    [MenuItem("DEFRAG/LobbyF/Bake Nexus Screen Videos")]
    public static void BakeAll()
    {
        if (job != null)
        {
            Debug.LogWarning("[NexusScreenPromoBaker] A bake is already running.");
            return;
        }
        var kit = PromoKit.Load();
        if (kit == null) return;
        job = new BakeJob(kit, new PromoComposition[] { new WidePromo(), new PortraitAtlasPromo() });
        EditorApplication.update += Step;
    }

    public static bool IsBaking => job != null;

    /// <summary>Writes PNG stills of both layouts at the given times, for checking a change before a full bake.</summary>
    public static void RenderStills(string folder, params float[] times)
    {
        var kit = PromoKit.Load();
        if (kit == null) return;
        Directory.CreateDirectory(folder);
        try
        {
            foreach (PromoComposition composition in new PromoComposition[] { new WidePromo(), new PortraitAtlasPromo() })
            {
                using var stage = new Stage(composition.Size);
                composition.Build(stage.Root, kit);
                composition.Pose(times.Length > 0 ? times[0] : 0f);
                stage.WarmUp();
                foreach (float time in times)
                {
                    composition.Pose(time);
                    string name = $"{Path.GetFileNameWithoutExtension(composition.OutputPath)}_{time:00.0}s.png";
                    // The UI writes partial alpha; the encoder ignores it, so make the still opaque the same way.
                    Texture2D frame = stage.Render();
                    Color32[] pixels = frame.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++) pixels[i].a = 255;
                    frame.SetPixels32(pixels);
                    File.WriteAllBytes(Path.Combine(folder, name), frame.EncodeToPNG());
                }
            }
        }
        finally
        {
            kit.Dispose();
        }
    }

    private static void Step()
    {
        bool finished;
        try
        {
            finished = job.Step(FrameBudgetSeconds);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            job.Abort();
            finished = true;
        }
        if (!finished) return;
        EditorApplication.update -= Step;
        job = null;
        EditorUtility.ClearProgressBar();
    }

    private sealed class BakeJob
    {
        private readonly PromoKit kit;
        private readonly Queue<PromoComposition> pending;
        private PromoComposition current;
        private Stage stage;
        private MediaEncoder encoder;
        private string tempPath;
        private int frame;
        private int frameCount;

        public BakeJob(PromoKit kit, IEnumerable<PromoComposition> compositions)
        {
            this.kit = kit;
            pending = new Queue<PromoComposition>(compositions);
        }

        /// <summary>Renders frames until the time budget runs out; true once everything is written.</summary>
        public bool Step(double budget)
        {
            double until = EditorApplication.timeSinceStartup + budget;
            while (EditorApplication.timeSinceStartup < until)
            {
                if (current == null)
                {
                    if (pending.Count == 0)
                    {
                        kit.Dispose();
                        Debug.Log("[NexusScreenPromoBaker] All screen videos baked.");
                        return true;
                    }
                    Begin(pending.Dequeue());
                }

                current.Pose(frame / (float)FrameRate);
                encoder.AddFrame(stage.Render());
                frame++;
                EditorUtility.DisplayProgressBar("Baking Nexus screen videos", $"{Path.GetFileName(current.OutputPath)}  {frame}/{frameCount}",
                    frame / (float)frameCount);
                if (frame >= frameCount) Finish();
            }
            return false;
        }

        private void Begin(PromoComposition composition)
        {
            current = composition;
            stage = new Stage(composition.Size);
            composition.Build(stage.Root, kit);
            composition.Pose(0f);
            stage.WarmUp();
            frame = 0;
            frameCount = Mathf.RoundToInt(composition.Duration * FrameRate);
            tempPath = Path.Combine(Path.GetTempPath(), "defrag_" + Path.GetFileName(composition.OutputPath));
            if (File.Exists(tempPath)) File.Delete(tempPath);
            var h264 = new H264EncoderAttributes
            {
                gopSize = 60,
                numConsecutiveBFrames = 0,          // Unity's Windows decoder stalls on reordered frames.
                profile = VideoEncodingProfile.H264Baseline
            };
            var attributes = new VideoTrackEncoderAttributes(h264)
            {
                frameRate = new MediaRational(FrameRate),
                width = (uint)composition.Size.x,
                height = (uint)composition.Size.y,
                includeAlpha = false,
                bitRateMode = VideoBitrateMode.High
            };
            encoder = new MediaEncoder(tempPath, attributes);
        }

        private void Finish()
        {
            encoder.Dispose();
            encoder = null;
            stage.Dispose();
            stage = null;
            string target = Path.GetFullPath(current.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(tempPath, target, true);
            File.Delete(tempPath);
            AssetDatabase.ImportAsset(current.OutputPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[NexusScreenPromoBaker] Wrote {current.OutputPath} ({current.Size.x}x{current.Size.y}, {current.Duration:0.#}s)");
            current = null;
        }

        public void Abort()
        {
            encoder?.Dispose();
            stage?.Dispose();
            kit.Dispose();
        }
    }

    /// <summary>A hidden camera and pixel-exact canvas that render one frame into a readable texture.</summary>
    private sealed class Stage : IDisposable
    {
        private const int UiLayer = 5;
        private readonly GameObject root;
        private readonly Camera camera;
        private readonly Canvas canvas;
        private readonly RenderTexture target;
        private readonly Texture2D readback;

        public RectTransform Root { get; }

        public Stage(Vector2Int size)
        {
            target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { name = "Nexus Promo Frame" };
            target.Create();
            readback = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };

            root = new GameObject("Nexus Promo Stage") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0f, -20000f, 0f);
            camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.gameObject.hideFlags = HideFlags.HideAndDontSave;
            camera.transform.SetParent(root.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.cullingMask = 1 << UiLayer;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 2f;
            camera.targetTexture = target;
            camera.enabled = false;
            var data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = Color.white;
            data.volumeLayerMask = 0;
            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler))
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = UiLayer
            };
            canvasObject.transform.SetParent(root.transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.5f;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            Root = (RectTransform)canvasObject.transform;
        }

        /// <summary>HDRP returns black for the first frames of a new camera; render a few before capturing.</summary>
        public void WarmUp()
        {
            for (int i = 0; i < 8; i++) Render();
        }

        public Texture2D Render()
        {
            SetLayer(Root);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            readback.Apply(false);
            RenderTexture.active = previous;
            return readback;
        }

        private static void SetLayer(Transform node)
        {
            if (node.gameObject.layer != UiLayer) node.gameObject.layer = UiLayer;
            node.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (Transform child in node) SetLayer(child);
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(root);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(readback);
        }
    }

    /// <summary>Shared look: palette, font, source stills and small procedural shapes.</summary>
    private sealed class PromoKit : IDisposable
    {
        public static readonly Color Paper = new(0.965f, 0.973f, 0.98f);
        public static readonly Color Ink = new(0.15f, 0.19f, 0.23f);
        public static readonly Color InkSoft = new(0.36f, 0.41f, 0.47f);
        public static readonly Color Accent = new(0.23f, 0.62f, 0.88f);

        private readonly List<UnityEngine.Object> owned = new();

        public TMP_FontAsset Font { get; private set; }
        public Texture2D Wide { get; private set; }
        public Texture2D Portraits { get; private set; }
        public Sprite ThinRing { get; private set; }
        public Sprite Dot { get; private set; }
        public Sprite FadeUp { get; private set; }      // opaque at the bottom, clear at the top
        public Sprite FadeRight { get; private set; }   // opaque at the left, clear at the right

        public static PromoKit Load()
        {
            var kit = new PromoKit { Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) };
            kit.Wide = kit.LoadStill("Nexus_Wide_Atrium.png");
            kit.Portraits = kit.LoadStill("Nexus_Portrait_Set.png");
            if (kit.Font == null || kit.Wide == null || kit.Portraits == null)
            {
                Debug.LogError($"[NexusScreenPromoBaker] Missing font ({FontPath}) or source stills in {SourceFolder}.");
                kit.Dispose();
                return null;
            }
            kit.ThinRing = kit.Shape("Thin Ring", 256, (x, y) =>
            {
                float r = new Vector2(x - 0.5f, y - 0.5f).magnitude * 2f;
                return Mathf.Clamp01((0.985f - r) * 128f) * Mathf.Clamp01((r - 0.9f) * 128f);
            });
            kit.Dot = kit.Shape("Dot", 64, (x, y) => Mathf.Clamp01((1f - new Vector2(x - 0.5f, y - 0.5f).magnitude * 2f) * 32f));
            kit.FadeUp = kit.Shape("Fade Up", 64, (x, y) => Mathf.SmoothStep(1f, 0f, y));
            kit.FadeRight = kit.Shape("Fade Right", 64, (x, y) => Mathf.SmoothStep(1f, 0f, x));
            return kit;
        }

        private Texture2D LoadStill(string file)
        {
            string path = SourceFolder + file;
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = file
            };
            texture.LoadImage(File.ReadAllBytes(path));
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            texture.wrapMode = TextureWrapMode.Clamp;
            owned.Add(texture);
            return texture;
        }

        private Sprite Shape(string name, int size, Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha((x + 0.5f) / size, (y + 0.5f) / size)) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply(true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(texture);
            owned.Add(sprite);
            return sprite;
        }

        public void Dispose()
        {
            foreach (UnityEngine.Object item in owned)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            owned.Clear();
        }
    }

    private abstract class PromoComposition
    {
        public abstract string OutputPath { get; }
        public abstract Vector2Int Size { get; }
        public abstract float Duration { get; }
        public abstract void Build(RectTransform root, PromoKit kit);
        public abstract void Pose(float time);

        // ---- small builders and timing helpers shared by both layouts ----

        protected static RectTransform Node(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.transform.SetParent(parent, false);
            return (RectTransform)node.transform;
        }

        protected static Image Box(string name, Transform parent, Color color, Sprite sprite = null)
        {
            Image image = Node(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        protected static TMP_Text Label(Transform parent, PromoKit kit, string text, float size, Color color,
            TextAlignmentOptions alignment, float spacing = 0f)
        {
            TextMeshProUGUI label = Node(text, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = kit.Font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.characterSpacing = spacing;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Anchors a rect to a point of its parent with a pixel size and offset.</summary>
        protected static T At<T>(T graphic, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size) where T : Component
        {
            var rect = (RectTransform)graphic.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return graphic;
        }

        protected static void Fill(Component graphic, Vector2 min, Vector2 max)
        {
            var rect = (RectTransform)graphic.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        protected static float Ease(float t) => t * t * (3f - 2f * t);
        protected static float Ramp(float from, float to, float t) => Ease(Mathf.Clamp01((t - from) / (to - from)));
        /// <summary>0 before fadeIn, 1 between the fades, 0 after fadeOut.</summary>
        protected static float Window(float t, float inStart, float inEnd, float outStart, float outEnd) =>
            Ramp(inStart, inEnd, t) * (1f - Ramp(outStart, outEnd, t));

        protected static void SetAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        protected static void Show(CanvasGroup group, float alpha) => group.alpha = alpha;

        protected static CanvasGroup Group(string name, Transform parent)
        {
            RectTransform node = Node(name, parent);
            node.anchorMin = Vector2.zero;
            node.anchorMax = Vector2.one;
            node.offsetMin = node.offsetMax = Vector2.zero;
            return node.gameObject.AddComponent<CanvasGroup>();
        }
    }

    /// <summary>The ~3:1 lobby screen: atrium pan, a data-sphere HUD beat, then a white end card.</summary>
    private sealed class WidePromo : PromoComposition
    {
        private const float Length = 40f;
        private const float PanEnd = 29.5f;
        // The data sphere in the still, in 0..1 image coordinates (measured from the render).
        private static readonly Vector2 SphereCenter = new(0.875f, 0.59f);
        private const float SphereDiameterV = 0.56f;

        private RawImage atrium;
        private float sourceAspect;
        private CanvasGroup opening;
        private CanvasGroup network;
        private CanvasGroup endCard;
        private Image networkShade;
        private RectTransform sphereFrame;
        private RectTransform openingRule;
        private RectTransform endRule;
        private TMP_Text throughput;
        private TMP_Text profiles;
        private TMP_Text sync;
        private TMP_Text[] openingTitle;
        private Image whiteOut;
        private readonly List<RectTransform> streaks = new();

        public override string OutputPath => WideVideoPath;
        public override Vector2Int Size => WideSize;
        public override float Duration => Length;

        public override void Build(RectTransform root, PromoKit kit)
        {
            Fill(Box("Paper", root, PromoKit.Paper), Vector2.zero, Vector2.one);
            atrium = Node("Atrium", root).gameObject.AddComponent<RawImage>();
            atrium.texture = kit.Wide;
            atrium.raycastTarget = false;
            Fill(atrium, Vector2.zero, Vector2.one);
            sourceAspect = kit.Wide.width / (float)kit.Wide.height;

            // Faint data streaks drifting across the whole frame.
            for (int i = 0; i < 7; i++)
            {
                Image streak = Box($"Streak {i}", root, new Color(PromoKit.Accent.r, PromoKit.Accent.g, PromoKit.Accent.b, 0.22f), kit.FadeRight);
                streak.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
                streaks.Add((RectTransform)At(streak, new Vector2(0f, 0.12f + i * 0.12f), new Vector2(0f, 0.5f), Vector2.zero,
                    new Vector2(260f + 90f * (i % 3), 2f)).transform);
            }

            // Opening: logotype and tagline on the empty white wall at the left.
            opening = Group("Opening", root);
            Image openingShade = Box("Shade", opening.transform, new Color(1f, 1f, 1f, 0.55f), kit.FadeRight);
            Fill(openingShade, Vector2.zero, new Vector2(0.5f, 1f));
            At(Box("Logo Ring", opening.transform, PromoKit.Ink, kit.ThinRing), new Vector2(0.06f, 0.63f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(118f, 118f));
            At(Box("Logo Dot", opening.transform, PromoKit.Accent, kit.Dot), new Vector2(0.06f, 0.63f), new Vector2(0.5f, 0.5f), new Vector2(118f, 0f), new Vector2(20f, 20f));
            openingTitle = new[]
            {
                At(Label(opening.transform, kit, "NEXUS", 118f, PromoKit.Ink, TextAlignmentOptions.MidlineLeft, 34f),
                    new Vector2(0.06f, 0.63f), new Vector2(0f, 0.5f), new Vector2(160f, 0f), new Vector2(900f, 140f)),
                At(Label(opening.transform, kit, "DATA  SCIENCES", 26f, PromoKit.InkSoft, TextAlignmentOptions.MidlineLeft, 60f),
                    new Vector2(0.06f, 0.5f), new Vector2(0f, 0.5f), new Vector2(166f, 0f), new Vector2(900f, 40f)),
            };
            openingRule = (RectTransform)At(Box("Rule", opening.transform, PromoKit.Accent), new Vector2(0.06f, 0.42f), new Vector2(0f, 0.5f),
                new Vector2(4f, 0f), new Vector2(0f, 3f)).transform;
            At(Label(opening.transform, kit, "당신의 데이터가 미래를 만듭니다.", 50f, PromoKit.Ink, TextAlignmentOptions.MidlineLeft),
                new Vector2(0.06f, 0.33f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1000f, 70f));
            At(Label(opening.transform, kit, "Your data builds the future.", 26f, PromoKit.InkSoft, TextAlignmentOptions.MidlineLeft, 6f),
                new Vector2(0.06f, 0.24f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(1000f, 40f));

            // Network beat: statistics on the left, a tracking frame around the data sphere.
            network = Group("Network", root);
            networkShade = Box("Shade", network.transform, new Color(1f, 1f, 1f, 0.8f), kit.FadeRight);
            Fill(networkShade, Vector2.zero, new Vector2(0.42f, 1f));
            At(Label(network.transform, kit, "모든 기록은 학습이 됩니다.", 54f, PromoKit.Ink, TextAlignmentOptions.MidlineLeft),
                new Vector2(0.06f, 0.74f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1100f, 80f));
            At(Label(network.transform, kit, "Every record becomes knowledge.", 26f, PromoKit.InkSoft, TextAlignmentOptions.MidlineLeft, 6f),
                new Vector2(0.06f, 0.645f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(1100f, 40f));
            Stat(network.transform, kit, "REAL-TIME THROUGHPUT", 0.46f, out throughput);
            Stat(network.transform, kit, "PROFILES INDEXED", 0.24f, out profiles);

            sphereFrame = Node("Sphere Frame", network.transform);
            sphereFrame.anchorMin = sphereFrame.anchorMax = Vector2.zero;
            sphereFrame.pivot = new Vector2(0.5f, 0.5f);
            Color bracket = new(PromoKit.Accent.r, PromoKit.Accent.g, PromoKit.Accent.b, 0.9f);
            foreach (Vector2 corner in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
            {
                Vector2 inward = new(corner.x > 0f ? -1f : 1f, corner.y > 0f ? -1f : 1f);
                At(Box("Bracket H", sphereFrame, bracket), corner, new Vector2(corner.x, 0.5f), new Vector2(0f, inward.y * 1.5f), new Vector2(44f, 3f));
                At(Box("Bracket V", sphereFrame, bracket), corner, new Vector2(0.5f, corner.y), new Vector2(inward.x * 1.5f, 0f), new Vector2(3f, 44f));
            }
            At(Label(sphereFrame, kit, "NX GLOBAL MESH", 22f, PromoKit.Ink, TextAlignmentOptions.BottomLeft, 30f),
                new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 12f), new Vector2(600f, 30f));
            sync = At(Label(sphereFrame, kit, "", 20f, PromoKit.InkSoft, TextAlignmentOptions.TopLeft, 8f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, -12f), new Vector2(600f, 30f));

            // End card on white.
            whiteOut = Box("White", root, PromoKit.Paper);
            Fill(whiteOut, Vector2.zero, Vector2.one);
            endCard = Group("End Card", root);
            At(Box("Logo Ring", endCard.transform, PromoKit.Ink, kit.ThinRing), new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), new Vector2(-330f, 0f), new Vector2(128f, 128f));
            At(Box("Logo Dot", endCard.transform, PromoKit.Accent, kit.Dot), new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), new Vector2(-266f, 0f), new Vector2(22f, 22f));
            At(Label(endCard.transform, kit, "NEXUS", 132f, PromoKit.Ink, TextAlignmentOptions.MidlineLeft, 40f),
                new Vector2(0.5f, 0.6f), new Vector2(0f, 0.5f), new Vector2(-240f, 0f), new Vector2(900f, 150f));
            At(Label(endCard.transform, kit, "Intelligence, refined.", 34f, PromoKit.InkSoft, TextAlignmentOptions.Center, 10f),
                new Vector2(0.5f, 0.4f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 50f));
            At(Label(endCard.transform, kit, "넥서스 데이터 사이언스  ·  인간을 이해하는 AI", 28f, PromoKit.InkSoft, TextAlignmentOptions.Center),
                new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 44f));
            endRule = (RectTransform)At(Box("Rule", endCard.transform, PromoKit.Accent), new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(0f, 3f)).transform;
        }

        private static void Stat(Transform parent, PromoKit kit, string caption, float y, out TMP_Text value)
        {
            At(Box("Stat Dot", parent, PromoKit.Accent, kit.Dot), new Vector2(0.06f, y + 0.07f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(12f, 12f));
            At(Label(parent, kit, caption, 22f, PromoKit.Accent, TextAlignmentOptions.MidlineLeft, 34f),
                new Vector2(0.06f, y + 0.07f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(800f, 30f));
            value = At(Label(parent, kit, "", 64f, PromoKit.Ink, TextAlignmentOptions.MidlineLeft, 2f),
                new Vector2(0.06f, y), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(900f, 80f));
        }

        public override void Pose(float t)
        {
            // Camera move over the atrium: a slow push-out while panning toward the data sphere.
            // It resets while the frame is fully white, so the loop point is invisible.
            float pan = t < PanEnd ? Ease(Mathf.Clamp01(t / PanEnd)) : 0f;
            float zoom = Mathf.Lerp(1.07f, 1f, pan);
            float viewH = 1f / zoom;
            float viewW = viewH * (Size.x / (float)Size.y) / sourceAspect;
            var view = new Rect(Mathf.Lerp(0f, 1f - viewW, pan), (1f - viewH) * 0.5f, viewW, viewH);
            atrium.uvRect = view;

            for (int i = 0; i < streaks.Count; i++)
            {
                float speed = 0.018f + 0.007f * (i % 4);
                float x = Mathf.Repeat(i * 0.37f + t * speed, 1.3f) - 0.15f;
                streaks[i].anchorMin = streaks[i].anchorMax = new Vector2(x, streaks[i].anchorMin.y);
            }

            float openingAlpha = Window(t, 0.6f, 2.4f, 11.5f, 13.2f);
            Show(opening, openingAlpha);
            float spread = Mathf.Lerp(70f, 34f, Ramp(0.3f, 3.2f, t));
            openingTitle[0].characterSpacing = spread;
            openingRule.sizeDelta = new Vector2(Mathf.Lerp(0f, 560f, Ramp(1.6f, 4.2f, t)), 3f);

            float networkAlpha = Window(t, 14.4f, 16.2f, 26.4f, 28.2f);
            Show(network, networkAlpha);
            float counting = Mathf.Max(0f, t - 14f);
            throughput.text = $"{4812.36 + counting * 0.731:#,0.00} PB";
            profiles.text = $"{2184337912L + (long)(counting * 3187.4f):#,0}";
            sync.text = $"NODE 0x7F3A  ·  SYNC {99.91f + 0.005f * Mathf.Floor(counting * 2f) % 0.08f:0.00}%";

            // Keep the brackets locked onto the sphere as the view moves.
            Vector2 sphere = new((SphereCenter.x - view.x) / view.width, (SphereCenter.y - view.y) / view.height);
            float radius = SphereDiameterV / view.height * Size.y * 0.5f;
            float breathe = 1f + 0.03f * Mathf.Sin(t * 1.7f);
            sphereFrame.anchoredPosition = new Vector2(sphere.x * Size.x, sphere.y * Size.y);
            sphereFrame.sizeDelta = new Vector2(radius * 2.2f, radius * 2.2f) * breathe;

            float white = Ramp(28.4f, 29.5f, t) * (1f - Ramp(38.6f, 40f, t));
            SetAlpha(whiteOut, white);
            Show(endCard, Window(t, 29.9f, 31.6f, 37.4f, 38.5f));
            endRule.sizeDelta = new Vector2(Mathf.Lerp(0f, 900f, Ramp(30.2f, 37.8f, t)), 3f);
        }
    }

    /// <summary>Five portrait posters side by side, one per wall screen, each on its own staggered cycle.</summary>
    private sealed class PortraitAtlasPromo : PromoComposition
    {
        private const float Cycle = 30f;
        private const float Stagger = 6f;

        private readonly struct Poster
        {
            public Poster(int left, int right, float focusX, string tag, string headline, string line)
            {
                Left = left;
                Right = right;
                FocusX = focusX;
                Tag = tag;
                Headline = headline;
                Line = line;
            }

            public int Left { get; }        // pixel columns of the panel inside the source still
            public int Right { get; }
            public float FocusX { get; }    // 0..1 across the panel
            public string Tag { get; }
            public string Headline { get; }
            public string Line { get; }
        }

        // Panel edges measured on the white gutters of Nexus_Portrait_Set.png (8256x2048), inset slightly.
        private static readonly Poster[] Posters =
        {
            new(14, 1606, 0.45f, "01  HUMAN", "인간을 이해하는 AI", "We understand you. Completely."),
            new(1662, 3280, 0.5f, "02  DATA", "하루 4.8 페타바이트", "Nothing is ever forgotten."),
            new(3316, 4938, 0.5f, "03  MIND", "스스로 배우는 신경망", "NX-7 neural architecture"),
            new(4992, 6590, 0.45f, "04  RESEARCH", "더 나은 내일을 연구합니다", "Research for a better tomorrow."),
            new(6630, 8242, 0.55f, "05  FUTURE", "미래는 이미 여기에", "The future is already here."),
        };

        private sealed class Cell
        {
            public RawImage Photo;
            public CanvasGroup Copy;
            public RectTransform CopyRoot;
            public RectTransform Rule;
            public Image White;
        }

        private readonly List<Cell> cells = new();
        private Vector2 sourceSize;

        public override string OutputPath => PortraitVideoPath;
        public override Vector2Int Size => new(PortraitCellSize.x * PortraitCells, PortraitCellSize.y);
        public override float Duration => Cycle;

        public override void Build(RectTransform root, PromoKit kit)
        {
            sourceSize = new Vector2(kit.Portraits.width, kit.Portraits.height);
            for (int i = 0; i < PortraitCells; i++)
            {
                Poster poster = Posters[i];
                RectTransform frame = Node($"Cell {i + 1}", root);
                frame.anchorMin = new Vector2(i / (float)PortraitCells, 0f);
                frame.anchorMax = new Vector2((i + 1) / (float)PortraitCells, 1f);
                frame.offsetMin = frame.offsetMax = Vector2.zero;
                frame.gameObject.AddComponent<RectMask2D>();
                Fill(Box("Paper", frame, PromoKit.Paper), Vector2.zero, Vector2.one);

                var cell = new Cell();
                cell.Photo = Node("Photo", frame).gameObject.AddComponent<RawImage>();
                cell.Photo.texture = kit.Portraits;
                cell.Photo.raycastTarget = false;
                Fill(cell.Photo, Vector2.zero, Vector2.one);

                // Soft white bands so the type reads on any photo.
                Image top = Box("Top Band", frame, new Color(1f, 1f, 1f, 0.6f), kit.FadeUp);
                top.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
                Fill(top, new Vector2(0f, 0.8f), Vector2.one);
                Fill(Box("Bottom Band", frame, new Color(1f, 1f, 1f, 0.93f), kit.FadeUp), Vector2.zero, new Vector2(1f, 0.46f));

                At(Box("Logo Ring", frame, PromoKit.Ink, kit.ThinRing), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -44f), new Vector2(40f, 40f));
                At(Box("Logo Dot", frame, PromoKit.Accent, kit.Dot), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(84f, -64f), new Vector2(9f, 9f));
                At(Label(frame, kit, "NEXUS", 30f, PromoKit.Ink, TextAlignmentOptions.MidlineLeft, 26f),
                    new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(98f, -64f), new Vector2(400f, 40f));
                At(Label(frame, kit, "● LIVE", 18f, PromoKit.Accent, TextAlignmentOptions.MidlineRight, 20f),
                    new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-40f, -64f), new Vector2(200f, 30f));

                cell.Copy = Group("Copy", frame);
                cell.CopyRoot = (RectTransform)cell.Copy.transform;
                At(Label(cell.Copy.transform, kit, poster.Tag, 22f, PromoKit.Accent, TextAlignmentOptions.MidlineLeft, 40f),
                    new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(48f, 262f), new Vector2(640f, 32f));
                TMP_Text headline = At(Label(cell.Copy.transform, kit, poster.Headline, 50f, PromoKit.Ink, TextAlignmentOptions.MidlineLeft),
                    new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(46f, 198f), new Vector2(640f, 70f));
                headline.enableAutoSizing = true;
                headline.fontSizeMin = 30f;
                headline.fontSizeMax = 50f;
                cell.Rule = (RectTransform)At(Box("Rule", cell.Copy.transform, PromoKit.Accent), new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                    new Vector2(48f, 146f), new Vector2(0f, 3f)).transform;
                At(Label(cell.Copy.transform, kit, poster.Line, 23f, PromoKit.InkSoft, TextAlignmentOptions.MidlineLeft, 4f),
                    new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(48f, 104f), new Vector2(640f, 36f));

                cell.White = Box("White", frame, PromoKit.Paper);
                Fill(cell.White, Vector2.zero, Vector2.one);
                cells.Add(cell);
            }
        }

        public override void Pose(float t)
        {
            float cellAspect = PortraitCellSize.x / (float)PortraitCellSize.y;
            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];
                Poster poster = Posters[i];
                float u = Mathf.Repeat(t + i * Stagger, Cycle);

                // Slow push-out with a slight rise; resets under the white flash at the cycle seam.
                float move = u < 28.6f ? Ease(Mathf.Clamp01(u / 27.5f)) : 0f;
                float zoom = Mathf.Lerp(1.15f, 1f, move);
                float heightPx = sourceSize.y / zoom;
                float widthPx = Mathf.Min(heightPx * cellAspect, poster.Right - poster.Left);
                float centerX = Mathf.Lerp(poster.Left, poster.Right, poster.FocusX);
                centerX = Mathf.Clamp(centerX, poster.Left + widthPx * 0.5f, poster.Right - widthPx * 0.5f);
                float slack = sourceSize.y - heightPx;
                float bottomPx = slack * Mathf.Lerp(0.35f, 0.55f, move);
                cell.Photo.uvRect = new Rect((centerX - widthPx * 0.5f) / sourceSize.x, bottomPx / sourceSize.y,
                    widthPx / sourceSize.x, heightPx / sourceSize.y);

                float copy = Window(u, 1.2f, 2.9f, 25.6f, 27.1f);
                cell.Copy.alpha = copy;
                cell.CopyRoot.anchoredPosition = new Vector2(0f, Mathf.Lerp(-18f, 0f, Ramp(1.2f, 3.2f, u)));
                cell.Rule.sizeDelta = new Vector2(Mathf.Lerp(0f, 220f, Ramp(1.8f, 4f, u)), 3f);

                float white = u < 1.4f ? 1f - Ramp(0f, 1.4f, u) : Ramp(27.4f, 28.6f, u);
                SetAlpha(cell.White, white);
            }
        }
    }
}
