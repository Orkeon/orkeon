using Orkeon.Constants.Configuration;
using System.Globalization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// Validates a mount list against what the runtime will check at boot: every entry
/// parses, every physical path exists, and no two mounts claim the same virtual path.
/// </summary>
public sealed class MountValidator
{
    private readonly IDirectoryProbe _directories;

    /// <summary>Creates a validator over the given directory access (defaults to the real disk).</summary>
    public MountValidator(IDirectoryProbe? directories = null) =>
        _directories = directories ?? PhysicalDirectoryProbe.Instance;

    /// <summary>
    /// Validates raw mount strings — the shape stored in
    /// <c>Orkeon:FileSystem:Mounts</c>.
    /// </summary>
    /// <param name="mountStrings">Entries to validate.</param>
    /// <param name="requireAtLeastOne">
    /// True in the mount editor, where saving an empty list makes the runtime refuse to
    /// boot; false when the launcher merely adds mounts on top of an existing file.
    /// </param>
    /// <param name="teamDirectory">See the structured overload.</param>
    public IReadOnlyList<ValidationMessage> Validate(
        IReadOnlyList<string> mountStrings,
        bool requireAtLeastOne = true,
        string? teamDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(mountStrings);

        var messages = new List<ValidationMessage>();
        var parsed = new List<MountDefinition>(mountStrings.Count);

        for (var i = 0; i < mountStrings.Count; i++)
        {
            if (MountDefinition.TryParse(mountStrings[i], out var definition, out var error))
                parsed.Add(definition);
            else
                messages.Add(FormatError(mountStrings[i], error, i));
        }

        messages.AddRange(Validate(parsed, requireAtLeastOne, teamDirectory));
        return messages;
    }

    /// <summary>Validates already-structured mount definitions (the editor's own list).</summary>
    /// <param name="mounts">Definitions to validate.</param>
    /// <param name="requireAtLeastOne">See the raw-string overload.</param>
    /// <param name="teamDirectory">
    /// The team folder a team-relative entry (<c>./output:/output:rw</c>) resolves under.
    /// With one, the folder's existence is checked there; without one — the team is not
    /// adopted yet, its folders are born at adoption — the existence check is skipped for
    /// such entries. Every other entry is checked exactly as before, whatever is passed.
    /// </param>
    public IReadOnlyList<ValidationMessage> Validate(
        IReadOnlyList<MountDefinition> mounts,
        bool requireAtLeastOne = true,
        string? teamDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        var messages = new List<ValidationMessage>();

        if (requireAtLeastOne && mounts.Count == 0)
        {
            messages.Add(ValidationMessage.Error(
                ValidationCodes.MountsEmpty,
                "At least one mount must be declared: the runtime refuses to start with an " +
                "empty 'Orkeon:FileSystem:Mounts'.",
                MountsSectionPath));
        }

        foreach (var mount in mounts)
        {
            var serialized = mount.ToMountString();

            if (!MountDefinition.IsValidVirtualPath(mount.VirtualPath))
            {
                var reason = MountDefinition.IsReservedVirtualPath(mount.VirtualPath)
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Virtual path '{mount.VirtualPath}' is reserved by the runner — choose another name.")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Virtual path '{mount.VirtualPath}' must start with '/'.");
                messages.Add(ValidationMessage.Error(ValidationCodes.MountFormat, reason, serialized));
                continue;
            }

            // The authoritative check: what the editor produces must survive the parser
            // the runtime uses (a physical path with a stray ':' fails here, for one).
            try
            {
                _ = mount.ToDomainMount();
            }
            catch (FormatException ex)
            {
                messages.Add(FormatError(serialized, ex.Message, index: null));
                continue;
            }
            catch (ArgumentException ex)
            {
                messages.Add(FormatError(serialized, ex.Message, index: null));
                continue;
            }

            if (!Exists(mount, serialized, teamDirectory))
            {
                messages.Add(ValidationMessage.Error(
                    ValidationCodes.MountPathMissing,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Physical path '{mount.PhysicalPath}' does not exist — create it or pick another folder."),
                    serialized));
            }
        }

        messages.AddRange(FindVirtualPathCollisions(mounts));
        return messages;
    }

    /// <summary>Configuration path of the mount list, used as the message path.</summary>
    private const string MountsSectionPath = ConfigurationKeys.FileSystemMounts;

    /// <summary>
    /// Whether the mount's folder exists, as the runtime will find it. A team-relative entry
    /// is looked for under the team folder when one is known — that is where the catalog
    /// resolves it before a launch — and is taken on trust when none is: that folder does not
    /// exist yet, it is created at adoption, and a missing-path error on a wizard row would
    /// name a mistake nobody made.
    /// </summary>
    private bool Exists(MountDefinition mount, string serialized, string? teamDirectory)
    {
        if (!TeamMountPaths.TryGetRelativeFolder(serialized, out var folder))
            return _directories.Exists(mount.PhysicalPath);

        return teamDirectory is not { Length: > 0 }
            || _directories.Exists(Path.Combine(teamDirectory, folder));
    }

    private static IEnumerable<ValidationMessage> FindVirtualPathCollisions(IReadOnlyList<MountDefinition> mounts)
    {
        var named = mounts.Where(m => !string.IsNullOrWhiteSpace(m.VirtualPath)).ToList();

        // An id names one entry (VFS-90): the engine refuses the file otherwise.
        foreach (var group in named.Where(m => m.Id is not null).GroupBy(m => m.Id!))
        {
            var count = group.Count();
            if (count < 2)
                continue;

            yield return ValidationMessage.Error(
                ValidationCodes.MountIdDuplicate,
                string.Create(CultureInfo.InvariantCulture, $"Mount id {group.Key} is carried by {count} entries; an id names one entry."),
                group.Key.ToString());
        }

        // What the runtime refuses at boot: one root, Ordinal with the trailing slash dropped
        // as the engine drops it, claimed by entries it cannot tell apart. Since VFS-90 two
        // entries MAY share a root — each with an id, a team or --mount-id picks one per run —
        // so that shape is information, not a refusal; an entry without an id among them is
        // what the engine refuses, and so does Studio.
        foreach (var group in named.GroupBy(m => MountDefinition.NormalizeRoot(m.VirtualPath), StringComparer.Ordinal))
        {
            var entries = group.ToList();
            if (entries.Count < 2)
                continue;

            var withoutId = entries.FirstOrDefault(m => m.Id is null);
            if (withoutId is null)
            {
                yield return ValidationMessage.Information(
                    ValidationCodes.MountSharedRoot,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Virtual path '{group.Key}' is declared {entries.Count} times; a team or --mount-id picks one per run."),
                    group.Key);
            }
            else
            {
                yield return ValidationMessage.Error(
                    ValidationCodes.MountVirtualCollision,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Virtual path '{group.Key}' is claimed by {entries.Count} mounts and '{withoutId.ToMountString()}' has no id; give every entry an id (saving assigns one) or rename one."),
                    group.Key);
            }

            foreach (var folder in entries
                .GroupBy(m => MountDefinition.NormalizeFolder(m.PhysicalPath), FolderComparer)
                .Where(f => f.Count() > 1))
            {
                yield return ValidationMessage.Warning(
                    ValidationCodes.MountSharedRoot,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Folder '{folder.Key}' is declared {folder.Count()} times under '{group.Key}'; one entry is enough."),
                    group.Key);
            }
        }

        // Near-collisions ('/Data' vs '/data') boot fine but read as one folder to a person —
        // said as a warning, never as a refusal.
        var near = named
            .GroupBy(m => NormalizeVirtualPath(m.VirtualPath), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1
                && group.Select(m => MountDefinition.NormalizeRoot(m.VirtualPath)).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => (Key: group.Key, Count: group.Count()));

        foreach (var (key, count) in near)
        {
            yield return ValidationMessage.Warning(
                ValidationCodes.MountVirtualCollision,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Virtual path '{key}' is claimed by {count} mounts; each virtual path must be unique."),
                key);
        }
    }

    private static readonly StringComparer FolderComparer =
        Orkeon.Domain.FileSystem.PhysicalPathContainment.Comparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static string NormalizeVirtualPath(string virtualPath)
    {
        var normalized = virtualPath.Trim().Replace('\\', '/').TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static ValidationMessage FormatError(string entry, string? error, int? index)
    {
        var prefix = index is null
            ? "Invalid mount definition"
            : string.Create(CultureInfo.InvariantCulture, $"Invalid mount definition at index {index.Value}");

        return ValidationMessage.Error(
            ValidationCodes.MountFormat,
            string.Create(CultureInfo.InvariantCulture, $"{prefix}: {error}"),
            entry);
    }
}
