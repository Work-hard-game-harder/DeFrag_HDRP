using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Adds a glowing screen surface to a device model. The material is cloned from
/// Resources/Devices/DeviceScreen (HDRP Lit, emissive map) so the shader variant ships in builds.
/// </summary>
public static class DeviceScreenQuad
{
    private const string TemplatePath = "Devices/DeviceScreen";
    private static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
    private static readonly int EmissiveColorMap = Shader.PropertyToID("_EmissiveColorMap");
    private static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
    private static Material template;

    /// <param name="localCenter">Screen centre in the parent's local space.</param>
    /// <param name="localNormal">Direction the screen faces, in the parent's local space.</param>
    /// <param name="localUp">Screen "up" direction, in the parent's local space.</param>
    /// <param name="localSize">Width/height in the parent's local units.</param>
    public static MeshRenderer Create(Transform parent, string name, Vector3 localCenter, Vector3 localNormal,
        Vector3 localUp, Vector2 localSize, Texture texture)
    {
        // Built without a collider: a MeshCollider under a dynamic Rigidbody (items on the floor) is invalid.
        var quad = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        quad.GetComponent<MeshFilter>().sharedMesh = QuadMesh();
        quad.layer = parent.gameObject.layer;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localCenter;
        // The built-in quad is visible from its -Z side, so -Z must point along the screen normal.
        quad.transform.localRotation = Quaternion.LookRotation(-localNormal, localUp);
        quad.transform.localScale = new Vector3(localSize.x, localSize.y, 1f);

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = CreateMaterial(texture, name);
        return renderer;
    }

    private static Mesh quadMesh;

    /// <summary>Unit quad in the XY plane facing -Z (same layout as the built-in primitive).</summary>
    private static Mesh QuadMesh()
    {
        if (quadMesh != null) return quadMesh;
        quadMesh = new Mesh { name = "Device Screen Quad" };
        quadMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        quadMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        quadMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        quadMesh.triangles = new[] { 0, 3, 1, 3, 0, 2 };
        quadMesh.RecalculateBounds();
        quadMesh.RecalculateTangents();
        return quadMesh;
    }

    public static Material CreateMaterial(Texture texture, string name)
    {
        template ??= Resources.Load<Material>(TemplatePath);
        Material material = template != null
            ? new Material(template)
            : new Material(Shader.Find("HDRP/Unlit"));
        material.name = $"{name} (Runtime)";
        SetTexture(material, texture);
        return material;
    }

    public static void SetTexture(Material material, Texture texture)
    {
        if (material == null) return;
        if (material.HasProperty(BaseColorMap)) material.SetTexture(BaseColorMap, texture);
        if (material.HasProperty(EmissiveColorMap)) material.SetTexture(EmissiveColorMap, texture);
        if (material.HasProperty("_UnlitColorMap")) material.SetTexture("_UnlitColorMap", texture);
    }

    /// <summary>HDR emission strength; 1 is roughly "screen brightness", higher values bloom.</summary>
    public static void SetGlow(Material material, Color tint, float intensity)
    {
        if (material != null && material.HasProperty(EmissiveColor))
            material.SetColor(EmissiveColor, tint * intensity);
    }
}
