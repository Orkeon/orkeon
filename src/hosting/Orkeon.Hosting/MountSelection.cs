using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting;

/// <summary>
/// One entry of the settings' agent-facing mount array (<c>Orkeon:FileSystem:Mounts</c>), at
/// the configuration index it was declared under. The index is what a withdrawal is written
/// against, so it is the real one — sparse arrays included — never the position in a list.
/// </summary>
/// <param name="Index">The configuration index of the entry.</param>
/// <param name="Spec">The mount string as declared, id prefix included.</param>
public sealed record DeclaredMountEntry(int Index, string Spec);

/// <summary>What selected a settings entry for the run (VFS-90, D-10).</summary>
public enum MountSelector
{
    /// <summary>The <c>--mount-id</c> option.</summary>
    MountIdOption,

    /// <summary>The <c>mounts:</c> block of the crew definition.</summary>
    CrewMounts,
}

/// <summary>A settings entry the run keeps because something selected it.</summary>
/// <param name="Index">Its configuration index.</param>
/// <param name="VirtualRoot">The root it claims, without a trailing slash.</param>
/// <param name="Id">Its id — a selected entry always carries one.</param>
/// <param name="Selector">What selected it.</param>
/// <param name="OverridesCrewChoice">
/// When <c>--mount-id</c> picked this entry while the crew's <c>mounts:</c> named another one
/// of the same root, the crew's choice — the option wins, and the host logs that it did.
/// </param>
public sealed record SelectedMountEntry(
    int Index,
    string VirtualRoot,
    MountId Id,
    MountSelector Selector,
    MountId? OverridesCrewChoice);

/// <summary>A settings entry not mounted for this run: its key is written to null at its index.</summary>
/// <param name="Index">Its configuration index.</param>
/// <param name="VirtualRoot">The root it claims, without a trailing slash.</param>
/// <param name="Id">Its id, when it carries one.</param>
public sealed record WithdrawnMountEntry(int Index, string VirtualRoot, MountId? Id);

/// <summary>
/// The outcome of <see cref="MountSelection.Resolve"/>: which declared entries stay, which are
/// withdrawn, and what to say about it. <see cref="Errors"/> non-empty means the run must not
/// start — the guards print them, the host throws them.
/// </summary>
public sealed record MountSelectionPlan
{
    /// <summary>A plan with nothing declared and nothing to decide.</summary>
    public static MountSelectionPlan Empty { get; } = new();

    /// <summary>The ids the plan was resolved with (the <c>--mount-id</c> values, deduplicated).</summary>
    public IReadOnlyList<MountId> SelectedMountIds { get; init; } = [];

    /// <summary>The crew's <c>mounts:</c> references the plan was resolved with.</summary>
    public IReadOnlyList<MountReference> CrewMountReferences { get; init; } = [];

    /// <summary>The entries something selected, in declaration order.</summary>
    public IReadOnlyList<SelectedMountEntry> Selected { get; init; } = [];

    /// <summary>The entries withdrawn for this run, in declaration order.</summary>
    public IReadOnlyList<WithdrawnMountEntry> Withdrawn { get; init; } = [];

    /// <summary>What the caller should say without refusing (no <c>WARNING: </c> prefix).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Why the run cannot start (no <c>ERROR: </c> prefix); empty when it can.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The configuration indices of <see cref="Withdrawn"/>.</summary>
    public IReadOnlyList<int> WithdrawnIndices() => Withdrawn.Select(entry => entry.Index).ToList();
}

/// <summary>
/// Decides, before the mount registry exists, which of the settings' agent-facing entries a run
/// mounts (VFS-90). Pure: it reads strings and returns a plan; the guards print the plan's
/// messages and the host applies its withdrawals, so a refusal has one text wherever it comes
/// from — the one-line diagnostic of a runner, or the exception of a host built without the
/// guards (the daemon, <c>rag</c>, <c>forge</c>, tests).
/// <para>
/// The rules, in precedence order (D-10): a <c>--mount</c> on a root replaces every declared
/// entry of that root; else a <c>--mount-id</c> selects the entry that carries the id; else
/// the crew's <c>mounts:</c> block does; else a root declared once is mounted as it is, and a
/// root declared several times with nothing selecting one is refused (D-04). An entry is
/// withdrawn — not mounted for this run — when another entry of its root was selected or a
/// <c>--mount</c> took the root. A crew reference never restricts: an entry the crew does not
/// name stays mounted when its root is unique (D-05).
/// </para>
/// </summary>
public static class MountSelection
{
    /// <summary>
    /// The two invariants a declared array must hold on its own, whatever the run asks for
    /// (D-03): an id names one entry, and a root declared more than once is declared by
    /// entries that all carry an id — without one, nothing can ever tell them apart.
    /// </summary>
    /// <param name="declared">The agent-facing entries, as declared.</param>
    /// <param name="settingsPath">The file they come from, named in the messages; null when
    /// they come from the environment alone.</param>
    /// <returns>The refusals, without prefix; empty when the array is sound.</returns>
    public static IReadOnlyList<string> ValidateDeclared(IReadOnlyList<DeclaredMountEntry> declared, string? settingsPath)
    {
        ArgumentNullException.ThrowIfNull(declared);

        var where = Where(settingsPath);
        var entries = Parse(declared);
        var errors = new List<string>();

        foreach (var group in entries.Where(entry => entry.Id is not null).GroupBy(entry => entry.Id!))
        {
            var twice = group.ToList();
            if (twice.Count < 2)
                continue;

            errors.Add(
                $"mount id {group.Key} is declared twice in {where}: {twice[0].Spec} and {twice[1].Spec}. "
                + "An id names one entry.");
        }

        foreach (var (root, group) in GroupByRoot(entries))
        {
            if (group.Count < 2)
                continue;

            var withoutId = group.FirstOrDefault(entry => entry.Id is null);
            if (withoutId is null)
                continue;

            errors.Add(
                $"'{root}' is declared {Times(group.Count)} in {where} ({JoinAnd(group.Select(entry => entry.Spec))}) "
                + $"and '{withoutId.Spec}' has no id. Give every entry an id (<ulid>|<physical>:{root}:<rights>; "
                + "Studio > Allowed folders writes one on save) or keep one.");
        }

        return errors;
    }

    /// <summary>
    /// Resolves what the run mounts. Includes <see cref="ValidateDeclared"/>: a host that only
    /// calls this method refuses an unsound array too.
    /// </summary>
    /// <param name="declared">The agent-facing entries, as declared.</param>
    /// <param name="cliMounts">The run's own mount strings (<c>--mount</c>), which replace by root.</param>
    /// <param name="selectedIds">The <c>--mount-id</c> values, already parsed.</param>
    /// <param name="crewReferences">The crew's <c>mounts:</c> block, already parsed.</param>
    /// <param name="settingsPath">The file the entries come from, named in the messages.</param>
    public static MountSelectionPlan Resolve(
        IReadOnlyList<DeclaredMountEntry> declared,
        IReadOnlyList<string> cliMounts,
        IReadOnlyList<MountId> selectedIds,
        IReadOnlyList<MountReference> crewReferences,
        string? settingsPath)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(cliMounts);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(crewReferences);

        var entries = Parse(declared);
        var resolution = new Resolution
        {
            SettingsLabel = Where(settingsPath),
            Errors = new List<string>(ValidateDeclared(declared, settingsPath)),
            ById = entries
                .Where(entry => entry.Id is not null)
                .GroupBy(entry => entry.Id!)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single()),
            CliRoots = new HashSet<string>(
                cliMounts.Select(TryGetVirtualRoot).OfType<string>(), StringComparer.Ordinal),
        };

        var ids = selectedIds.Distinct().ToList();
        var references = crewReferences.Distinct().ToList();

        ApplyMountIdOptions(resolution, ids);
        ApplyCrewReferences(resolution, references);
        RefuseUnprovidedRoots(resolution, entries);

        var selected = new List<SelectedMountEntry>();
        var withdrawn = new List<WithdrawnMountEntry>();
        foreach (var (root, group) in GroupByRoot(entries))
            DecideRoot(resolution, root, group, selected, withdrawn);

        withdrawn.Sort((a, b) => a.Index.CompareTo(b.Index));
        return new MountSelectionPlan
        {
            SelectedMountIds = ids,
            CrewMountReferences = references,
            Selected = selected,
            Withdrawn = withdrawn,
            Warnings = resolution.Warnings,
            Errors = resolution.Errors,
        };
    }

    /// <summary>What one resolution reads and accumulates, phase after phase.</summary>
    private sealed class Resolution
    {
        /// <summary>The settings file, as the messages name it.</summary>
        public required string SettingsLabel { get; init; }

        public required List<string> Errors { get; init; }

        public List<string> Warnings { get; } = [];

        /// <summary>The entries an id names unambiguously (an id declared twice names nothing).</summary>
        public required Dictionary<MountId, ParsedEntry> ById { get; init; }

        /// <summary>The roots a <c>--mount</c> replaces.</summary>
        public required HashSet<string> CliRoots { get; init; }

        /// <summary>The entries <c>--mount-id</c> selected, by root.</summary>
        public Dictionary<string, List<ParsedEntry>> OptionChoices { get; } = new(StringComparer.Ordinal);

        /// <summary>The entries the crew's <c>mounts:</c> selected, by root.</summary>
        public Dictionary<string, List<ParsedEntry>> CrewChoices { get; } = new(StringComparer.Ordinal);

        /// <summary>The roots the crew requires, in first-reference order.</summary>
        public List<string> RequiredRoots { get; } = [];

        /// <summary>The required roots already refused, which the "nothing provides it" check leaves alone.</summary>
        public HashSet<string> RootsInError { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Each <c>--mount-id</c> selects the entry that carries it, or is refused.</summary>
    private static void ApplyMountIdOptions(Resolution resolution, List<MountId> ids)
    {
        foreach (var id in ids)
        {
            if (resolution.ById.TryGetValue(id, out var entry))
            {
                Choose(resolution.OptionChoices, entry);
                continue;
            }

            resolution.Errors.Add(
                $"no entry of {resolution.SettingsLabel} carries mount id {id} (passed as --mount-id). "
                + "Declare it (Studio > Allowed folders) or drop the option.");
        }
    }

    /// <summary>
    /// Each crew reference names a root the run requires and, when it carries an id, selects
    /// the entry that carries it — unless a <c>--mount</c> on the root satisfies the reference
    /// whatever id it carries (D-09): an exported team's launcher names the folder itself and
    /// knows nothing of this machine's ids.
    /// </summary>
    private static void ApplyCrewReferences(Resolution resolution, List<MountReference> references)
    {
        foreach (var reference in references)
        {
            var root = reference.VirtualRoot;
            if (!resolution.RequiredRoots.Contains(root, StringComparer.Ordinal))
                resolution.RequiredRoots.Add(root);

            if (resolution.CliRoots.Contains(root) || reference.Id is null)
                continue;

            if (!resolution.ById.TryGetValue(reference.Id, out var entry))
            {
                resolution.RootsInError.Add(root);
                resolution.Errors.Add(
                    $"no entry of {resolution.SettingsLabel} carries mount id {reference.Id} (referenced by the crew's mounts: for '{root}'). "
                    + $"Declare it (Studio > Allowed folders) or pass --mount <folder>:{root}:rw.");
                continue;
            }

            if (!string.Equals(entry.Root, root, StringComparison.Ordinal))
            {
                resolution.RootsInError.Add(root);
                resolution.Errors.Add(
                    $"mount id {reference.Id} is '{entry.Root}' in {resolution.SettingsLabel} but the crew lists it for '{root}'. "
                    + "Fix the crew's mounts: or the settings entry.");
                continue;
            }

            Choose(resolution.CrewChoices, entry);
        }
    }

    /// <summary>A root the crew requires that nothing mounts is refused, with the three ways to provide it.</summary>
    private static void RefuseUnprovidedRoots(Resolution resolution, List<ParsedEntry> entries)
    {
        foreach (var root in resolution.RequiredRoots)
        {
            if (resolution.CliRoots.Contains(root)
                || entries.Exists(entry => entry.Root == root)
                || resolution.RootsInError.Contains(root))
            {
                continue;
            }

            resolution.Errors.Add(
                $"the crew requires '{root}' (mounts: in its definition) and nothing provides it: run the team's "
                + $"launcher, declare a folder under {root} in {resolution.SettingsLabel}, or pass --mount <folder>:{root}:rw.");
        }
    }

    /// <summary>
    /// Which entry of one root the run keeps, the others withdrawn: the <c>--mount</c> replaces
    /// them all, else the one <c>--mount-id</c> picked, else the one the crew picked; several
    /// picks are refused, and several entries with nothing picking one too (D-04).
    /// </summary>
    private static void DecideRoot(
        Resolution resolution,
        string root,
        List<ParsedEntry> group,
        List<SelectedMountEntry> selected,
        List<WithdrawnMountEntry> withdrawn)
    {
        var optionPick = resolution.OptionChoices.GetValueOrDefault(root) ?? [];
        var crewPick = resolution.CrewChoices.GetValueOrDefault(root) ?? [];

        if (resolution.CliRoots.Contains(root))
        {
            foreach (var entry in optionPick)
                resolution.Warnings.Add($"--mount-id {entry.Id} selects '{root}', which --mount also replaces; the --mount wins.");

            // The host writes the --mount at the first entry's index; every other entry of
            // the root is withdrawn so the run ends with one mount under that name.
            withdrawn.AddRange(group.Skip(1).Select(entry => new WithdrawnMountEntry(entry.Index, root, entry.Id)));
            return;
        }

        if (optionPick.Count > 1)
        {
            resolution.Errors.Add(
                $"--mount-id selects {Count(optionPick.Count)} of '{root}' ({JoinAnd(optionPick.Select(entry => entry.Id!.ToString()))}); pass one.");
            return;
        }

        if (crewPick.Count > 1)
        {
            resolution.Errors.Add(
                $"the crew's mounts: selects {Count(crewPick.Count)} of '{root}' ({JoinAnd(crewPick.Select(entry => entry.Id!.ToString()))}); keep one.");
            return;
        }

        var (pick, selector, overridden) = PickOf(optionPick, crewPick);
        if (pick is null)
        {
            // D-04: several entries, all with ids (else ValidateDeclared already refused),
            // and no one said which. The message names every candidate so the operator can
            // pick without opening the file.
            if (group.Count > 1 && group.All(entry => entry.Id is not null))
            {
                resolution.Errors.Add(
                    $"'{root}' is declared {Times(group.Count)} in {resolution.SettingsLabel} "
                    + $"({string.Join(", ", group.Select(entry => $"{entry.Id}: {entry.BasePath}"))}) and nothing selects one. "
                    + $"Pass --mount-id <id>, list '<id>|{root}' under mounts: in the crew, "
                    + $"or pass --mount <folder>:{root}:rw to replace them all.");
            }

            return;
        }

        selected.Add(new SelectedMountEntry(pick.Index, root, pick.Id!, selector, overridden));
        withdrawn.AddRange(group
            .Where(entry => entry.Index != pick.Index)
            .Select(entry => new WithdrawnMountEntry(entry.Index, root, entry.Id)));
    }

    /// <summary>
    /// The one pick of a root: the <c>--mount-id</c> one first (noting the crew's choice it
    /// overrides, when they differ), else the crew's, else none. Both lists hold at most one.
    /// </summary>
    private static (ParsedEntry? Pick, MountSelector Selector, MountId? Overridden) PickOf(
        List<ParsedEntry> optionPick, List<ParsedEntry> crewPick)
    {
        if (optionPick.Count == 1)
        {
            var pick = optionPick[0];
            var overridden = crewPick.Count == 1 && crewPick[0].Index != pick.Index ? crewPick[0].Id : null;
            return (pick, MountSelector.MountIdOption, overridden);
        }

        return crewPick.Count == 1
            ? (crewPick[0], MountSelector.CrewMounts, null)
            : (null, MountSelector.CrewMounts, null);
    }

    /// <summary>
    /// The virtual root a mount string claims, without its trailing slash, or null when the
    /// string is not a well-formed mount — the parser reports those at host build time with
    /// its own precise message. Ordinal, like the registry's own duplicate check.
    /// </summary>
    public static string? TryGetVirtualRoot(string mountString)
    {
        try
        {
            return NormalizeRoot(FileSystemMount.Parse(mountString).VirtualPath);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string NormalizeRoot(string virtualPath) =>
        virtualPath.Length > 1 ? virtualPath.TrimEnd('/') : virtualPath;

    private static string Where(string? settingsPath) =>
        string.IsNullOrEmpty(settingsPath) ? "the configuration" : settingsPath;

    private static string Times(int count) => count == 2 ? "twice" : $"{count} times";

    private static string Count(int count) => count == 2 ? "two entries" : $"{count} entries";

    private static string JoinAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }

    private static void Choose(Dictionary<string, List<ParsedEntry>> choices, ParsedEntry entry)
    {
        if (!choices.TryGetValue(entry.Root, out var list))
            choices[entry.Root] = list = [];
        if (!list.Contains(entry))
            list.Add(entry);
    }

    private static List<ParsedEntry> Parse(IReadOnlyList<DeclaredMountEntry> declared)
    {
        var entries = new List<ParsedEntry>();
        foreach (var (index, spec) in declared.OrderBy(entry => entry.Index))
        {
            FileSystemMount mount;
            try
            {
                mount = FileSystemMount.Parse(spec);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // Malformed strings are reported by the mount parser at host build time,
                // with its own precise message. Not this method's.
                continue;
            }

            entries.Add(new ParsedEntry(index, spec, NormalizeRoot(mount.VirtualPath), mount.Id, mount.BasePath));
        }

        return entries;
    }

    /// <summary>The entries by root, roots in first-declared order, entries in index order.</summary>
    private static List<(string Root, List<ParsedEntry> Entries)> GroupByRoot(List<ParsedEntry> entries)
    {
        var groups = new List<(string Root, List<ParsedEntry> Entries)>();
        foreach (var entry in entries)
        {
            var slot = groups.FindIndex(group => string.Equals(group.Root, entry.Root, StringComparison.Ordinal));
            if (slot < 0)
                groups.Add((entry.Root, [entry]));
            else
                groups[slot].Entries.Add(entry);
        }

        return groups;
    }

    private sealed record ParsedEntry(int Index, string Spec, string Root, MountId? Id, string BasePath);
}
