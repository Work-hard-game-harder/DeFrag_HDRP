using UnityEngine;

// Procedural shapes for runtime-built HUDs, so minigame UI needs no imported sprite assets.
public static class RuntimeUiSprites
{
    private const int Size = 128;

    private static Sprite disc;
    private static Sprite ring;
    private static Sprite arrow;
    private static Sprite softGlow;

    public static Sprite Disc => disc ??= Create("Disc", (x, y) => Coverage(Radius(x, y), 0.96f));
    public static Sprite Ring => ring ??= Create("Ring", (x, y) =>
    {
        float r = Radius(x, y);
        return Coverage(r, 0.96f) * (1f - Coverage(r, 0.8f));
    });
    public static Sprite Arrow => arrow ??= Create("Arrow", (x, y) =>
    {
        // Upward-pointing chevron; UI rotation turns it toward the tracked heading.
        float u = x * 2f - 1f, v = y * 2f - 1f;
        bool inside = v > -0.75f && v < 0.95f && Mathf.Abs(u) < (0.95f - v) * 0.55f && !(v < -0.2f && Mathf.Abs(u) < (-0.2f - v) * 0.9f);
        return inside ? 1f : 0f;
    });
    public static Sprite SoftGlow => softGlow ??= Create("SoftGlow", (x, y) =>
    {
        float r = Radius(x, y);
        return Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r);
    });

    private static readonly System.Collections.Generic.Dictionary<int, Sprite> wedges = new();
    private static Sprite wheel;

    public static Sprite Wheel => wheel ??= Create("Wheel", (x, y) =>
    {
        float r = Radius(x, y);
        float rim = Coverage(r, 0.96f) * (1f - Coverage(r, 0.78f));
        float hub = Coverage(r, 0.2f);
        float angle = Mathf.Atan2(y * 2f - 1f, x * 2f - 1f) * Mathf.Rad2Deg;
        float spoke = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 60f) * 60f)) < 5f && r < 0.8f ? 1f : 0f;
        return Mathf.Max(rim, Mathf.Max(hub, spoke));
    });

    // Annular wedge centered on 12 o'clock, used as an ignition window on circular gauges.
    public static Sprite Wedge(float degrees)
    {
        int key = Mathf.RoundToInt(degrees);
        if (wedges.TryGetValue(key, out Sprite cachedWedge))
            return cachedWedge;
        float half = key * 0.5f;
        Sprite created = Create($"Wedge{key}", (x, y) =>
        {
            float r = Radius(x, y);
            float angleFromTop = Mathf.Atan2(x * 2f - 1f, y * 2f - 1f) * Mathf.Rad2Deg;
            float inside = Mathf.Clamp01((half - Mathf.Abs(angleFromTop)) * 0.5f);
            return inside * Coverage(r, 0.96f) * (1f - Coverage(r, 0.55f));
        });
        wedges[key] = created;
        return created;
    }

    private static float Radius(float x, float y) => new Vector2(x * 2f - 1f, y * 2f - 1f).magnitude;

    private static float Coverage(float radius, float edge) => Mathf.Clamp01((edge - radius) * Size * 0.5f);

    private static Sprite Create(string name, System.Func<float, float, float> alpha)
    {
        Texture2D texture = new(Size, Size, TextureFormat.RGBA32, false)
        {
            name = $"RuntimeUi_{name}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color[] pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
            pixels[y * Size + x] = new Color(1f, 1f, 1f, alpha((x + 0.5f) / Size, (y + 0.5f) / Size));
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
    }
}
