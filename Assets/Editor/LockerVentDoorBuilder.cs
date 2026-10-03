#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cuts a louvred vent window into the imported locker door so a hidden player can see out.
/// The original FBX stays untouched: the front/back slab faces are rebuilt around the handle
/// hole and the new vent hole, every other original triangle is kept, and angled slats plus
/// a frame are added. Re-run from the menu after changing the constants below.
/// </summary>
public static class LockerVentDoorBuilder
{
    private const string SourceModelPath = "Assets/Prefabs/Locker.fbx";
    private const string SourceMeshName = "Door_Locker2";
    private const string PrefabPath = "Assets/Prefabs/Locker.prefab";
    private const string OutputPath = "Assets/Art/Locker/Door_Locker2_Vented.asset";

    // World-space vent size (metres) centred on the locker's View Anchor.
    private const float VentWidth = 0.62f;
    private const float VentHeight = 0.42f;
    private const int SlatCount = 6;
    private const float SlatRise = 0.034f;      // vertical extent of one tilted slat
    private const float SlatThickness = 0.004f;
    private const float SlatOutset = 0.022f;    // how far the slats' lower lip protrudes outside
    private const float SlatInset = 0.006f;

    [MenuItem("DEFRAG/Locker/Build Vented Door Mesh")]
    public static void Build()
    {
        Mesh source = LoadSourceMesh();
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            LockerHiding locker = prefab.GetComponent<LockerHiding>();
            var serialized = new SerializedObject(locker);
            var door = (Transform)serialized.FindProperty("doorPivot").objectReferenceValue;
            var view = (Transform)serialized.FindProperty("viewAnchor").objectReferenceValue;
            if (door == null || view == null)
                throw new System.InvalidOperationException("Locker prefab needs Door Pivot and View Anchor.");

            Mesh vented = BuildVentedMesh(source, door, view, prefab.transform);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputPath);
            if (existing != null)
            {
                // Rewrite in place so the asset GUID (and prefab references) stay stable.
                existing.Clear();
                existing.name = vented.name;
                existing.indexFormat = vented.indexFormat;
                existing.SetVertices(vented.vertices);
                existing.SetNormals(vented.normals);
                existing.SetUVs(0, vented.uv);
                existing.SetTriangles(vented.triangles, 0);
                existing.SetTangents(vented.tangents);
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(vented);
                vented = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(vented, OutputPath);
            }

            door.GetComponent<MeshFilter>().sharedMesh = vented;
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LockerVentDoorBuilder] Built {OutputPath} ({vented.vertexCount} verts) and assigned it to {PrefabPath}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static Mesh LoadSourceMesh()
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SourceModelPath))
            if (asset is Mesh mesh && mesh.name == SourceMeshName)
                return mesh;
        throw new FileNotFoundException($"{SourceMeshName} not found in {SourceModelPath}");
    }

    private struct Rect2 { public float x0, x1, z0, z1; public bool Contains(float x, float z) => x > x0 && x < x1 && z > z0 && z < z1; }

    private static Mesh BuildVentedMesh(Mesh source, Transform door, Transform view, Transform root)
    {
        Vector3[] v = source.vertices;
        Vector3[] n = source.normals;
        Vector2[] uv = source.uv;
        int[] tris = source.triangles;
        // The prefab stretches the model non-uniformly; give the padlock its true proportions.
        LockerMeshParts.UnsquashLock(v, n, tris, door.lossyScale);
        Bounds b = GeometryUtility.CalculateBounds(v, Matrix4x4.identity);
        Rect2 slab = new() { x0 = b.min.x, x1 = b.max.x, z0 = b.min.z, z1 = b.max.z };

        // The door is a bevelled panel, not a flat slab. The handle is the part that
        // protrudes furthest; keep the vent clear of it.
        float handleThreshold = b.min.y + (b.max.y - b.min.y) * 0.55f;
        Rect2 handle = new() { x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue };
        foreach (Vector3 p in v)
        {
            if (p.y < handleThreshold) continue;
            handle.x0 = Mathf.Min(handle.x0, p.x); handle.x1 = Mathf.Max(handle.x1, p.x);
            handle.z0 = Mathf.Min(handle.z0, p.z); handle.z1 = Mathf.Max(handle.z1, p.z);
        }

        // Vent rect in door-local space, centred on the eye position.
        Vector3 lossy = door.lossyScale;
        Vector3 eye = door.InverseTransformPoint(view.position);
        float halfW = VentWidth * 0.5f / Mathf.Abs(lossy.x);
        float halfH = VentHeight * 0.5f / Mathf.Abs(lossy.z);
        float marginX = (slab.x1 - slab.x0) * 0.08f, marginZ = (slab.z1 - slab.z0) * 0.04f;
        Rect2 vent = new()
        {
            x0 = Mathf.Clamp(eye.x - halfW, slab.x0 + marginX, slab.x1 - marginX - halfW * 2f),
            z0 = Mathf.Clamp(eye.z - halfH, slab.z0 + marginZ, slab.z1 - marginZ - halfH * 2f)
        };
        vent.x1 = vent.x0 + halfW * 2f;
        vent.z1 = vent.z0 + halfH * 2f;
        if (handle.x0 < handle.x1 && Overlaps(vent, handle))
        {
            vent.z0 = handle.z1 + marginZ;
            vent.z1 = vent.z0 + halfH * 2f;
        }

        // Which local ±Y side faces out of the locker?
        Renderer body = root.GetComponent<Renderer>();
        Vector3 outwardWorld = door.GetComponent<Renderer>().bounds.center - (body != null ? body.bounds.center : root.position);
        outwardWorld.y = 0f;
        float outSign = Mathf.Sign(door.InverseTransformDirection(outwardWorld).y);
        if (outSign == 0f) outSign = 1f;

        System.Func<float, float, Vector2> uvAt = UvSampler(v, uv, _ => true);
        var builder = new MeshBuilder(uvAt, uvAt);

        // Keep the door, cutting away exactly the vent rectangle from every triangle.
        float front = float.MinValue, back = float.MaxValue;
        for (int i = 0; i < tris.Length; i += 3)
        {
            var tri = new List<Vert>
            {
                new(v[tris[i]], n[tris[i]], uv[tris[i]]),
                new(v[tris[i + 1]], n[tris[i + 1]], uv[tris[i + 1]]),
                new(v[tris[i + 2]], n[tris[i + 2]], uv[tris[i + 2]])
            };
            float minX = Mathf.Min(tri[0].p.x, tri[1].p.x, tri[2].p.x), maxX = Mathf.Max(tri[0].p.x, tri[1].p.x, tri[2].p.x);
            float minZ = Mathf.Min(tri[0].p.z, tri[1].p.z, tri[2].p.z), maxZ = Mathf.Max(tri[0].p.z, tri[1].p.z, tri[2].p.z);
            if (maxX <= vent.x0 || minX >= vent.x1 || maxZ <= vent.z0 || minZ >= vent.z1)
            {
                builder.AddPolygon(tri);
                continue;
            }

            foreach (Vert p in tri) { front = Mathf.Max(front, p.p.y); back = Mathf.Min(back, p.p.y); }
            var rest = Clip(tri, 0, vent.x0, true, out List<Vert> left);
            builder.AddPolygon(left);
            rest = Clip(rest, 0, vent.x1, false, out List<Vert> right);
            builder.AddPolygon(right);
            rest = Clip(rest, 2, vent.z0, true, out List<Vert> below);
            builder.AddPolygon(below);
            Clip(rest, 2, vent.z1, false, out List<Vert> above);
            builder.AddPolygon(above);
        }
        if (front < back) { front = b.max.y; back = b.min.y; }
        float outerY = outSign > 0 ? front : back;
        float innerY = outSign > 0 ? back : front;

        // Frame: the four inner walls of the cut.
        builder.AddQuad(new Vector3(vent.x0, back, vent.z0), new Vector3(vent.x1, back, vent.z0),
            new Vector3(vent.x1, front, vent.z0), new Vector3(vent.x0, front, vent.z0), Vector3.forward, true);
        builder.AddQuad(new Vector3(vent.x0, back, vent.z1), new Vector3(vent.x1, back, vent.z1),
            new Vector3(vent.x1, front, vent.z1), new Vector3(vent.x0, front, vent.z1), Vector3.back, true);
        builder.AddQuad(new Vector3(vent.x0, back, vent.z0), new Vector3(vent.x0, back, vent.z1),
            new Vector3(vent.x0, front, vent.z1), new Vector3(vent.x0, front, vent.z0), Vector3.right, true);
        builder.AddQuad(new Vector3(vent.x1, back, vent.z0), new Vector3(vent.x1, back, vent.z1),
            new Vector3(vent.x1, front, vent.z1), new Vector3(vent.x1, front, vent.z0), Vector3.left, true);

        // Louvres: lower lip outside, upper lip inside, like a stamped locker vent.
        float sy = Mathf.Abs(lossy.y), sz = Mathf.Abs(lossy.z);
        float rise = SlatRise / sz, thick = SlatThickness / sz;
        float outsideY = outerY + outSign * SlatOutset / sy;
        float insideY = innerY - outSign * SlatInset / sy;
        float pitch = (vent.z1 - vent.z0) / SlatCount;
        for (int s = 0; s < SlatCount; s++)
        {
            float zTop = vent.z0 + pitch * (s + 1);
            float zInner = zTop, zOuter = zTop - rise;
            builder.AddSlat(vent.x0, vent.x1, outsideY, zOuter, insideY, zInner, thick);
        }

        Mesh mesh = builder.ToMesh(source.name + "_Vented");
        return mesh;
    }

    private static bool Overlaps(Rect2 a, Rect2 b) => a.x0 < b.x1 && a.x1 > b.x0 && a.z0 < b.z1 && a.z1 > b.z0;

    private struct Vert
    {
        public Vector3 p; public Vector3 n; public Vector2 uv;
        public Vert(Vector3 p, Vector3 n, Vector2 uv) { this.p = p; this.n = n; this.uv = uv; }
        public static Vert Lerp(Vert a, Vert b, float t) =>
            new(Vector3.Lerp(a.p, b.p, t), Vector3.Lerp(a.n, b.n, t).normalized, Vector2.Lerp(a.uv, b.uv, t));
    }

    /// <summary>
    /// Splits a convex polygon at axis == value. <paramref name="piece"/> receives the side
    /// below the value when <paramref name="pieceBelow"/> is true (above otherwise); the
    /// other side is returned. Vertex order (winding) is preserved.
    /// </summary>
    private static List<Vert> Clip(List<Vert> poly, int axis, float value, bool pieceBelow, out List<Vert> piece)
    {
        piece = ClipHalf(poly, axis, value, pieceBelow);
        return ClipHalf(poly, axis, value, !pieceBelow);
    }

    private static List<Vert> ClipHalf(List<Vert> poly, int axis, float value, bool keepBelow)
    {
        var output = new List<Vert>();
        if (poly == null || poly.Count < 3) return output;
        for (int i = 0; i < poly.Count; i++)
        {
            Vert a = poly[i], c = poly[(i + 1) % poly.Count];
            float da = keepBelow ? value - a.p[axis] : a.p[axis] - value;
            float dc = keepBelow ? value - c.p[axis] : c.p[axis] - value;
            if (da >= 0f) output.Add(a);
            if ((da >= 0f) != (dc >= 0f))
                output.Add(Vert.Lerp(a, c, da / (da - dc)));
        }
        return output;
    }

    // Planar UV from the face's own vertices: least-squares affine fit uv = A * (x, z, 1).
    private static System.Func<float, float, Vector2> UvSampler(Vector3[] v, Vector2[] uv, System.Func<Vector3, bool> onFace)
    {
        double sxx = 0, sxz = 0, sx = 0, szz = 0, sz = 0, s1 = 0;
        double ux = 0, uz = 0, u1 = 0, vx = 0, vz = 0, v1 = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (!onFace(v[i])) continue;
            double x = v[i].x, z = v[i].z;
            sxx += x * x; sxz += x * z; sx += x; szz += z * z; sz += z; s1 += 1;
            ux += uv[i].x * x; uz += uv[i].x * z; u1 += uv[i].x;
            vx += uv[i].y * x; vz += uv[i].y * z; v1 += uv[i].y;
        }
        double[,] m = { { sxx, sxz, sx }, { sxz, szz, sz }, { sx, sz, s1 } };
        double[] cu = Solve3(m, new[] { ux, uz, u1 }), cv = Solve3(m, new[] { vx, vz, v1 });
        return (x, z) => new Vector2((float)(cu[0] * x + cu[1] * z + cu[2]), (float)(cv[0] * x + cv[1] * z + cv[2]));
    }

    private static double[] Solve3(double[,] a, double[] b)
    {
        double Det(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
            m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        double d = Det(a);
        if (System.Math.Abs(d) < 1e-30) return new double[] { 0, 0, b[2] / System.Math.Max(1, a[2, 2]) };
        var result = new double[3];
        for (int c = 0; c < 3; c++)
        {
            var m = (double[,])a.Clone();
            for (int r = 0; r < 3; r++) m[r, c] = b[r];
            result[c] = Det(m) / d;
        }
        return result;
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> positions = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<int> indices = new();
        private readonly System.Func<float, float, Vector2> frontUv, backUv;

        public MeshBuilder(System.Func<float, float, Vector2> frontUv, System.Func<float, float, Vector2> backUv)
        {
            this.frontUv = frontUv;
            this.backUv = backUv;
        }

        public void AddPolygon(List<Vert> poly)
        {
            if (poly == null || poly.Count < 3) return;
            int start = positions.Count;
            foreach (Vert v in poly) { positions.Add(v.p); normals.Add(v.n); uvs.Add(v.uv); }
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                indices.Add(start); indices.Add(start + i); indices.Add(start + i + 1);
            }
        }

        public void AddSlat(float x0, float x1, float yOut, float zOut, float yIn, float zIn, float thickness)
        {
            // Cross-section: a thin parallelogram from the outer (lower) lip to the inner (upper) lip.
            Vector3 O0 = new(0, yOut, zOut), I0 = new(0, yIn, zIn);
            Vector3 O1 = O0 + Vector3.back * thickness, I1 = I0 + Vector3.back * thickness;
            Vector3 X0 = new(x0, 0, 0), X1 = new(x1, 0, 0);
            Vector3 along = (I0 - O0).normalized;
            Vector3 up = Vector3.Cross(along, Vector3.right).normalized;
            if (up.z < 0) up = -up;
            AddQuad(O0 + X0, O0 + X1, I0 + X1, I0 + X0, up, true);           // top surface
            AddQuad(O1 + X0, O1 + X1, I1 + X1, I1 + X0, -up, true);          // bottom surface
            AddQuad(O0 + X0, O0 + X1, O1 + X1, O1 + X0, -along, true);       // outer lip
            AddQuad(I0 + X0, I0 + X1, I1 + X1, I1 + X0, along, true);        // inner lip
            AddQuad(O0 + X0, I0 + X0, I1 + X0, O1 + X0, Vector3.left, true); // end caps
            AddQuad(O0 + X1, I0 + X1, I1 + X1, O1 + X1, Vector3.right, true);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool front)
        {
            int start = positions.Count;
            foreach (Vector3 p in new[] { a, b, c, d })
            {
                positions.Add(p);
                normals.Add(normal.normalized);
                uvs.Add(front ? frontUv(p.x, p.z) : backUv(p.x, p.z));
            }
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            if (!flip) { indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 }); }
            else { indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 }); }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = positions.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
#endif
