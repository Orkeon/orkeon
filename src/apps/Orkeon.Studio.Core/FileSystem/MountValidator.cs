using System.Globalization;
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
    public IReadOnlyList<ValidationMessage> Validate(
        IReadOnlyList<string> mountStrings,
        bool requireAtLeastOne = true)
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

        messages.AddRange(Validate(parsed, requireAtLeastOne));
        return messages;
    }

    /// <summary>Validates already-structured mount definitions (the editor's own list).</summary>
    /// <param name="mounts">Definitions to validate.</param>
    /// <param name="requireAtLeastOne">See the raw-string overload.</param>
    public IReadOnlyList<ValidationMessage> Validate(
        IReadOnlyList<MountDefinition> mounts,
        bool requireAtLeastOne = true)
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
                messages.Add(ValidationMessage.Error(
                    ValidationCodes.MountFormat,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Virtual path '{mount.VirtualPath}' must start with '/' or be a Windows drive path."),
                    serialized));
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

            if (!_directories.Exists(mount.PhysicalPath))
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
    private const string MountsSectionPath = "Orkeon:FileSystem:Mounts";

    private static IEnumerable<ValidationMessage> FindVirtualPathCollisions(IReadOnlyList<MountDefinition> mounts)
    {
        return mounts
            .Where(m => !string.IsNullOrWhiteSpace(m.VirtualPath))
            .GroupBy(m => NormalizeVirtualPath(m.VirtualPath), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => ValidationMessage.Error(
                ValidationCodes.MountVirtualCollision,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Virtual path '{group.Key}' is claimed by {group.Count()} mounts; each virtual path must be unique."),
                group.Key));
    }

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
