using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class ImportB2FThumbnailPropsOnce
{
    private const string SourceScenePath = "Assets/Scene/B2F.unity";
    private const string TargetScenePath = "Assets/Scenes/B1F_Thumbnail.unity";
    private const string SessionKey = "DeFrag.ImportB2FThumbnailPropsOnce.v1";

    private static readonly string[] SourceNames =
    {
        "Plane.174_cell",
        "Electric wire",
        "Hall1_Light",
    };

    static ImportB2FThumbnailPropsOnce()
    {
        EditorApplication.delayCall += ImportOnce;
    }

    [MenuItem("Tools/Thumbnail/Import B2F Props")]
    private static void ImportFromMenu()
    {
        Import();
    }

    private static void ImportOnce()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        SessionState.SetBool(SessionKey, true);
        Import();
    }

    private static void Import()
    {

        Scene sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
        Scene targetScene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Additive);

        try
        {
            foreach (string sourceName in SourceNames)
            {
                string targetName = $"B2F_{sourceName}";
                if (FindByName(targetScene, targetName) != null)
                {
                    continue;
                }

                GameObject source = FindByName(sourceScene, sourceName);
                if (source == null)
                {
                    Debug.LogError($"B2F thumbnail import could not find: {sourceName}");
                    continue;
                }

                Vector3 worldPosition = source.transform.position;
                Quaternion worldRotation = source.transform.rotation;
                Vector3 worldScale = source.transform.lossyScale;

                GameObject clone = UnityEngine.Object.Instantiate(source);
                clone.name = targetName;
                clone.transform.SetParent(null, false);
                clone.transform.SetPositionAndRotation(worldPosition, worldRotation);
                clone.transform.localScale = worldScale;
                SceneManager.MoveGameObjectToScene(clone, targetScene);
            }

            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(targetScene, TargetScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Imported B2F thumbnail props into B1F_Thumbnail.");
        }
        finally
        {
            EditorSceneManager.CloseScene(sourceScene, true);
            EditorSceneManager.CloseScene(targetScene, true);
        }
    }

    private static GameObject FindByName(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameObject match = FindByNameRecursive(root.transform, objectName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static GameObject FindByNameRecursive(Transform current, string objectName)
    {
        if (string.Equals(current.name, objectName, StringComparison.OrdinalIgnoreCase))
        {
            return current.gameObject;
        }

        for (int index = 0; index < current.childCount; index++)
        {
            GameObject match = FindByNameRecursive(current.GetChild(index), objectName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
}
