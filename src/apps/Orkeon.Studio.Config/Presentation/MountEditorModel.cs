using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>One row of the mount list, as the screen displays it.</summary>
/// <param name="Index">Position in <c>Orkeon:FileSystem:Mounts</c>.</param>
/// <param name="Raw">The entry exactly as stored.</param>
/// <param name="Definition">The parsed form, or null when the entry does not parse.</param>
/// <param name="Error">Why the entry does not parse.</param>
internal sealed record MountRow(int Index, string Raw, MountDefinition? Definition, string? Error)
{
    /// <summary>True when the entry can be edited in the mount form.</summary>
    public bool IsParsable => Definition is not null;

    /// <summary>The single line the list shows.</summary>
    public string Display
    {
        get
        {
            if (Definition is null)
                return string.Create(CultureInfo.InvariantCulture, $"! {Raw}  ({Error})");

            var overrides = Definition.Overrides.Count == 0
                ? ""
                : string.Create(CultureInfo.InvariantCulture, $"  (+{Definition.Overrides.Count} sub-path override(s))");

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Definition.VirtualPath}  [{MountRightsTokens.ToToken(Definition.Rights)}]  <- {Definition.PhysicalPath}{overrides}");
        }
    }
}

/// <summary>
/// The mount list of an <c>appsettings.json</c>, edited row by row. Entries are held as
/// the raw strings they are stored as, so an entry Studio cannot parse is listed, flagged
/// and saved back untouched instead of being silently dropped.
/// </summary>
internal sealed class MountEditorModel
{
    private readonly List<string> _entries = [];
    private readonly MountValidator _validator;

    /// <summary>Creates the editor over a directory probe (defaults to the real disk).</summary>
    public MountEditorModel(IDirectoryProbe? directories = null) =>
        _validator = new MountValidator(directories);

    /// <summary>Title of the section in the navigation list.</summary>
    public const string SectionTitle = "Mounts (VFS)";

    /// <summary>True when the document already carried the mount array.</summary>
    public bool SectionExisted { get; private set; }

    /// <summary>The entries exactly as they will be written.</summary>
    public IReadOnlyList<string> RawEntries => _entries;

    /// <summary>The rows a list view renders.</summary>
    public IReadOnlyList<MountRow> Rows
    {
        get
        {
            var rows = new List<MountRow>(_entries.Count);
            for (var i = 0; i < _entries.Count; i++)
            {
                rows.Add(MountDefinition.TryParse(_entries[i], out var definition, out var error)
                    ? new MountRow(i, _entries[i], definition, null)
                    : new MountRow(i, _entries[i], null, error));
            }

            return rows;
        }
    }

    /// <summary>Reads the mount array out of a document.</summary>
    public void LoadFrom(AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        SectionExisted = document.Mounts.Exists;
        _entries.Clear();
        _entries.AddRange(document.Mounts.RawEntries);
    }

    /// <summary>
    /// Writes the mount array back. An empty list is written as an empty array only when
    /// the document already had one — an absent section stays absent rather than gaining
    /// a key the user never asked for.
    /// </summary>
    public void ApplyTo(AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_entries.Count == 0 && !SectionExisted)
            return;

        document.Mounts.SetRaw(_entries);
        SectionExisted = true;
    }

    /// <summary>Appends a mount.</summary>
    public void Add(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        _entries.Add(mount.ToMountString());
    }

    /// <summary>Replaces the mount at <paramref name="index"/>.</summary>
    public void Replace(int index, MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _entries.Count);

        _entries[index] = mount.ToMountString();
    }

    /// <summary>Removes the mount at <paramref name="index"/>.</summary>
    public void RemoveAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _entries.Count);

        _entries.RemoveAt(index);
    }

    /// <summary>
    /// Validates the whole list the way the runtime will at boot: every entry parses,
    /// every physical path exists, no two mounts claim the same virtual path, and at
    /// least one mount is declared.
    /// </summary>
    public IReadOnlyList<ValidationMessage> Validate() =>
        _validator.Validate(_entries, requireAtLeastOne: true);
}
