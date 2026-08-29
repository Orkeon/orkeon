using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over <c>Orkeon:FileSystem:Mounts</c> — an array of Docker-style mount
/// strings. Entries are exposed both raw (nothing is dropped, even an unparsable one)
/// and as editable <see cref="MountDefinition"/> values.
/// </summary>
public sealed class MountsSection
{
    /// <summary>Configuration path of the mount array.</summary>
    public const string SectionPath = ConfigurationKeys.FileSystemMounts;

    private readonly AppSettingsDocument _document;

    internal MountsSection(AppSettingsDocument document) => _document = document;

    /// <summary>True when the array key is present (it may still be empty).</summary>
    public bool Exists => _document.ContainsPath(SectionPath);

    /// <summary>The entries exactly as stored.</summary>
    public IReadOnlyList<string> RawEntries => _document.GetStringArray(SectionPath);

    /// <summary>
    /// The entries that parse, in order. Unparsable entries are skipped here and
    /// reported by <see cref="MountValidator"/>; use <see cref="RawEntries"/> to edit
    /// them as text.
    /// </summary>
    public IReadOnlyList<MountDefinition> Definitions
    {
        get
        {
            var definitions = new List<MountDefinition>();
            foreach (var entry in RawEntries)
            {
                if (MountDefinition.TryParse(entry, out var definition, out _))
                    definitions.Add(definition);
            }

            return definitions;
        }
    }

    /// <summary>Replaces the array with the serialized form of the given definitions.</summary>
    public void Set(IEnumerable<MountDefinition> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        _document.SetStringArray(SectionPath, mounts.Select(m => m.ToMountString()));
    }

    /// <summary>Replaces the array with raw entries (the raw-edit path).</summary>
    public void SetRaw(IEnumerable<string> mountStrings)
    {
        ArgumentNullException.ThrowIfNull(mountStrings);
        _document.SetStringArray(SectionPath, mountStrings);
    }

    /// <summary>Appends one mount, keeping the existing entries untouched.</summary>
    public void Add(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        var entries = new List<string>(RawEntries) { mount.ToMountString() };
        _document.SetStringArray(SectionPath, entries);
    }

    /// <summary>Removes the entry at <paramref name="index"/>.</summary>
    public void RemoveAt(int index)
    {
        var entries = new List<string>(RawEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, entries.Count);

        entries.RemoveAt(index);
        _document.SetStringArray(SectionPath, entries);
    }

    /// <summary>Removes the whole array key.</summary>
    public void Remove() => _document.Remove(SectionPath);
}
