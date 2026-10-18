using System.Text;
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
/// <param name="Deny">
/// A deny ACE rather than an allow one. Canonical order puts it ahead of the allow ACEs a parent passes
/// down, but the access check of an AppContainer skips it: it is a second layer, and the refusal is the
/// integrity label of <see cref="AclPlan.Labels"/>.
/// </param>
internal sealed record AclEntry(string Path, AclRights Rights, bool Deny);

/// <summary>What the integrity label of a path refuses a process below Medium integrity (the AppContainer).</summary>
internal enum LabelPolicy
{
    /// <summary>Writing, creating, deleting, changing the permissions: a read-only sub path of a read-write mount.</summary>
    NoWriteUp,

    /// <summary>Reading, writing and executing: a hidden path below a granted one.</summary>
    NoAccessUp,
}

/// <summary>One explicit Medium integrity label on one host path.</summary>
/// <param name="Path">The physical path.</param>
/// <param name="Policy">What the label refuses the container.</param>
internal sealed record LabelEntry(string Path, LabelPolicy Policy);

/// <summary>
/// The ACEs that give an AppContainer the view of a shell session: what <see cref="ConfinedPath"/>
/// says of each mount, written as Windows access control. Pure: it reads no ACL and touches no path,
/// so the plan is checked entry by entry on any machine.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A read-only path gets an allow <see cref="AclRights.ReadAndExecute"/>; under a read-write
/// path it also gets a deny <see cref="AclRights.Write"/> and a no-write-up label (<see cref="Labels"/>),
/// since the parent's inherited allow would otherwise still let the container write there.</item>
/// <item>A read-write path gets an allow <see cref="AclRights.Modify"/>.</item>
/// <item>A hidden directory or file gets a deny <see cref="AclRights.All"/> and, below a granted path,
/// a no-access-up label.</item>
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

    /// <summary>
    /// The integrity labels of a view: what an ACE cannot refuse an AppContainer below a path its
    /// mount grants (the container's access check skips an ACE that denies its package SID, see
    /// <see cref="IntegrityLabels"/>). A hidden path below a granted one gets <see cref="LabelPolicy.NoAccessUp"/>;
    /// a read-only path below a read-write one gets <see cref="LabelPolicy.NoWriteUp"/>. A path below
    /// nothing granted is out of reach already and gets no label.
    /// </summary>
    /// <param name="paths">The mounts and their sub paths; <see cref="ConfinedPath.Source"/> is the host path.</param>
    /// <param name="readOnlyPaths">Extra host directories the container may read and execute.</param>
    public static IReadOnlyList<LabelEntry> Labels(IEnumerable<ConfinedPath> paths, IEnumerable<string> readOnlyPaths)
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
        var granted = ordered
            .Where(p => p.Access is ConfinedAccess.ReadOnly or ConfinedAccess.ReadWrite)
            .Select(p => p.Source)
            .Concat(readOnlyPaths.Select(Normalize).Where(p => p.Length > 0))
            .ToList();

        var labels = new List<LabelEntry>();
        foreach (var path in ordered)
        {
            if (path.Access is ConfinedAccess.HiddenDirectory or ConfinedAccess.HiddenFile
                && granted.Any(g => IsStrictlyUnder(path.Source, g)))
                labels.Add(new LabelEntry(path.Source, LabelPolicy.NoAccessUp));
            else if (path.Access == ConfinedAccess.ReadOnly && writable.Any(w => IsStrictlyUnder(path.Source, w)))
                labels.Add(new LabelEntry(path.Source, LabelPolicy.NoWriteUp));
        }

        return labels;
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

/// <summary>The SDDL of the integrity labels a session writes and reads back. Pure.</summary>
internal static class LabelSddl
{
    /// <summary>The SDDL of the label a policy writes, on a directory (inherited by its tree) or a file.</summary>
    public static string Of(LabelPolicy policy, bool directory)
    {
        var flags = directory ? "OICI" : "";
        var rights = policy switch
        {
            LabelPolicy.NoWriteUp => "NW",
            LabelPolicy.NoAccessUp => "NRNWNX",
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown label policy."),
        };
        return $"S:(ML;{flags};{rights};;;ME)";
    }

    /// <summary>
    /// The explicit mandatory label ACEs of a label SDDL (<c>S:(ML;...)</c>), as an SDDL of their
    /// own; empty when the label is only inherited or there is none. Pure.
    /// </summary>
    public static string ExplicitOf(string sddl)
    {
        ArgumentNullException.ThrowIfNull(sddl);
        var start = sddl.IndexOf("S:", StringComparison.Ordinal);
        if (start < 0)
            return "";

        var explicitAces = new StringBuilder();
        var index = start + 2;
        while ((index = sddl.IndexOf('(', index)) >= 0)
        {
            var end = sddl.IndexOf(')', index);
            if (end < 0)
                break;
            var ace = sddl[(index + 1)..end];
            var fields = ace.Split(';');
            if (fields.Length >= 2 && fields[0] == "ML" && !HasFlag(fields[1], "ID"))
                explicitAces.Append('(').Append(ace).Append(')');
            index = end + 1;
        }

        return explicitAces.Length == 0 ? "" : "S:" + explicitAces;
    }

    /// <summary>Whether a SDDL flag string (two letters per flag) holds <paramref name="flag"/>.</summary>
    private static bool HasFlag(string flags, string flag)
    {
        for (var i = 0; i + 1 < flags.Length; i += 2)
        {
            if (string.CompareOrdinal(flags, i, flag, 0, 2) == 0)
                return true;
        }

        return false;
    }
}
