using System;
using System.Collections.Generic;
using UnityEngine;

public static class TerminalArchiveNumberRegistry
{
    private static readonly Dictionary<string, int> AssignedNumbers = new();
    private static readonly HashSet<int> UsedNumbers = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        AssignedNumbers.Clear();
        UsedNumbers.Clear();
    }

    public static int GetNumber(ConnectionDevice terminal)
    {
        string id = terminal.TerminalId;
        if (AssignedNumbers.TryGetValue(id, out int assigned))
            return assigned;

        int candidate = PositiveHash(id) % 100;
        for (int offset = 0; offset < 100; offset++)
        {
            int number = (candidate + offset) % 100;
            if (!UsedNumbers.Add(number))
                continue;

            AssignedNumbers.Add(id, number);
            return number;
        }

        throw new InvalidOperationException(
            "Download archive numbers support at most 100 unique terminal IDs.");
    }

    private static int PositiveHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value)
                hash = hash * 31 + character;
            return hash & int.MaxValue;
        }
    }
}
