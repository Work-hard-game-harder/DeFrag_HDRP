using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "DownloadCommandWordLibrary",
    menuName = "DeFrag/Hacking/Download Command Word Library")]
public sealed class DownloadCommandWordLibrary : ScriptableObject
{
    [SerializeField] private List<string> verbs = new()
    {
        "OVERRIDE", "VALIDATE", "SYNCHRONIZE", "DECRYPT",
        "RECALIBRATE", "AUTHORIZE", "INITIALIZE", "QUARANTINE"
    };

    [SerializeField] private List<string> words = new()
    {
        "SECURITY", "PROTOCOL", "PROTECTION", "CREDENTIAL",
        "FIREWALL", "DATABASE", "MAINFRAME", "DIRECTORY",
        "SEQUENCE", "NETWORK", "TERMINAL", "SUBSYSTEM",
        "REPOSITORY", "INTEGRITY", "HANDSHAKE", "PERMISSION",
        "ARCHIVE", "STORAGE", "MEMORY", "CIPHER"
    };

    public DownloadCommand CreateCommand(int archiveNumber)
    {
        string verb = Pick(verbs, "OVERRIDE");
        string first = Pick(words, "SECURITY");
        string second = PickDifferent(words, first, "PROTOCOL");
        string final = PickDifferent(words, second, "PROTECTION");
        return new DownloadCommand(verb, first, second, archiveNumber, final);
    }

    private static string Pick(IReadOnlyList<string> source, string fallback)
    {
        return source.Count == 0
            ? fallback
            : Normalize(source[UnityEngine.Random.Range(0, source.Count)], fallback);
    }

    private static string PickDifferent(
        IReadOnlyList<string> source,
        string excluded,
        string fallback)
    {
        if (source.Count < 2)
            return fallback;

        string value;
        do value = Pick(source, fallback);
        while (string.Equals(value, excluded, StringComparison.Ordinal));
        return value;
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().Replace(' ', '_').ToUpperInvariant();
    }
}

public readonly struct DownloadCommand
{
    private readonly string[] tokens;

    public DownloadCommand(
        string verb,
        string first,
        string second,
        int archiveNumber,
        string final)
    {
        tokens = new[]
        {
            verb, first, second, archiveNumber.ToString("00"), final
        };
    }

    public string FullText => string.Join("_", tokens);

    public string TokenAt(int index) => tokens[index];

    public string ObscuredText(int hiddenIndex)
    {
        string[] visible = (string[])tokens.Clone();
        visible[hiddenIndex] = new string('█', tokens[hiddenIndex].Length);
        return string.Join("_", visible);
    }
}
