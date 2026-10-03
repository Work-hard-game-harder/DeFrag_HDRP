#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Paints a white/silver brushed-metal texture set for the locker straight into its UV atlas.
/// Every texel is mapped back to its position on the scaled model, so the brushing, scratches
/// and grime keep real-world proportions even though the UV islands have uneven density.
/// Menu: DEFRAG > Locker > Bake Metal Textures. Output lives in Assets/Art/Locker/.
/// </summary>
public static class LockerMetalTextureBaker
{
    private const string SourceModelPath = "Assets/Prefabs/Locker.fbx";
    private const string BodyMeshName = "Locker2";
    private const string DoorMeshName = "Door_Locker2";
    private const string PrefabPath = "Assets/Prefabs/Locker.prefab";
    private const string OutputFolder = "Assets/Art/Locker";
    private const string MaterialPath = OutputFolder + "/Locker_Metal.mat";
    private const int Resolution = 4096;
    private const int DilatePixels = 8;

    // Look (linear 0..1). Satin white steel with a brushed grain; the padlock is darker gunmetal.
    private static readonly Color PanelColor = new(0.86f, 0.875f, 0.89f);
    private static readonly Color LockColor = new(0.36f, 0.37f, 0.39f);
    private static readonly Color GrimeColor = new(0.42f, 0.40f, 0.36f);
    private const float PanelMetallic = 0.6f, LockMetallic = 1f;
    private const float PanelSmoothness = 0.62f, LockSmoothness = 0.72f;
    private const float GrimeHeight = 0.5f;       // metres above the locker floor
    private const float ScratchCell = 0.16f;      // metres
    private const float ScratchWidth = 0.0025f;   // metres
    private const float EdgeWearWidth = 0.025f;   // metres
    private const float NormalStrength = 6f;

    private struct Texel
    {
        public bool covered;
        public Vector3 position;   // scaled model space, metres; Z is up
        public Vector3 normal;     // scaled model space
        public bool isLock;
        public byte meshIndex;
    }

    [MenuItem("DEFRAG/Locker/Bake Metal Textures")]
    public static void Bake()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Vector3 scale = prefab.transform.lossyScale;
        Mesh body = LoadMesh(BodyMeshName), door = LoadMesh(DoorMeshName);

        var texels = new Texel[Resolution * Resolution];
        var bounds = new Bounds[2];
        Rasterize(body, scale, 0, texels, bounds);
        Rasterize(door, scale, 1, texels, bounds);

        var albedo = new Color[texels.Length];
        var mask = new Color[texels.Length];
        var height = new float[texels.Length];
        float floorZ = bounds[0].min.z;
        for (int i = 0; i < texels.Length; i++)
        {
            if (!texels[i].covered) continue;
            Shade(texels[i], bounds[texels[i].meshIndex], floorZ, out albedo[i], out mask[i], out height[i]);
        }

        Color[] normal = HeightToNormal(height, texels);
        Dilate(albedo, texels); Dilate(mask, texels); Dilate(normal, texels);

        Directory.CreateDirectory(OutputFolder);
        string albedoPath = Save(albedo, "Locker_Metal_BaseColor.png", true);
        string maskPath = Save(mask, "Locker_Metal_Mask.png", false);
        string normalPath = Save(normal, "Locker_Metal_Normal.png", false);
        AssetDatabase.Refresh();
        Configure(albedoPath, TextureImporterType.Default, true);
        Configure(maskPath, TextureImporterType.Default, false);
        Configure(normalPath, TextureImporterType.NormalMap, false);

        Material material = CreateOrUpdateMaterial(albedoPath, maskPath, normalPath);
        AssignToPrefab(material);
        Debug.Log($"[LockerMetalTextureBaker] Baked {Resolution}px locker textures and assigned {MaterialPath}.");
    }

    private static Mesh LoadMesh(string name)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SourceModelPath))
            if (asset is Mesh mesh && mesh.name == name)
                return mesh;
        throw new FileNotFoundException($"{name} not found in {SourceModelPath}");
    }

    private static void Rasterize(Mesh mesh, Vector3 scale, byte meshIndex, Texel[] texels, Bounds[] bounds)
    {
        Vector3[] v = mesh.vertices;
        Vector3[] n = mesh.normals;
        Vector2[] uv = mesh.uv;
        int[] tris = mesh.triangles;
        // Paint the padlock with the same proportions the vent builder gives it.
        LockerMeshParts.UnsquashLock(v, n, tris, scale);
        bool[] isLock = LockerMeshParts.FindLockVertices(v, tris);

        var p = new Vector3[v.Length];
        var normals = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            p[i] = Vector3.Scale(v[i], scale);
            normals[i] = new Vector3(n[i].x / scale.x, n[i].y / scale.y, n[i].z / scale.z).normalized;
        }
        bounds[meshIndex] = GeometryUtility.CalculateBounds(p, Matrix4x4.identity);

        for (int t = 0; t < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            Vector2 a = uv[i0] * Resolution, b = uv[i1] * Resolution, c = uv[i2] * Resolution;
            float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (Mathf.Abs(area) < 1e-6f) continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)) - 1);
            int x1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)) - 1);
            int y1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)) + 1);
            Vector3 faceNormal = (normals[i0] + normals[i1] + normals[i2]).normalized;
            bool lockTri = isLock[i0] && isLock[i1] && isLock[i2];
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 q = new(x + 0.5f, y + 0.5f);
                float w0 = ((b.x - q.x) * (c.y - q.y) - (c.x - q.x) * (b.y - q.y)) / area;
                float w1 = ((c.x - q.x) * (a.y - q.y) - (a.x - q.x) * (c.y - q.y)) / area;
                float w2 = 1f - w0 - w1;
                const float eps = -0.02f;
                if (w0 < eps || w1 < eps || w2 < eps) continue;
                int index = y * Resolution + x;
                // Small lock islands win over the slab if UV islands touch.
                if (texels[index].covered && texels[index].isLock && !lockTri) continue;
                texels[index] = new Texel
                {
                    covered = true,
                    position = p[i0] * w0 + p[i1] * w1 + p[i2] * w2,
                    normal = faceNormal,
                    isLock = lockTri,
                    meshIndex = meshIndex
                };
            }
        }
    }

    private static void Shade(Texel texel, Bounds meshBounds, float floorZ, out Color albedo, out Color mask, out float height)
    {
        Vector3 p = texel.position, n = texel.normal;
        // Face-plane coordinates: 'along' follows the brushing, 'across' is perpendicular to it.
        float along, across;
        if (Mathf.Abs(n.z) > 0.7f)
        {
            along = p.x; across = p.y;
        }
        else
        {
            Vector2 tangent = new Vector2(-n.y, n.x).normalized;
            along = p.z; across = p.x * tangent.x + p.y * tangent.y;
        }

        float grain = 0.55f * Noise(across * 420f, along * 3f) + 0.3f * Noise(across * 1300f, along * 9f) +
                      0.15f * Noise(across * 120f, along * 1.2f);
        float cloud = Fbm(across * 1.6f, along * 1.6f, 3);
        float dents = Fbm(across * 7f + 31f, along * 7f + 17f, 2);
        float scratch = Scratches(across, along);

        float aboveFloor = p.z - floorZ;
        float grime = (1f - Mathf.SmoothStep(0f, GrimeHeight, aboveFloor)) * Mathf.Lerp(0.35f, 1f, Fbm(across * 5f, along * 5f, 3));
        grime += 0.25f * Mathf.Clamp01(Fbm(across * 2.5f + 7f, along * 0.6f, 3) - 0.55f) * 3f; // faint vertical run marks
        grime = Mathf.Clamp01(grime);

        float edge = EdgeFactor(p, meshBounds);

        if (texel.isLock)
        {
            float wear = Mathf.Clamp01(edge + scratch * 0.6f);
            albedo = Color.Lerp(LockColor * (0.92f + 0.12f * grain), new Color(0.62f, 0.63f, 0.64f), wear * 0.5f);
            mask = new Color(LockMetallic, 1f, 1f, Mathf.Clamp01(LockSmoothness - 0.15f * cloud - 0.2f * wear));
            height = grain * 0.15f - scratch * 0.5f;
            albedo.a = 1f;
            return;
        }

        Color baseColor = PanelColor * (0.95f + 0.05f * grain) * (0.97f + 0.05f * cloud);
        // Worn edges and scratches expose brighter, rougher bare steel.
        Color bare = new(0.93f, 0.935f, 0.94f);
        baseColor = Color.Lerp(baseColor, bare, Mathf.Clamp01(scratch * 0.8f + edge * 0.35f));
        baseColor = Color.Lerp(baseColor, GrimeColor, grime * 0.45f);
        albedo = baseColor;
        albedo.a = 1f;

        float metallic = Mathf.Clamp01(PanelMetallic + 0.25f * scratch + 0.15f * edge - 0.3f * grime);
        float smoothness = Mathf.Clamp01(PanelSmoothness + 0.08f * (grain - 0.5f) - 0.08f * cloud - 0.3f * grime - 0.2f * scratch);
        float occlusion = Mathf.Clamp01(1f - 0.35f * grime);
        mask = new Color(metallic, occlusion, 1f, smoothness);
        height = (grain - 0.5f) * 0.12f + (dents - 0.5f) * 0.5f - scratch * 0.6f;
    }

    // Short random scratch segments, a few per cell, mostly near-horizontal.
    private static float Scratches(float u, float v)
    {
        float cu = Mathf.Floor(u / ScratchCell), cv = Mathf.Floor(v / ScratchCell);
        float best = 0f;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            float gx = cu + dx, gy = cv + dy;
            int count = Mathf.FloorToInt(Hash(gx, gy, 0f) * 3f);
            for (int s = 0; s < count; s++)
            {
                float k = s * 7.13f;
                Vector2 start = new((gx + Hash(gx, gy, 1f + k)) * ScratchCell, (gy + Hash(gx, gy, 2f + k)) * ScratchCell);
                float angle = (Hash(gx, gy, 3f + k) - 0.5f) * 1.2f + (Hash(gx, gy, 4f + k) > 0.8f ? 1.4f : 0f);
                float length = Mathf.Lerp(0.03f, 0.14f, Hash(gx, gy, 5f + k));
                Vector2 end = start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
                float d = DistanceToSegment(new Vector2(u, v), start, end);
                float strength = Mathf.Lerp(0.35f, 1f, Hash(gx, gy, 6f + k));
                best = Mathf.Max(best, (1f - Mathf.SmoothStep(0f, ScratchWidth, d)) * strength);
            }
        }
        return best;
    }

    private static float EdgeFactor(Vector3 p, Bounds b)
    {
        float dx = Mathf.Min(p.x - b.min.x, b.max.x - p.x);
        float dy = Mathf.Min(p.y - b.min.y, b.max.y - p.y);
        float dz = Mathf.Min(p.z - b.min.z, b.max.z - p.z);
        // Near an edge means close to two bounding faces at once; the face we sit on is ~0.
        float a = Mathf.Min(dx, dy, dz);
        float second = dx + dy + dz - a - Mathf.Max(dx, dy, dz);
        float wear = 1f - Mathf.SmoothStep(0f, EdgeWearWidth, second);
        return wear * Mathf.Lerp(0.3f, 1f, Noise(p.x * 40f + p.z * 25f, p.y * 40f - p.z * 25f));
    }

    private static Color[] HeightToNormal(float[] height, Texel[] texels)
    {
        var normal = new Color[height.Length];
        int r = Resolution;
        for (int y = 0; y < r; y++)
        for (int x = 0; x < r; x++)
        {
            int i = y * r + x;
            if (!texels[i].covered) { normal[i] = new Color(0.5f, 0.5f, 1f, 1f); continue; }
            float H(int xx, int yy)
            {
                xx = Mathf.Clamp(xx, 0, r - 1); yy = Mathf.Clamp(yy, 0, r - 1);
                int j = yy * r + xx;
                return texels[j].covered ? height[j] : height[i];
            }
            float dx = (H(x + 1, y) - H(x - 1, y)) * 0.5f;
            float dy = (H(x, y + 1) - H(x, y - 1)) * 0.5f;
            Vector3 nn = new Vector3(-dx * NormalStrength, -dy * NormalStrength, 1f).normalized;
            normal[i] = new Color(nn.x * 0.5f + 0.5f, nn.y * 0.5f + 0.5f, nn.z * 0.5f + 0.5f, 1f);
        }
        return normal;
    }

    // Grow island borders outward so mip maps and bilinear filtering do not pull in black seams.
    private static void Dilate(Color[] pixels, Texel[] texels)
    {
        int r = Resolution;
        var filled = new bool[pixels.Length];
        for (int i = 0; i < filled.Length; i++) filled[i] = texels[i].covered;
        for (int pass = 0; pass < DilatePixels; pass++)
        {
            var next = (bool[])filled.Clone();
            for (int y = 0; y < r; y++)
            for (int x = 0; x < r; x++)
            {
                int i = y * r + x;
                if (filled[i]) continue;
                Color sum = Color.clear; int count = 0;
                if (x > 0 && filled[i - 1]) { sum += pixels[i - 1]; count++; }
                if (x < r - 1 && filled[i + 1]) { sum += pixels[i + 1]; count++; }
                if (y > 0 && filled[i - r]) { sum += pixels[i - r]; count++; }
                if (y < r - 1 && filled[i + r]) { sum += pixels[i + r]; count++; }
                if (count == 0) continue;
                pixels[i] = sum / count;
                next[i] = true;
            }
            filled = next;
        }
        Color fallback = pixels.Length > 0 ? AverageCovered(pixels, texels) : Color.gray;
        for (int i = 0; i < pixels.Length; i++)
            if (!filled[i]) pixels[i] = fallback;
    }

    private static Color AverageCovered(Color[] pixels, Texel[] texels)
    {
        Color sum = Color.clear; int count = 0;
        for (int i = 0; i < pixels.Length; i += 97)
            if (texels[i].covered) { sum += pixels[i]; count++; }
        return count > 0 ? sum / count : Color.gray;
    }

    private static string Save(Color[] pixels, string fileName, bool srgb)
    {
        var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, !srgb);
        if (srgb)
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = pixels[i].gamma;
        texture.SetPixels(pixels);
        texture.Apply();
        string path = OutputFolder + "/" + fileName;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        return path;
    }

    private static void Configure(string path, TextureImporterType type, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.maxTextureSize = Resolution;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static Material CreateOrUpdateMaterial(string albedoPath, string maskPath, string normalPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("HDRP/Lit")) { name = "Locker_Metal" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
        material.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
        material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        material.SetFloat("_NormalScale", 1f);
        material.SetFloat("_MetallicRemapMin", 0f);
        material.SetFloat("_MetallicRemapMax", 1f);
        material.SetFloat("_SmoothnessRemapMin", 0f);
        material.SetFloat("_SmoothnessRemapMax", 1f);
        material.SetFloat("_AORemapMin", 0f);
        material.SetFloat("_AORemapMax", 1f);
        HDMaterial.ValidateMaterial(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void AssignToPrefab(Material material)
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    // ── Noise ───────────────────────────────────────────────────────────────
    private static float Hash(float x, float y, float z)
    {
        Vector3 p = new(Frac(x * 0.1031f), Frac(y * 0.1030f), Frac(z * 0.0973f));
        float d = Vector3.Dot(p, new Vector3(p.y, p.z, p.x) + Vector3.one * 33.33f);
        p += Vector3.one * d;
        return Frac((p.x + p.y) * p.z);
    }

    private static float Noise(float x, float y)
    {
        float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy, 0f), b = Hash(ix + 1f, iy, 0f), c = Hash(ix, iy + 1f, 0f), d = Hash(ix + 1f, iy + 1f, 0f);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amplitude = 0.5f, total = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += Noise(x, y) * amplitude;
            total += amplitude;
            x = x * 2.03f + 5.1f; y = y * 2.03f + 1.7f;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static float Frac(float v) => v - Mathf.Floor(v);

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }
}
#endif
