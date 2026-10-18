using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>The rights an ACE of the plan grants or refuses the session's AppContainer SID.</summary>
internal enum AclRights
{
    /// <summary>Read, list and execute.</summary>
    ReadAndExecute,

    /// <summary>Read, execute, write, create and delete (not change the permissions).</summary>
    Modify,

    /// <summary>Write, create, delete and change permissions: what a read-only path refuses.</summary>
    Write,

    /// <summary>Everything: what a hidden path refuses.</summary>
    All,
}

/// <summary>One ACE for the session's AppContainer SID on one host path.</summary>
/// <param name="Path">The physical path.</param>
/// <param name="Rights">What the ACE covers.</param>
/// <param name="Deny">A deny ACE (it wins over the allow ACEs a parent passes down) rather than an allow one.</param>
internal sealed record AclEntry(string Path, AclRights Rights, bool Deny);

/// <summary>
/// The ACEs that give an AppContainer the view of a shell session: what <see cref="ConfinedPath"/>
/// says of each mount, written as Windows access control. Pure: it reads no ACL and touches no path,
/// so the plan is checked entry by entry on any machine.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A read-only path gets an allow <see cref="AclRights.ReadAndExecute"/>; under a read-write
/// path it also gets a deny <see cref="AclRights.Write"/>, since the parent's inherited allow would
/// otherwise still let the container write there (allow ACEs add up, an explicit deny wins).</item>
/// <item>A read-write path gets an allow <see cref="AclRights.Modify"/>.</item>
/// <item>A hidden directory or file gets a deny <see cref="AclRights.All"/>.</item>
/// <item>An extra read-only host directory gets an allow <see cref="AclRights.ReadAndExecute"/>.</item>
/// </list>
/// On a directory every ACE is inheritable, so it covers the whole tree below it.
/// </remarks>
internal static class AclPlan
{
    /// <summary>The ACEs of a view, parents first.</summary>
    /// <param name="paths">The mounts and their sub paths; <see cref="ConfinedPath.Source"/> is the host path.</param>
    /// <param name="readOnlyPaths">Extra host directories the container may read and execute.</param>
    public static IReadOnlyList<AclEntry> Build(IEnumerable<ConfinedPath> paths, IEnumerable<string> readOnlyPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(readOnlyPaths);

        var ordered = paths
            .Select(p => p with { Source = Normalize(p.Source) })
            .Where(p => p.Source.Length > 0)
            .OrderBy(p => Depth(p.Source))
            .ThenBy(p => p.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var writable = ordered.Where(p => p.Access == ConfinedAccess.ReadWrite).Select(p => p.Source).ToList();

        var entries = new List<AclEntry>();
        foreach (var extra in readOnlyPaths.Select(Normalize).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            entries.Add(new AclEntry(extra, AclRights.ReadAndExecute, Deny: false));

        foreach (var path in ordered)
        {
            switch (path.Access)
            {
                case ConfinedAccess.ReadOnly:
                    entries.Add(new AclEntry(path.Source, AclRights.ReadAndExecute, Deny: false));
                    if (writable.Any(w => IsStrictlyUnder(path.Source, w)))
                        entries.Add(new AclEntry(path.Source, AclRights.Write, Deny: true));
                    break;
                case ConfinedAccess.ReadWrite:
                    entries.Add(new AclEntry(path.Source, AclRights.Modify, Deny: false));
                    break;
                case ConfinedAccess.HiddenDirectory:
                case ConfinedAccess.HiddenFile:
                    entries.Add(new AclEntry(path.Source, AclRights.All, Deny: true));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(paths), path.Access, "Unknown confined access.");
            }
        }

        return entries;
    }

    /// <summary>The distinct paths of a plan, the ones a session records in its ledger.</summary>
    public static IReadOnlyList<string> PathsOf(IEnumerable<AclEntry> entries)
        => [.. entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether <paramref name="path"/> lies below <paramref name="parent"/> (case-insensitive, as NTFS).</summary>
    internal static bool IsStrictlyUnder(string path, string parent)
        => path.Length > parent.Length
           && path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
           && (IsSeparator(path[parent.Length]) || IsSeparator(parent[^1]));

    private static string Normalize(string path)
    {
        var trimmed = path.Trim();
        // Keep a drive root ("C:\") whole; any other trailing separator goes.
        while (trimmed.Length > 3 && IsSeparator(trimmed[^1]))
            trimmed = trimmed[..^1];
        return trimmed;
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';

    private static int Depth(string path) => path.Count(IsSeparator);
}
