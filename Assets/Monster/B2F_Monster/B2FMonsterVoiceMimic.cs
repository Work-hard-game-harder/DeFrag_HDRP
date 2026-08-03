using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace DeFrag.Monsters.B2F
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class B2FMonsterVoiceMimic : MonoBehaviour
    {
        [Header("Recording Source")]
        [SerializeField] private string recordingFolderName = WalkieTalkieRecordingStorage.DefaultFolderName;
        [SerializeField, Min(0.1f)] private float folderScanInterval = 1f;

        [Header("Playback")]
        [SerializeField] private AudioSource mimicAudioSource;
        [SerializeField, Range(0f, 1f)] private float playbackVolume = 1f;
        [Min(0.1f)] public float mimicPlaybackRange = 20f;
        [SerializeField] private bool deleteFileAfterPlayback = true;

        [Header("Runtime FIFO (Read Only)")]
        [SerializeField] private List<AudioClip> mimicVoiceClipsList = new List<AudioClip>();
        [SerializeField] private List<string> loadedFilePaths = new List<string>();

        [Header("Gizmo")]
        [Tooltip("주황색: 몬스터가 흉내 낸 음성이 플레이어에게 들리는 재생 범위입니다.")]
        [SerializeField] private Color playbackRangeColor = new Color(1f, 0.55f, 0f, 0.18f);

        private readonly HashSet<string> loadingFilePaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> consumedFilePaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Coroutine refreshLoop;
        private bool refreshInProgress;
        private bool hasActivePlayback;
        private int activePlaybackStartFrame;
        private AudioClip activeClip;
        private string activeFilePath;

        public IReadOnlyList<AudioClip> MimicVoiceClips => mimicVoiceClipsList;
        public IReadOnlyList<string> LoadedFilePaths => loadedFilePaths;
        public float MimicPlaybackRange => mimicPlaybackRange;
        public bool HasActivePlayback => hasActivePlayback;
        public bool IsActivePlaybackComplete =>
            hasActivePlayback &&
            Time.frameCount > activePlaybackStartFrame &&
            (mimicAudioSource == null || !mimicAudioSource.isPlaying);

        private string RecordingDirectory =>
            WalkieTalkieRecordingStorage.GetDirectoryPath(recordingFolderName);

        private void Awake()
        {
            ResolveAudioSource();
            RepairParallelLists();
            ApplyAudioRange();
            Directory.CreateDirectory(RecordingDirectory);
        }

        private void OnEnable()
        {
            if (Application.isPlaying && refreshLoop == null)
                refreshLoop = StartCoroutine(RefreshLoop());
        }

        private void OnDisable()
        {
            if (refreshLoop != null)
            {
                StopCoroutine(refreshLoop);
                refreshLoop = null;
            }

            CancelActivePlayback();
            refreshInProgress = false;
            loadingFilePaths.Clear();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < mimicVoiceClipsList.Count; i++)
            {
                AudioClip clip = mimicVoiceClipsList[i];
                if (clip != null)
                    Destroy(clip);
            }
        }

        private void OnValidate()
        {
            folderScanInterval = Mathf.Max(0.1f, folderScanInterval);
            mimicPlaybackRange = Mathf.Max(0.1f, mimicPlaybackRange);
            ResolveAudioSource();
            ApplyAudioRange();
            RepairParallelLists();
        }

        public void RefreshMimicList()
        {
            if (!isActiveAndEnabled || refreshInProgress)
                return;

            StartCoroutine(RefreshMimicListRoutine());
        }

        public bool TryStartNextPlayback(float volumeMultiplier = 1f)
        {
            if (hasActivePlayback || mimicAudioSource == null)
                return false;

            RepairParallelLists();
            if (mimicVoiceClipsList.Count == 0)
            {
                RefreshMimicList();
                return false;
            }

            activeClip = mimicVoiceClipsList[0];
            activeFilePath = loadedFilePaths[0];
            if (activeClip == null)
            {
                RemoveEntryAt(0, false);
                return false;
            }

            mimicAudioSource.clip = activeClip;
            mimicAudioSource.volume = Mathf.Clamp01(playbackVolume * volumeMultiplier);
            mimicAudioSource.Play();

            activePlaybackStartFrame = Time.frameCount;
            hasActivePlayback = true;
            return true;
        }

        public bool CompleteActivePlayback()
        {
            if (!hasActivePlayback || !IsActivePlaybackComplete)
                return false;

            int index = FindLoadedPathIndex(activeFilePath);
            AudioClip completedClip = activeClip;
            string completedPath = activeFilePath;

            ClearActivePlayback();
            if (index >= 0)
                RemoveEntryAt(index, false);

            if (deleteFileAfterPlayback)
                DeleteConsumedFile(completedPath);

            WalkieTalkieRecordingLibrary.Unregister(completedPath, true);
            if (completedClip != null)
                Destroy(completedClip);

            return true;
        }

        public void CancelActivePlayback()
        {
            if (!hasActivePlayback)
                return;

            if (mimicAudioSource != null && mimicAudioSource.isPlaying)
                mimicAudioSource.Stop();

            ClearActivePlayback();
        }

        private IEnumerator RefreshLoop()
        {
            while (isActiveAndEnabled)
            {
                yield return RefreshMimicListRoutine();
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, folderScanInterval));
            }

            refreshLoop = null;
        }

        private IEnumerator RefreshMimicListRoutine()
        {
            if (refreshInProgress)
                yield break;

            refreshInProgress = true;
            Directory.CreateDirectory(RecordingDirectory);
            RemoveEntriesWhoseFilesNoLongerExist();

            List<string> files =
                WalkieTalkieRecordingStorage.GetRecordingFilesOldestFirst(RecordingDirectory);

            foreach (string rawPath in files)
            {
                string path = WalkieTalkieRecordingStorage.NormalizePath(rawPath);
                if (FindLoadedPathIndex(path) >= 0 ||
                    loadingFilePaths.Contains(path) ||
                    consumedFilePaths.Contains(path))
                {
                    continue;
                }

                loadingFilePaths.Add(path);
                yield return LoadRecording(path);
                loadingFilePaths.Remove(path);
            }

            refreshInProgress = false;
        }

        private IEnumerator LoadRecording(string path)
        {
            if (!File.Exists(path))
                yield break;

            string uri = new Uri(path).AbsoluteUri;
            using UnityWebRequest request =
                UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[B2F Mimic] Failed to load '{path}': {request.error}", this);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null || !File.Exists(path) || FindLoadedPathIndex(path) >= 0)
            {
                if (clip != null)
                    Destroy(clip);
                yield break;
            }

            clip.name = Path.GetFileNameWithoutExtension(path);
            mimicVoiceClipsList.Add(clip);
            loadedFilePaths.Add(path);
        }

        private void RemoveEntriesWhoseFilesNoLongerExist()
        {
            for (int i = loadedFilePaths.Count - 1; i >= 0; i--)
            {
                string path = loadedFilePaths[i];
                if (string.Equals(path, activeFilePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!File.Exists(path))
                    RemoveEntryAt(i, true);
            }
        }

        private void DeleteConsumedFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception)
            {
                consumedFilePaths.Add(path);
                Debug.LogWarning($"[B2F Mimic] Could not delete consumed recording '{path}': {exception.Message}", this);
            }
        }

        private void RemoveEntryAt(int index, bool destroyClip)
        {
            if (index < 0 || index >= mimicVoiceClipsList.Count || index >= loadedFilePaths.Count)
                return;

            AudioClip clip = mimicVoiceClipsList[index];
            mimicVoiceClipsList.RemoveAt(index);
            loadedFilePaths.RemoveAt(index);

            if (destroyClip && clip != null)
                Destroy(clip);
        }

        private int FindLoadedPathIndex(string path)
        {
            for (int i = 0; i < loadedFilePaths.Count; i++)
            {
                if (string.Equals(loadedFilePaths[i], path, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private void RepairParallelLists()
        {
            mimicVoiceClipsList ??= new List<AudioClip>();
            loadedFilePaths ??= new List<string>();

            int sharedCount = Mathf.Min(mimicVoiceClipsList.Count, loadedFilePaths.Count);
            if (mimicVoiceClipsList.Count > sharedCount)
                mimicVoiceClipsList.RemoveRange(sharedCount, mimicVoiceClipsList.Count - sharedCount);
            if (loadedFilePaths.Count > sharedCount)
                loadedFilePaths.RemoveRange(sharedCount, loadedFilePaths.Count - sharedCount);
        }

        private void ResolveAudioSource()
        {
            if (mimicAudioSource == null)
                mimicAudioSource = GetComponent<AudioSource>();
        }

        private void ApplyAudioRange()
        {
            if (mimicAudioSource == null)
                return;

            mimicAudioSource.playOnAwake = false;
            mimicAudioSource.loop = false;
            mimicAudioSource.spatialBlend = 1f;
            mimicAudioSource.maxDistance = mimicPlaybackRange;
            mimicAudioSource.minDistance = Mathf.Min(mimicAudioSource.minDistance, mimicPlaybackRange);
        }

        private void ClearActivePlayback()
        {
            if (mimicAudioSource != null && mimicAudioSource.clip == activeClip)
                mimicAudioSource.clip = null;

            activeClip = null;
            activeFilePath = null;
            hasActivePlayback = false;
        }

        private void OnDrawGizmosSelected()
        {
            float range = Mathf.Max(0.1f, mimicPlaybackRange);
            Gizmos.color = playbackRangeColor;
            Gizmos.DrawSphere(transform.position, range);

            Color wireColor = playbackRangeColor;
            wireColor.a = Mathf.Max(0.6f, wireColor.a);
            Gizmos.color = wireColor;
            Gizmos.DrawWireSphere(transform.position, range);

#if UNITY_EDITOR
            UnityEditor.Handles.color = new Color(1f, 0.65f, 0.1f, 1f);
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.2f,
                $"Mimic Playback Range ({range:0.0}m)");
#endif
        }
    }
}
