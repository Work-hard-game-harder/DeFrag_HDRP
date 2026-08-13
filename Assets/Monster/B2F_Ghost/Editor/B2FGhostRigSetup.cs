using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the Ghost prefab on the rigged Meshy model instead of the static Ghost.fbx mesh.
/// The operation is idempotent and can also be run again from the Tools menu.
/// </summary>
[InitializeOnLoad]
internal static class B2FGhostRigSetup
{
    private const string IdleModelPath =
        "Assets/Monster/B2F_Ghost/Meshy_AI_Animation_019fea8d-50a5-7f89-9626-33fb707cab66_withSkin.fbx";

    private const string MovingModelPath =
        "Assets/Monster/B2F_Ghost/Meshy_AI_Animation_019fea8e-041d-7702-938d-102e1229073f_withSkin (1).fbx";

    private const string StaticModelPath = "Assets/Monster/B2F_Ghost/Ghost.fbx";
    private const string GhostPrefabPath = "Assets/Monster/B2F_Ghost/Ghost.prefab";
    private const string ControllerPath = "Assets/Monster/B2F_Ghost/B2F_Ghost.controller";
    private const string AnimatedVisualName = "AnimatedGhostVisual";

    static B2FGhostRigSetup()
    {
        EditorApplication.delayCall += ConfigureWhenReady;
    }

    [MenuItem("Tools/DeFrag/B2F Ghost/Repair Rig And Prefab")]
    private static void ConfigureFromMenu()
    {
        ConfigureWhenReady();
    }

    private static void ConfigureWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            return;
        }

        Avatar sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(IdleModelPath)
            .OfType<Avatar>()
            .FirstOrDefault();

        if (sourceAvatar == null)
        {
            AssetDatabase.ImportAsset(IdleModelPath, ImportAssetOptions.ForceUpdate);
            sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(IdleModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        if (sourceAvatar == null)
        {
            Debug.LogError($"B2F Ghost rig setup failed: no Generic Avatar was generated from '{IdleModelPath}'.");
            return;
        }

        ConfigureMovingAvatar(sourceAvatar);
        ConfigureGhostPrefab(sourceAvatar);
    }

    private static void ConfigureMovingAvatar(Avatar sourceAvatar)
    {
        if (AssetImporter.GetAtPath(MovingModelPath) is not ModelImporter importer)
        {
            Debug.LogError($"B2F Ghost rig setup failed: ModelImporter was not found for '{MovingModelPath}'.");
            return;
        }

        bool changed = importer.animationType != ModelImporterAnimationType.Generic ||
                       importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther ||
                       importer.sourceAvatar != sourceAvatar;

        if (!changed)
        {
            return;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = sourceAvatar;
        importer.SaveAndReimport();
    }

    private static void ConfigureGhostPrefab(Avatar sourceAvatar)
    {
        GameObject riggedModel = AssetDatabase.LoadAssetAtPath<GameObject>(IdleModelPath);
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

        if (riggedModel == null || controller == null)
        {
            Debug.LogError("B2F Ghost rig setup failed: the rigged model or Animator Controller is missing.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(GhostPrefabPath);
        try
        {
            Transform visualTransform = root.transform.Find(AnimatedVisualName);
            GameObject visual;

            if (visualTransform == null)
            {
                DisableStaticModelRenderers(root);
                visual = (GameObject)PrefabUtility.InstantiatePrefab(riggedModel, root.transform);
                visual.name = AnimatedVisualName;
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one;
            }
            else
            {
                visual = visualTransform.gameObject;
            }

            Animator visualAnimator = visual.GetComponent<Animator>();
            if (visualAnimator == null)
            {
                visualAnimator = visual.AddComponent<Animator>();
            }

            visualAnimator.avatar = sourceAvatar;
            visualAnimator.runtimeAnimatorController = controller;
            visualAnimator.applyRootMotion = false;
            visualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Animator oldRootAnimator = root.GetComponent<Animator>();
            if (oldRootAnimator != null && oldRootAnimator != visualAnimator)
            {
                Object.DestroyImmediate(oldRootAnimator);
            }

            B2F_GhostController ghostController = root.GetComponent<B2F_GhostController>();
            if (ghostController != null)
            {
                SerializedObject serializedController = new SerializedObject(ghostController);
                SerializedProperty animatorProperty = serializedController.FindProperty("animator");
                if (animatorProperty != null)
                {
                    animatorProperty.objectReferenceValue = visualAnimator;
                    serializedController.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, GhostPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void DisableStaticModelRenderers(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            if (source != null && AssetDatabase.GetAssetPath(source) == StaticModelPath)
            {
                renderer.enabled = false;
            }
        }
    }
}
