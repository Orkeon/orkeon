using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.Analysis;

internal static class IndexPaths
{
    public static string RootPrefix(IRaggableStore store)
        => (store.GetIndexedRoots().Select(r => r.VirtualRoot).FirstOrDefault() ?? "/").TrimEnd('/') + "/";

    public static string Relative(string path, string rootPrefix)
        => path.StartsWith(rootPrefix, StringComparison.Ordinal) ? path[rootPrefix.Length..] : path;
}
