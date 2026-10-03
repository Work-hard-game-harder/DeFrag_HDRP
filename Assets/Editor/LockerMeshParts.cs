#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mesh helpers shared by the locker editor bakers. The imported door mesh is one slab plus
/// a few small disconnected pieces (the padlock). The prefab root is scaled non-uniformly
/// (250, 150, 130), which stretches those pieces; <see cref="UnsquashLock"/> counter-scales them.
/// </summary>
public static class LockerMeshParts
{
    /// <summary>Per-vertex flag: true for every vertex outside the largest connected piece.</summary>
    public static bool[] FindLockVertices(Vector3[] vertices, int[] triangles)
    {
        int[] component = ConnectedComponents(vertices, triangles, out int[] sizes);
        int largest = 0;
        for (int c = 1; c < sizes.Length; c++)
            if (sizes[c] > sizes[largest]) largest = c;

        var isLock = new bool[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            isLock[i] = component[i] != largest;
        return isLock;
    }

    /// <summary>
    /// Rescales the lock pieces so they look uniformly scaled under <paramref name="lossyScale"/>.
    /// The height axis (local Z) is kept; width (X) is shrunk around the lock centre and depth (Y)
    /// around the door's mid-plane so the lock stays attached to the slab.
    /// </summary>
    public static void UnsquashLock(Vector3[] vertices, Vector3[] normals, int[] triangles, Vector3 lossyScale)
    {
        bool[] isLock = FindLockVertices(vertices, triangles);
        float sx = Mathf.Abs(lossyScale.x), sy = Mathf.Abs(lossyScale.y), sz = Mathf.Abs(lossyScale.z);
        if (sx < 1e-6f || sy < 1e-6f || sz < 1e-6f) return;
        float fx = sz / sx, fy = sz / sy;

        float lockMinX = float.MaxValue, lockMaxX = float.MinValue;
        float slabMinY = float.MaxValue, slabMaxY = float.MinValue;
        bool anyLock = false;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (isLock[i])
            {
                anyLock = true;
                lockMinX = Mathf.Min(lockMinX, vertices[i].x);
                lockMaxX = Mathf.Max(lockMaxX, vertices[i].x);
            }
            else
            {
                slabMinY = Mathf.Min(slabMinY, vertices[i].y);
                slabMaxY = Mathf.Max(slabMaxY, vertices[i].y);
            }
        }
        if (!anyLock) return;

        float pivotX = (lockMinX + lockMaxX) * 0.5f;
        float pivotY = (slabMinY + slabMaxY) * 0.5f;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!isLock[i]) continue;
            Vector3 p = vertices[i];
            vertices[i] = new Vector3(pivotX + (p.x - pivotX) * fx, pivotY + (p.y - pivotY) * fy, p.z);
            if (normals != null && i < normals.Length)
            {
                Vector3 n = normals[i];
                normals[i] = new Vector3(n.x / fx, n.y / fy, n.z).normalized;
            }
        }
    }

    private static int[] ConnectedComponents(Vector3[] vertices, int[] triangles, out int[] sizes)
    {
        // Weld by position first: imported meshes split vertices along UV seams.
        var welded = new Dictionary<Vector3Int, int>();
        var weldId = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i] * 1e6f;
            var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
            if (!welded.TryGetValue(key, out int id))
            {
                id = welded.Count;
                welded.Add(key, id);
            }
            weldId[i] = id;
        }

        var parent = new int[welded.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = Find(weldId[triangles[i]]);
            parent[Find(weldId[triangles[i + 1]])] = a;
            parent[Find(weldId[triangles[i + 2]])] = a;
        }

        var rootToComponent = new Dictionary<int, int>();
        var component = new int[vertices.Length];
        var counts = new List<int>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int root = Find(weldId[i]);
            if (!rootToComponent.TryGetValue(root, out int c))
            {
                c = counts.Count;
                rootToComponent.Add(root, c);
                counts.Add(0);
            }
            component[i] = c;
            counts[c]++;
        }
        sizes = counts.ToArray();
        return component;
    }
}
#endif
