using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class WalkieTalkieRecordingStorage
{
    public const string DefaultFolderName = "WalkieTalkieRecordings";

    public static string GetDirectoryPath(string folderName = DefaultFolderName)
    {
        string safeFolderName = string.IsNullOrWhiteSpace(folderName)
            ? DefaultFolderName
            : folderName.Trim();

        return Path.Combine(Application.persistentDataPath, safeFolderName);
    }

    public static List<string> GetRecordingFilesOldestFirst(string directoryPath)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return result;

        result.AddRange(Directory.GetFiles(directoryPath, "*.wav", SearchOption.TopDirectoryOnly));
        result.Sort(CompareByAgeThenName);
        return result;
    }

    public static void EnforceRecordingLimit(
        string directoryPath,
        int maximumFileCount,
        Action<string> onFileRemoved = null)
    {
        int safeLimit = Mathf.Max(1, maximumFileCount);
        List<string> files = GetRecordingFilesOldestFirst(directoryPath);
        int removeCount = files.Count - safeLimit;

        for (int i = 0; i < removeCount; i++)
        {
            string path = files[i];
            try
            {
                File.Delete(path);
                onFileRemoved?.Invoke(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WalkieRecorder] Could not remove old recording '{path}': {exception.Message}");
            }
        }
    }

    public static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
    }

    private static int CompareByAgeThenName(string left, string right)
    {
        DateTime leftTime = File.GetCreationTimeUtc(left);
        DateTime rightTime = File.GetCreationTimeUtc(right);
        int timeComparison = leftTime.CompareTo(rightTime);
        return timeComparison != 0
            ? timeComparison
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
