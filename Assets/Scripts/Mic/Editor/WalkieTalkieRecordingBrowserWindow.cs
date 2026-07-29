using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace DeFrag.EditorTools
{
    public sealed class WalkieTalkieRecordingBrowserWindow : EditorWindow
    {
        private const string MenuPath = "Tools/DeFrag/Walkie-Talkie Recordings";
        private const string ImportDirectory = "Assets/WalkieTalkieRecordings";
        private const double RefreshIntervalSeconds = 1d;

        private readonly List<RecordingEntry> recordings = new List<RecordingEntry>();
        private readonly HashSet<string> selectedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Vector2 scrollPosition;
        private bool automaticRefresh = true;
        private double nextRefreshAt;
        private UnityWebRequest previewRequest;
        private AudioClip previewClip;
        private string previewPath;
        private string statusMessage;

        private string RecordingDirectory =>
            WalkieTalkieRecordingStorage.GetDirectoryPath();

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<WalkieTalkieRecordingBrowserWindow>();
            window.titleContent = new GUIContent("Walkie Recordings");
            window.minSize = new Vector2(720f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += EditorUpdate;
            RefreshRecordings();
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            StopPreview();
        }

        private void EditorUpdate()
        {
            if (!automaticRefresh || EditorApplication.timeSinceStartup < nextRefreshAt)
                return;

            RefreshRecordings();
            Repaint();
        }

        private void OnGUI()
        {
            DrawDescription();
            DrawToolbar();
            DrawSummary();
            DrawRecordingList();

            if (!string.IsNullOrWhiteSpace(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
        }

        private void DrawDescription()
        {
            EditorGUILayout.HelpBox(
                "게임은 persistentDataPath의 WAV를 사용합니다. 필요한 녹음만 선택해 " +
                "Assets/WalkieTalkieRecordings로 가져오면 정식 AudioClip 에셋으로 보존됩니다. " +
                "B2F가 재생을 완료한 원본 파일은 삭제될 수 있습니다.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Runtime Folder", RecordingDirectory);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    RefreshRecordings();

                if (GUILayout.Button("폴더 열기", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    RevealRecordingDirectory();

                using (new EditorGUI.DisabledScope(selectedPaths.Count == 0))
                {
                    if (GUILayout.Button(
                            $"선택 항목 가져오기 ({selectedPaths.Count})",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(150f)))
                    {
                        ImportRecordings(selectedPaths.ToArray());
                    }
                }

                GUILayout.FlexibleSpace();
                automaticRefresh = GUILayout.Toggle(
                    automaticRefresh,
                    "자동 새로고침",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(105f));
            }
        }

        private void DrawSummary()
        {
            long totalBytes = recordings.Sum(entry => entry.FileSizeBytes);
            EditorGUILayout.LabelField(
                $"녹음 {recordings.Count}개 · 총 {FormatBytes(totalBytes)} · 오래된 순서(FIFO)",
                EditorStyles.boldLabel);
        }

        private void DrawRecordingList()
        {
            DrawHeader();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (recordings.Count == 0)
            {
                EditorGUILayout.Space(12f);
                EditorGUILayout.LabelField(
                    "현재 저장된 WAV 녹음이 없습니다.",
                    EditorStyles.centeredGreyMiniLabel);
            }

            foreach (RecordingEntry entry in recordings)
                DrawRecordingRow(entry);

            EditorGUILayout.EndScrollView();
        }

        private static void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("", GUILayout.Width(20f));
                GUILayout.Label("파일명", EditorStyles.boldLabel, GUILayout.MinWidth(250f));
                GUILayout.Label("녹음 시각", EditorStyles.boldLabel, GUILayout.Width(145f));
                GUILayout.Label("크기", EditorStyles.boldLabel, GUILayout.Width(75f));
                GUILayout.Label("미리 듣기", EditorStyles.boldLabel, GUILayout.Width(75f));
                GUILayout.Label("에셋", EditorStyles.boldLabel, GUILayout.Width(65f));
            }
        }

        private void DrawRecordingRow(RecordingEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool wasSelected = selectedPaths.Contains(entry.Path);
                bool isSelected = GUILayout.Toggle(wasSelected, GUIContent.none, GUILayout.Width(20f));
                if (isSelected != wasSelected)
                {
                    if (isSelected)
                        selectedPaths.Add(entry.Path);
                    else
                        selectedPaths.Remove(entry.Path);
                }

                GUILayout.Label(entry.FileName, GUILayout.MinWidth(250f));
                GUILayout.Label(
                    entry.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    GUILayout.Width(145f));
                GUILayout.Label(FormatBytes(entry.FileSizeBytes), GUILayout.Width(75f));

                bool isPreviewing = string.Equals(
                    previewPath,
                    entry.Path,
                    StringComparison.OrdinalIgnoreCase);

                if (GUILayout.Button(isPreviewing ? "정지" : "재생", GUILayout.Width(75f)))
                {
                    if (isPreviewing)
                        StopPreview();
                    else
                        BeginPreview(entry.Path);
                }

                if (GUILayout.Button("가져오기", GUILayout.Width(65f)))
                    ImportRecordings(new[] { entry.Path });
            }
        }

        private void RefreshRecordings()
        {
            nextRefreshAt = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
            Directory.CreateDirectory(RecordingDirectory);

            recordings.Clear();
            foreach (string path in
                     WalkieTalkieRecordingStorage.GetRecordingFilesOldestFirst(RecordingDirectory))
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                    continue;

                recordings.Add(new RecordingEntry(
                    WalkieTalkieRecordingStorage.NormalizePath(path),
                    file.Name,
                    file.Length,
                    file.CreationTime));
            }

            selectedPaths.RemoveWhere(path => !File.Exists(path));

            if (!string.IsNullOrWhiteSpace(previewPath) && !File.Exists(previewPath))
                StopPreview();
        }

        private void BeginPreview(string path)
        {
            StopPreview();
            if (!File.Exists(path))
            {
                statusMessage = "미리 들을 파일이 이미 삭제되었습니다.";
                RefreshRecordings();
                return;
            }

            previewPath = path;
            previewRequest = UnityWebRequestMultimedia.GetAudioClip(
                new Uri(path).AbsoluteUri,
                AudioType.WAV);
            previewRequest.SendWebRequest();
            EditorApplication.update += PollPreviewRequest;
            statusMessage = $"불러오는 중: {Path.GetFileName(path)}";
        }

        private void PollPreviewRequest()
        {
            if (previewRequest == null || !previewRequest.isDone)
                return;

            EditorApplication.update -= PollPreviewRequest;

            if (previewRequest.result != UnityWebRequest.Result.Success)
            {
                statusMessage = $"미리 듣기 로드 실패: {previewRequest.error}";
                previewRequest.Dispose();
                previewRequest = null;
                previewPath = null;
                Repaint();
                return;
            }

            previewClip = DownloadHandlerAudioClip.GetContent(previewRequest);
            previewRequest.Dispose();
            previewRequest = null;

            if (previewClip == null || !EditorAudioPreview.Play(previewClip))
            {
                statusMessage = "Unity Audio Preview API를 실행할 수 없습니다.";
                StopPreview();
            }
            else
            {
                statusMessage = $"재생 중: {Path.GetFileName(previewPath)}";
            }

            Repaint();
        }

        private void StopPreview()
        {
            EditorApplication.update -= PollPreviewRequest;
            EditorAudioPreview.Stop();

            if (previewRequest != null)
            {
                previewRequest.Abort();
                previewRequest.Dispose();
                previewRequest = null;
            }

            if (previewClip != null)
            {
                DestroyImmediate(previewClip);
                previewClip = null;
            }

            previewPath = null;
        }

        private void ImportRecordings(IReadOnlyCollection<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return;

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "WalkieTalkieRecordings"));
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                return;

            var importedAssets = new List<UnityEngine.Object>();
            int importedCount = 0;

            foreach (string sourcePath in paths)
            {
                if (!File.Exists(sourcePath))
                    continue;

                string desiredAssetPath =
                    $"{ImportDirectory}/{Path.GetFileName(sourcePath)}".Replace('\\', '/');
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(desiredAssetPath);
                string destinationPath = Path.Combine(
                    projectRoot,
                    assetPath.Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    File.Copy(sourcePath, destinationPath, false);
                    AssetDatabase.ImportAsset(
                        assetPath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);

                    AudioClip importedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                    if (importedClip != null)
                        importedAssets.Add(importedClip);
                    importedCount++;
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[Walkie Recording Browser] Import failed for '{sourcePath}': {exception.Message}");
                }
            }

            AssetDatabase.Refresh();
            if (importedAssets.Count > 0)
            {
                Selection.objects = importedAssets.ToArray();
                EditorGUIUtility.PingObject(importedAssets[0]);
            }

            selectedPaths.Clear();
            statusMessage = $"{importedCount}개 녹음을 {ImportDirectory}로 가져왔습니다.";
        }

        private void RevealRecordingDirectory()
        {
            Directory.CreateDirectory(RecordingDirectory);
            EditorUtility.RevealInFinder(RecordingDirectory);
        }

        private static string FormatBytes(long byteCount)
        {
            if (byteCount < 1024)
                return $"{byteCount} B";
            if (byteCount < 1024 * 1024)
                return $"{byteCount / 1024f:F1} KB";
            return $"{byteCount / (1024f * 1024f):F1} MB";
        }

        private sealed class RecordingEntry
        {
            public RecordingEntry(string path, string fileName, long fileSizeBytes, DateTime createdAt)
            {
                Path = path;
                FileName = fileName;
                FileSizeBytes = fileSizeBytes;
                CreatedAt = createdAt;
            }

            public string Path { get; }
            public string FileName { get; }
            public long FileSizeBytes { get; }
            public DateTime CreatedAt { get; }
        }

        private static class EditorAudioPreview
        {
            private const BindingFlags StaticFlags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            private static readonly Type AudioUtilType =
                typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

            public static bool Play(AudioClip clip)
            {
                if (AudioUtilType == null || clip == null)
                    return false;

                Stop();
                MethodInfo method = AudioUtilType
                    .GetMethods(StaticFlags)
                    .FirstOrDefault(candidate =>
                        (candidate.Name == "PlayPreviewClip" || candidate.Name == "PlayClip") &&
                        candidate.GetParameters().Length >= 1 &&
                        candidate.GetParameters()[0].ParameterType == typeof(AudioClip));

                if (method == null)
                    return false;

                ParameterInfo[] parameters = method.GetParameters();
                object[] arguments = new object[parameters.Length];
                arguments[0] = clip;
                for (int i = 1; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType == typeof(int))
                        arguments[i] = 0;
                    else if (parameters[i].ParameterType == typeof(bool))
                        arguments[i] = false;
                    else
                        arguments[i] = parameters[i].HasDefaultValue
                            ? parameters[i].DefaultValue
                            : null;
                }

                method.Invoke(null, arguments);
                return true;
            }

            public static void Stop()
            {
                if (AudioUtilType == null)
                    return;

                MethodInfo method =
                    AudioUtilType.GetMethod("StopAllPreviewClips", StaticFlags) ??
                    AudioUtilType.GetMethod("StopAllClips", StaticFlags);
                method?.Invoke(null, null);
            }
        }
    }
}
