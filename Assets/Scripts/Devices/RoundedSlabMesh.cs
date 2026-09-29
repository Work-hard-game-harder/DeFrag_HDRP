using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a rounded-rectangle slab (a squircle-ish panel) whose front face points along +Z:
/// straight sides from z = 0, a small bevel, and a flat front cap at z = depth. The cap is UV-mapped
/// planar over the whole rectangle so a screen texture fits it exactly, rounded corners included.
/// "Viewer right" is local -X, because the face is seen from +Z looking back toward -Z.
/// </summary>
public static class RoundedSlabMesh
{
    public static UnityEngine.Mesh Build(string name, Vector2 size, float radius, float depth, float bevel = 0.003f, int cornerSegments = 10)
    {
        radius = Mathf.Clamp(radius, 0.0005f, Mathf.Min(size.x, size.y) * 0.5f);
        bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(radius * 0.9f, depth * 0.9f));
        List<Vector2> outer = Outline(size, radius, cornerSegments);
        List<Vector2> inner = Outline(size - Vector2.one * bevel * 2f, Mathf.Max(0.0005f, radius - bevel), cornerSegments);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        Vector2 UvOf(Vector2 face, Vector2 extent) => new(face.x / extent.x + 0.5f, face.y / extent.y + 0.5f);

        // Sides: ring at the wall and ring where the bevel starts, both on the outer outline.
        AddBand(vertices, uvs, triangles, outer, 0f, outer, depth - bevel, size);
        // Bevel: outer outline up to the inset cap outline.
        if (bevel > 0f) AddBand(vertices, uvs, triangles, outer, depth - bevel, inner, depth, size);

        // Front cap: triangle fan from the centre, planar UVs over the full panel size.
        int centre = vertices.Count;
        vertices.Add(ToLocal(Vector2.zero, depth));
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < inner.Count; i++)
        {
            vertices.Add(ToLocal(inner[i], depth));
            uvs.Add(UvOf(inner[i], size));
        }
        for (int i = 0; i < inner.Count; i++)
        {
            int a = centre + 1 + i, b = centre + 1 + (i + 1) % inner.Count;
            AddTriangle(vertices, triangles, centre, a, b, Vector3.forward);
        }

        var mesh = new UnityEngine.Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Counter-clockwise outline (as the viewer sees it) of a rounded rectangle centred on the origin.</summary>
    private static List<Vector2> Outline(Vector2 size, float radius, int segments)
    {
        var points = new List<Vector2>();
        Vector2 half = size * 0.5f - Vector2.one * radius;
        Vector2[] centres = { new(half.x, half.y), new(-half.x, half.y), new(-half.x, -half.y), new(half.x, -half.y) };
        for (int corner = 0; corner < 4; corner++)
        {
            float start = corner * 90f;
            for (int s = 0; s <= segments; s++)
            {
                float angle = (start + 90f * s / segments) * Mathf.Deg2Rad;
                points.Add(centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }
        return points;
    }

    private static void AddBand(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        List<Vector2> lower, float lowerZ, List<Vector2> upper, float upperZ, Vector2 size)
    {
        int start = vertices.Count;
        int count = lower.Count;
        for (int i = 0; i < count; i++)
        {
            vertices.Add(ToLocal(lower[i], lowerZ));
            uvs.Add(new Vector2((float)i / count, 0f));
            vertices.Add(ToLocal(upper[i], upperZ));
            uvs.Add(new Vector2((float)i / count, 1f));
        }
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            int a = start + i * 2, b = start + j * 2, c = start + j * 2 + 1, d = start + i * 2 + 1;
            Vector2 mid = (lower[i] + lower[j]) * 0.5f;
            Vector3 outward = ToLocal(mid, 0f).normalized + Vector3.forward * 0.2f;
            AddTriangle(vertices, triangles, a, b, c, outward);
            AddTriangle(vertices, triangles, a, c, d, outward);
        }
    }

    // Orders the winding so the visible side faces the intended direction.
    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, int a, int b, int c, Vector3 outward)
    {
        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (Vector3.Dot(normal, outward) >= 0f) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        else { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
    }

    private static Vector3 ToLocal(Vector2 face, float z) => new(-face.x, face.y, z);
}
