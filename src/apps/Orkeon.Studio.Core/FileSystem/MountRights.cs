using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// The closed list of access-right tokens a mount string may carry. Mirrors
/// <c>FileSystemMount.ParseRights</c> exactly — a value the runtime would reject
/// cannot be produced from this enum.
/// </summary>
public enum MountRights
{
    /// <summary>Read only (<c>ro</c>).</summary>
    ReadOnly,

    /// <summary>Read, write, create and delete (<c>rw</c>).</summary>
    ReadWrite,

    /// <summary>Read, write and create, no delete (<c>rwnd</c>).</summary>
    ReadWriteNoDelete,
}

/// <summary>A rights token paired with the label a UI shows next to it.</summary>
/// <param name="Rights">The rights value.</param>
/// <param name="Token">The token written into the mount string.</param>
/// <param name="Label">Human-readable description for a drop-down.</param>
public sealed record MountRightsChoice(MountRights Rights, string Token, string Label);

/// <summary>
/// Conversions between <see cref="MountRights"/>, the textual tokens of the mount
/// string format, and the domain <see cref="FileAccessRights"/> flags.
/// </summary>
public static class MountRightsTokens
{
    /// <summary>Token of <see cref="MountRights.ReadOnly"/>.</summary>
    public const string ReadOnly = "ro";

    /// <summary>Token of <see cref="MountRights.ReadWrite"/>.</summary>
    public const string ReadWrite = "rw";

    /// <summary>Token of <see cref="MountRights.ReadWriteNoDelete"/>.</summary>
    public const string ReadWriteNoDelete = "rwnd";

    /// <summary>Every choice, in the order a drop-down should list them (English labels).</summary>
    public static IReadOnlyList<MountRightsChoice> Choices { get; } =
        ChoicesFor(EnglishStudioStrings.Instance);

    /// <summary>The choices with their labels resolved through a culture port (STUDIO-11).</summary>
    public static IReadOnlyList<MountRightsChoice> ChoicesFor(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return
        [
            new(MountRights.ReadOnly, ReadOnly, strings[StudioStringKeys.RightsReadOnly]),
            new(MountRights.ReadWrite, ReadWrite, strings[StudioStringKeys.RightsReadWrite]),
            new(MountRights.ReadWriteNoDelete, ReadWriteNoDelete, strings[StudioStringKeys.RightsReadWriteNoDelete]),
        ];
    }

    /// <summary>Every valid token, for validation messages.</summary>
    public static IReadOnlyList<string> Tokens { get; } =
        Choices.Select(c => c.Token).ToList().AsReadOnly();

    /// <summary>Returns the token written into a mount string.</summary>
    public static string ToToken(MountRights rights) => rights switch
    {
        MountRights.ReadOnly => ReadOnly,
        MountRights.ReadWrite => ReadWrite,
        MountRights.ReadWriteNoDelete => ReadWriteNoDelete,
        _ => throw new ArgumentOutOfRangeException(nameof(rights)),
    };

    /// <summary>Parses a token (trimmed, case-insensitive), as the runtime does.</summary>
    public static bool TryParse(string? token, out MountRights rights)
    {
        switch (token?.Trim().ToUpperInvariant())
        {
            case "RO":
                rights = MountRights.ReadOnly;
                return true;
            case "RW":
                rights = MountRights.ReadWrite;
                return true;
            case "RWND":
                rights = MountRights.ReadWriteNoDelete;
                return true;
            default:
                rights = MountRights.ReadOnly;
                return false;
        }
    }

    /// <summary>Maps to the domain flags.</summary>
    public static FileAccessRights ToFileAccessRights(MountRights rights) => rights switch
    {
        MountRights.ReadOnly => FileAccessRights.ReadOnly,
        MountRights.ReadWrite => FileAccessRights.ReadWrite,
        MountRights.ReadWriteNoDelete => FileAccessRights.ReadWriteNoDelete,
        _ => throw new ArgumentOutOfRangeException(nameof(rights)),
    };

    /// <summary>
    /// Maps the domain flags back to a token. Only the three combinations the mount
    /// format can express are representable — anything else has no spelling.
    /// </summary>
    public static bool TryFromFileAccessRights(FileAccessRights rights, out MountRights mountRights)
    {
        if (rights == FileAccessRights.ReadWrite)
        {
            mountRights = MountRights.ReadWrite;
            return true;
        }

        if (rights == FileAccessRights.ReadWriteNoDelete)
        {
            mountRights = MountRights.ReadWriteNoDelete;
            return true;
        }

        if (rights == FileAccessRights.ReadOnly)
        {
            mountRights = MountRights.ReadOnly;
            return true;
        }

        mountRights = MountRights.ReadOnly;
        return false;
    }

    /// <summary>Returns the UI label of a rights value (English).</summary>
    [SuppressMessage("Design", "CA1024", Justification = "Lookup over the Choices table, not a property-backed value.")]
    public static string GetLabel(MountRights rights) =>
        Choices.First(c => c.Rights == rights).Label;

    /// <summary>Returns the UI label of a rights value through a culture port (STUDIO-11).</summary>
    public static string GetLabel(MountRights rights, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return strings[rights switch
        {
            MountRights.ReadOnly => StudioStringKeys.RightsReadOnly,
            MountRights.ReadWrite => StudioStringKeys.RightsReadWrite,
            MountRights.ReadWriteNoDelete => StudioStringKeys.RightsReadWriteNoDelete,
            _ => throw new ArgumentOutOfRangeException(nameof(rights)),
        }];
    }

    /// <summary>
    /// Position of <paramref name="rights"/> in <see cref="Choices"/> — the index a
    /// drop-down or a choice list has to be set to. Unknown values fall back to the first
    /// entry, which is the safest of the three.
    /// </summary>
    public static int IndexOf(MountRights rights)
    {
        for (var i = 0; i < Choices.Count; i++)
        {
            if (Choices[i].Rights == rights)
                return i;
        }

        return 0;
    }

    /// <summary>The rights at <paramref name="index"/> of <see cref="Choices"/>, clamped to the list.</summary>
    public static MountRights At(int index) =>
        Choices[Math.Clamp(index, 0, Choices.Count - 1)].Rights;
}
