using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Rasterizes the baked NavMesh of one floor into a blueprint texture with world<->UV mapping.
public sealed class FacilityBlueprint
{
    private static readonly Color Background = new(0.004f, 0.02f, 0.03f, 0.96f);
    private static readonly Color Floor = new(0.02f, 0.13f, 0.16f, 1f);
    private static readonly Color Grid = new(0.05f, 0.22f, 0.26f, 1f);
    private static readonly Color Wall = new(0.3f, 0.95f, 1f, 1f);

    private static FacilityBlueprint cached;
    private static int cachedSceneHandle;
    private static Vector2 cachedHeightRange;

    public Texture2D Texture { get; }
    public Rect WorldRect { get; }

    private FacilityBlueprint(Texture2D texture, Rect worldRect)
    {
        Texture = texture;
        WorldRect = worldRect;
    }

    public static FacilityBlueprint GetOrBuild(Vector2 floorHeightRange, int resolution, float gridSpacing)
    {
        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (cached != null && cached.Texture != null &&
            cachedSceneHandle == sceneHandle && cachedHeightRange == floorHeightRange)
            return cached;

        cached = Build(floorHeightRange, resolution, gridSpacing);
        cachedSceneHandle = sceneHandle;
        cachedHeightRange = floorHeightRange;
        return cached;
    }

    public Vector2 WorldToUv(Vector3 world) => new(
        Mathf.InverseLerp(WorldRect.xMin, WorldRect.xMax, world.x),
        Mathf.InverseLerp(WorldRect.yMin, WorldRect.yMax, world.z));

    public Vector3 UvToWorld(Vector2 uv, float height) => new(
        Mathf.Lerp(WorldRect.xMin, WorldRect.xMax, uv.x),
        height,
        Mathf.Lerp(WorldRect.yMin, WorldRect.yMax, uv.y));

    public float WorldToUvDistance(float meters) => meters / Mathf.Max(WorldRect.width, 0.01f);

    private static FacilityBlueprint Build(Vector2 heightRange, int resolution, float gridSpacing)
    {
        NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.indices;

        bool any = false;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            if (!TriangleOnFloor(vertices, indices, i, heightRange)) continue;
            for (int k = 0; k < 3; k++)
            {
                Vector3 v = vertices[indices[i + k]];
                min = Vector2.Min(min, new Vector2(v.x, v.z));
                max = Vector2.Max(max, new Vector2(v.x, v.z));
                any = true;
            }
        }

        if (!any)
        {
            min = new Vector2(-50f, -50f);
            max = new Vector2(50f, 50f);
            Debug.LogWarning("[FacilityBlueprint] No NavMesh triangles in the floor height range; radar shows an empty grid.");
        }

        // Square world rect so meters map uniformly on both axes.
        float padding = 4f;
        float size = Mathf.Max(max.x - min.x, max.y - min.y) + padding * 2f;
        Vector2 center = (min + max) * 0.5f;
        Rect worldRect = new(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

        bool[] walkable = new bool[resolution * resolution];
        for (int i = 0; any && i + 2 < indices.Length; i += 3)
        {
            if (!TriangleOnFloor(vertices, indices, i, heightRange)) continue;
            RasterizeTriangle(
                ToPixel(vertices[indices[i]], worldRect, resolution),
                ToPixel(vertices[indices[i + 1]], worldRect, resolution),
                ToPixel(vertices[indices[i + 2]], worldRect, resolution),
                walkable, resolution);
        }

        Color[] pixels = new Color[walkable.Length];
        float metersPerPixel = size / resolution;
        int gridPixels = Mathf.Max(2, Mathf.RoundToInt(gridSpacing / metersPerPixel));
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            int index = y * resolution + x;
            if (!walkable[index])
            {
                pixels[index] = Background;
                continue;
            }

            bool edge = IsEdge(walkable, resolution, x, y);
            bool gridLine = x % gridPixels == 0 || y % gridPixels == 0;
            pixels[index] = edge ? Wall : gridLine ? Grid : Floor;
        }

        Texture2D texture = new(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = "Facility Blueprint",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return new FacilityBlueprint(texture, worldRect);
    }

    private static bool TriangleOnFloor(Vector3[] vertices, int[] indices, int start, Vector2 heightRange)
    {
        for (int k = 0; k < 3; k++)
        {
            float y = vertices[indices[start + k]].y;
            if (y < heightRange.x || y > heightRange.y) return false;
        }
        return true;
    }

    private static Vector2 ToPixel(Vector3 world, Rect rect, int resolution) => new(
        (world.x - rect.xMin) / rect.width * resolution,
        (world.z - rect.yMin) / rect.height * resolution);

    private static void RasterizeTriangle(Vector2 a, Vector2 b, Vector2 c, bool[] target, int resolution)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, resolution - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, resolution - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, resolution - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, resolution - 1);
        float area = Cross(b - a, c - a);
        if (Mathf.Abs(area) < 0.0001f) return;
        float winding = Mathf.Sign(area);

        // Signed pixel distance to each edge; slight dilation closes gaps between NavMesh tiles.
        const float tolerance = -0.6f;
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector2 p = new(x + 0.5f, y + 0.5f);
            float d0 = Cross(c - b, p - b) * winding / Distance(b, c);
            float d1 = Cross(a - c, p - c) * winding / Distance(c, a);
            float d2 = Cross(b - a, p - a) * winding / Distance(a, b);
            if (d0 >= tolerance && d1 >= tolerance && d2 >= tolerance)
                target[y * resolution + x] = true;
        }
    }

    private static float Distance(Vector2 a, Vector2 b) => Mathf.Max(0.0001f, Vector2.Distance(a, b));
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static bool IsEdge(bool[] walkable, int resolution, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= resolution || ny >= resolution || !walkable[ny * resolution + nx])
                return true;
        }
        return false;
    }
}
