using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.FileSystem;

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

    /// <summary>Every choice, in the order a drop-down should list them.</summary>
    public static IReadOnlyList<MountRightsChoice> Choices { get; } =
    [
        new(MountRights.ReadOnly, ReadOnly, "Read only"),
        new(MountRights.ReadWrite, ReadWrite, "Read / write (create and delete allowed)"),
        new(MountRights.ReadWriteNoDelete, ReadWriteNoDelete, "Read / write without delete"),
    ];

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

    /// <summary>Returns the UI label of a rights value.</summary>
    [SuppressMessage("Design", "CA1024", Justification = "Lookup over the Choices table, not a property-backed value.")]
    public static string GetLabel(MountRights rights) =>
        Choices.First(c => c.Rights == rights).Label;
}
