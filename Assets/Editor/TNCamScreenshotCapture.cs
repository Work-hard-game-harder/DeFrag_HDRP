using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

internal static class TNCamScreenshotCapture
{
    private const string CameraName = "TNCam";
    private const string OutputDirectory = "Assets/Captures";
    private const int CaptureWidth = 1920;
    private const int CaptureHeight = 1080;

    [MenuItem("Tools/Thumbnail/Capture TNCam PNG")]
    private static void Capture()
    {
        Camera camera = FindCamera(SceneManager.GetActiveScene(), CameraName);
        if (camera == null)
        {
            EditorUtility.DisplayDialog(
                "TNCam Capture",
                $"활성 씬에서 '{CameraName}' 카메라를 찾을 수 없습니다.",
                "확인");
            return;
        }

        Directory.CreateDirectory(OutputDirectory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string assetPath = $"{OutputDirectory}/B1F_Thumbnail_{timestamp}.png";
        string absolutePath = Path.GetFullPath(assetPath);

        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        var renderTexture = new RenderTexture(
            CaptureWidth,
            CaptureHeight,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        var screenshot = new Texture2D(
            CaptureWidth,
            CaptureHeight,
            TextureFormat.RGBA32,
            false,
            false);

        try
        {
            renderTexture.Create();
            camera.targetTexture = renderTexture;

            var renderRequest = new HDRenderPipeline.StandardRequest
            {
                destination = renderTexture,
            };

            if (!RenderPipeline.SupportsRenderRequest(camera, renderRequest))
            {
                throw new InvalidOperationException("현재 HDRP 설정에서 카메라 캡처 요청을 지원하지 않습니다.");
            }

            RenderPipeline.SubmitRenderRequest(camera, renderRequest);

            RenderTexture.active = renderTexture;
            screenshot.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
            screenshot.Apply(false, false);

            File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(screenshot);
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }

        AssetDatabase.Refresh();
        Texture2D capturedAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        Selection.activeObject = capturedAsset;
        EditorGUIUtility.PingObject(capturedAsset);

        Debug.Log($"TNCam 캡처 저장 완료: {assetPath}");
    }

    private static Camera FindCamera(Scene scene, string cameraName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Camera camera = FindCameraRecursive(root.transform, cameraName);
            if (camera != null)
            {
                return camera;
            }
        }

        return null;
    }

    private static Camera FindCameraRecursive(Transform current, string cameraName)
    {
        if (string.Equals(current.name, cameraName, StringComparison.Ordinal))
        {
            return current.GetComponent<Camera>();
        }

        for (int index = 0; index < current.childCount; index++)
        {
            Camera camera = FindCameraRecursive(current.GetChild(index), cameraName);
            if (camera != null)
            {
                return camera;
            }
        }

        return null;
    }
}
