namespace Orkeon.Analysis.Abstractions.Editing;

/// <summary>Process-wide per-file edit lock registry shared by edit_file and edit_batch.</summary>
public static class FileEditLocks
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, string> Held = new(StringComparer.Ordinal);

    /// <summary>Takes every path for the holder, all or nothing; on refusal <paramref name="heldPath"/> is the held path.</summary>
    public static bool TryAcquire(IReadOnlyList<string> paths, string holderId, out string? heldPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(holderId);
        lock (Gate)
        {
            heldPath = paths.FirstOrDefault(Held.ContainsKey);
            if (heldPath is not null) return false;
            foreach (var path in paths) Held[path] = holderId;
            return true;
        }
    }

    /// <summary>Releases the paths the holder holds.</summary>
    public static void Release(IReadOnlyList<string> paths, string holderId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (Gate)
        {
            foreach (var path in paths)
            {
                if (Held.TryGetValue(path, out var owner) && owner == holderId) Held.Remove(path);
            }
        }
    }

    internal static bool IsActive(string holderId)
    {
        lock (Gate) return Held.ContainsValue(holderId);
    }
}
