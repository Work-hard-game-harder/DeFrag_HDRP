using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the player character's right forearm and hand, posed as a pointing index finger, into a
/// static mesh for the elevator keypad. The mesh pivot is the index fingertip and +Z is the pointing
/// direction; the prefab reuses the player's own material so the hand matches the character.
/// Menu: DEFRAG > Keypad > Bake Pointing Hand.
/// </summary>
public static class KeypadHandBaker
{
    private const string ModelPath = "Assets/Player/0724_PlayerCharacter.fbx";
    private const string PlayerPrefabPath = "Assets/Player/PlayerCharacter(H).prefab";
    private const string OutputFolder = "Assets/Art/LobbyF/Keypad";
    private const string MeshPath = OutputFolder + "/Keypad_Hand_Point.asset";
    private const string PrefabPath = OutputFolder + "/Keypad_Hand.prefab";

    // Degrees around each finger bone's local X axis (the rig's curl axis); the index stays nearly straight.
    private static readonly Dictionary<string, float> Curl = new()
    {
        { "mixamorig:RightHandRing1", 75f }, { "mixamorig:RightHandRing2", 95f }, { "mixamorig:RightHandRing3", 70f },
        { "mixamorig:RightHandMiddle1", 75f }, { "mixamorig:RightHandMiddle2", 95f }, { "mixamorig:RightHandMiddle3", 70f },
        { "mixamorig:RightHandIndex1", 6f }, { "mixamorig:RightHandIndex2", 4f }
    };

    [MenuItem("DEFRAG/Keypad/Bake Pointing Hand")]
    public static void Bake()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError($"[KeypadHandBaker] Missing {ModelPath}"); return; }
        var instance = (GameObject)Object.Instantiate(model);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = instance.GetComponent<Animator>();
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (Transform bone in instance.GetComponentsInChildren<Transform>())
                if (Curl.TryGetValue(bone.name, out float degrees)) bone.localRotation *= Quaternion.AngleAxis(degrees, Vector3.right);

            UnityEngine.Mesh mesh = BuildHandMesh(animator, skin);
            Directory.CreateDirectory(OutputFolder);
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(MeshPath);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, MeshPath);

            Material[] playerMaterials = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath)
                .GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials;
            var hand = new GameObject("Keypad Hand");
            hand.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = hand.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { playerMaterials[0] };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(hand, PrefabPath);
            Object.DestroyImmediate(hand);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KeypadHandBaker] Baked {mesh.vertexCount} vertices into {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static UnityEngine.Mesh BuildHandMesh(Animator animator, SkinnedMeshRenderer skin)
    {
        var baked = new UnityEngine.Mesh();
        skin.BakeMesh(baked, true);
        UnityEngine.Mesh source = skin.sharedMesh;
        BoneWeight[] weights = source.boneWeights;

        var boneIndex = new Dictionary<Transform, int>();
        for (int i = 0; i < skin.bones.Length; i++) if (skin.bones[i] != null) boneIndex[skin.bones[i]] = i;
        var handBones = new HashSet<int>();
        var indexBones = new HashSet<int>();
        foreach (Transform bone in skin.bones)
        {
            if (bone == null) continue;
            if (bone.name.StartsWith("mixamorig:RightHand")) handBones.Add(boneIndex[bone]);
            if (bone.name.StartsWith("mixamorig:RightHandIndex")) indexBones.Add(boneIndex[bone]);
        }
        // The whole forearm sleeve is kept: its cut end sits at the elbow, well outside the frame.
        handBones.Add(boneIndex[animator.GetBoneTransform(HumanBodyBones.RightLowerArm)]);

        Transform knuckle = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
        Transform distal = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);
        Vector3[] vertices = baked.vertices;
        var world = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) world[i] = skin.transform.TransformPoint(vertices[i]);

        Vector3 roughDirection = (distal.position - knuckle.position).normalized;
        Vector3 tip = distal.position;
        float furthest = float.MinValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!indexBones.Contains(weights[i].boneIndex0)) continue;
            float along = Vector3.Dot(world[i] - knuckle.position, roughDirection);
            if (along > furthest) { furthest = along; tip = world[i]; }
        }
        Vector3 pointing = (tip - knuckle.position).normalized;
        Quaternion toLocal = Quaternion.Inverse(Quaternion.LookRotation(pointing, Vector3.ProjectOnPlane(Vector3.up, pointing).normalized));

        var remap = new int[vertices.Length];
        var outVertices = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outUvs = new List<Vector2>();
        Vector3[] normals = baked.normals;
        Vector2[] uvs = source.uv;
        for (int i = 0; i < vertices.Length; i++)
        {
            remap[i] = -1;
            if (!handBones.Contains(weights[i].boneIndex0)) continue;
            remap[i] = outVertices.Count;
            outVertices.Add(toLocal * (world[i] - tip));
            outNormals.Add(toLocal * skin.transform.TransformDirection(normals[i]));
            outUvs.Add(i < uvs.Length ? uvs[i] : Vector2.zero);
        }
        // Only the body sub-mesh (0) carries the arm.
        var triangles = new List<int>();
        int[] sourceTriangles = source.GetTriangles(0);
        for (int t = 0; t < sourceTriangles.Length; t += 3)
        {
            int a = remap[sourceTriangles[t]], b = remap[sourceTriangles[t + 1]], c = remap[sourceTriangles[t + 2]];
            if (a < 0 || b < 0 || c < 0) continue;
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }

        var mesh = new UnityEngine.Mesh { name = "Keypad_Hand_Point" };
        mesh.SetVertices(outVertices);
        mesh.SetNormals(outNormals);
        mesh.SetUVs(0, outUvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        Object.DestroyImmediate(baked);
        return mesh;
    }
}
