namespace Orkeon.Analysis.Core.Editing.Intent;

internal static class UsingOrderDetector
{
    /// <summary>Detects System-first versus alphabetical order from the names of an existing block.</summary>
    public static IComparer<string> Detect(IReadOnlyList<string> existing)
    {
        var alphabetical = Comparer<string>.Create(string.CompareOrdinal);
        if (IsSorted(existing, alphabetical)) return alphabetical;
        var systemFirst = Comparer<string>.Create(CompareSystemFirst);
        return IsSorted(existing, systemFirst) ? systemFirst : alphabetical;
    }

    private static bool IsSorted(IReadOnlyList<string> names, Comparer<string> comparer)
    {
        for (var i = 1; i < names.Count; i++)
            if (comparer.Compare(names[i - 1], names[i]) > 0) return false;
        return true;
    }

    private static int CompareSystemFirst(string left, string right)
    {
        var byGroup = IsSystem(right).CompareTo(IsSystem(left));
        return byGroup != 0 ? byGroup : string.CompareOrdinal(left, right);
    }

    private static bool IsSystem(string name)
        => name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
}
