namespace Orkeon.Analysis.Core.Editing;

/// <summary>Process-wide registry of the files a running batch holds, keyed by virtual path; the value is the batch id.</summary>
internal static class EditLocks
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, string> Held = new(StringComparer.Ordinal);

    public static void Acquire(IReadOnlyList<string> paths, string batchId)
    {
        lock (Gate)
        {
            var taken = paths.FirstOrDefault(Held.ContainsKey);
            if (taken is not null)
                throw new EditConflictException($"'{taken}' is held by another edit batch in progress.");
            foreach (var path in paths) Held[path] = batchId;
        }
    }

    public static void Release(IReadOnlyList<string> paths, string batchId)
    {
        lock (Gate)
        {
            foreach (var path in paths)
            {
                if (Held.TryGetValue(path, out var owner) && owner == batchId) Held.Remove(path);
            }
        }
    }

    public static bool IsActive(string batchId)
    {
        lock (Gate) return Held.ContainsValue(batchId);
    }
}
