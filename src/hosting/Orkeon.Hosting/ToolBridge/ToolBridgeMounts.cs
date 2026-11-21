using System.Collections.Immutable;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The mounts a call sends (#445, ADR-019), judged before the bridge presents them: each an absolute virtual path in
/// its plain form, other than the root, named once, and clear of the mounts the bridge keeps for itself (its internal
/// ones, which the scratch carries forward: a caller mount over one would take the bridge's own state, configuration
/// or logs, into the scratch and back to the caller).
/// </summary>
internal static class ToolBridgeMounts
{
    /// <summary>The mounts of a call, each path without its final slash, or the reason they are refused.</summary>
    /// <param name="mounts">The mounts the call sends.</param>
    /// <param name="boot">The bridge's own mounts; none when null.</param>
    /// <param name="problem">The reason, in the caller's words; null when the mounts are sound.</param>
    /// <returns>The mounts, normalized; null when refused.</returns>
    public static ImmutableList<BridgeMount>? Normalize(IReadOnlyList<BridgeMount> mounts, FileSystemRegistry? boot, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        var internals = boot?.GetMounts().Where(m => m.Visibility == MountVisibility.Internal).Select(m => m.VirtualPath.TrimEnd('/')).ToList() ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = ImmutableList.CreateBuilder<BridgeMount>();
        foreach (var mount in mounts)
        {
            problem = ProblemOf(mount.Path, out var path);
            if (problem is null && !seen.Add(path))
                problem = $"the call names the mount '{path}' twice.";
            if (problem is null && internals.Any(i => Overlaps(i, path)))
                problem = $"the mount '{path}' of the call overlaps a mount the tool bridge keeps for itself.";
            if (problem is not null)
                return null;
            normalized.Add(mount with { Path = path });
        }

        problem = null;
        return normalized.ToImmutable();
    }

    private static string? ProblemOf(string sent, out string path)
    {
        path = sent.Trim();
        if (!path.StartsWith('/') || path.Contains('\\', StringComparison.Ordinal))
            return $"the mount '{sent}' of the call is not an absolute virtual path (/output).";
        path = path.TrimEnd('/');
        if (path.Length == 0)
            return "the call names the root '/' as a mount: a mount of a call is a folder under it.";
        if (path[1..].Split('/').Any(segment => segment is "" or "." or ".."))
            return $"the mount '{sent}' of the call names an empty, '.' or '..' segment.";
        return null;
    }

    private static bool Overlaps(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal)
        || a.StartsWith(b + "/", StringComparison.Ordinal)
        || b.StartsWith(a + "/", StringComparison.Ordinal);
}
