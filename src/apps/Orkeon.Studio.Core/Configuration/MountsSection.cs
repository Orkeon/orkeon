using Orkeon.Constants.Configuration;
using Orkeon.Domain.Common;
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

    /// <summary>
    /// Replaces the array with the serialized form of the given definitions. An entry without
    /// an id gets one on the way (VFS-90): the settings are the one place a mount's identity is
    /// born, and every save is an occasion — a file written before ids existed is complete
    /// after its first save, with nothing else changed.
    /// </summary>
    public void Set(IEnumerable<MountDefinition> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        _document.SetStringArray(SectionPath, mounts.Select(m => WithId(m).ToMountString()));
    }

    /// <summary>The entry carrying <paramref name="id"/>, or null.</summary>
    public MountDefinition? Find(MountId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Definitions.FirstOrDefault(m => id.Equals(m.Id));
    }

    /// <summary>The raw index of the entry carrying <paramref name="id"/>, or -1.</summary>
    public int IndexOf(MountId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var entries = RawEntries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (MountDefinition.TryParse(entries[i], out var mount, out _) && id.Equals(mount.Id))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Gives an id to every parsable entry that has none, leaving unparsable entries as they
    /// are. Returns whether anything changed — the editors call it at load so the ids exist
    /// before the first save, and the TUIs on save.
    /// </summary>
    public bool WithIdsAssigned()
    {
        var entries = new List<string>(RawEntries);
        var changed = false;
        for (var i = 0; i < entries.Count; i++)
        {
            if (MountDefinition.TryParse(entries[i], out var mount, out _) && mount.Id is null)
            {
                entries[i] = mount.WithFreshId().ToMountString();
                changed = true;
            }
        }

        if (changed)
            _document.SetStringArray(SectionPath, entries);
        return changed;
    }

    /// <summary>
    /// The declared entry a team may bind (VFS-90, D-01): an existing entry that declares the
    /// same thing — folder, root, rights — is reused (and given an id if it had none); otherwise
    /// <paramref name="mount"/> is added under an id of its own, even when another entry
    /// already claims its root — two entries may share a root, their ids tell them apart. A
    /// mount that arrives with an id (a team imported from another machine, D-06) keeps it
    /// unless this file already spends that id on something else.
    /// </summary>
    /// <returns>The entry as it now stands in the file, id included.</returns>
    public MountDefinition EnsureDeclared(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        var entries = new List<string>(RawEntries);
        for (var i = 0; i < entries.Count; i++)
        {
            if (!MountDefinition.TryParse(entries[i], out var declared, out _) || !declared.SameDeclaration(mount))
                continue;

            if (declared.Id is not null)
                return declared;

            var identified = mount.Id is not null && Find(mount.Id) is null ? declared with { Id = mount.Id } : declared.WithFreshId();
            entries[i] = identified.ToMountString();
            _document.SetStringArray(SectionPath, entries);
            return identified;
        }

        var added = mount.Id is null || Find(mount.Id) is not null ? mount.WithFreshId() : mount;
        entries.Add(added.ToMountString());
        _document.SetStringArray(SectionPath, entries);
        return added;
    }

    private static MountDefinition WithId(MountDefinition mount) => mount.Id is null ? mount.WithFreshId() : mount;

    /// <summary>Replaces the array with raw entries (the raw-edit path).</summary>
    public void SetRaw(IEnumerable<string> mountStrings)
    {
        ArgumentNullException.ThrowIfNull(mountStrings);
        _document.SetStringArray(SectionPath, mountStrings);
    }

    /// <summary>Appends one mount, keeping the existing entries untouched; an entry without an id gets one.</summary>
    public void Add(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        var entries = new List<string>(RawEntries) { WithId(mount).ToMountString() };
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
