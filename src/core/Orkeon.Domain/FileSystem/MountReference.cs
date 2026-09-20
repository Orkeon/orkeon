using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.FileSystem;

/// <summary>
/// What a crew declares under <c>mounts:</c> (VFS-90): a virtual root it expects, optionally
/// pinned to one settings entry by its <see cref="MountId"/> — <c>/output</c> or
/// <c>01J…|/output</c>. The root is always present: a run that meets an unknown id (a team
/// carried to another machine, a launcher's own <c>--mount</c>) can still be satisfied by
/// whatever provides that root, and every message can name it. A crew never carries a physical
/// path (ADR-008): the reference selects among the settings' entries, it binds nothing.
/// </summary>
public sealed record MountReference
{
    /// <summary>The settings entry this reference pins, or null when the root alone is named.</summary>
    public MountId? Id { get; }

    /// <summary>The virtual root, rooted at <c>/</c>, without a trailing slash.</summary>
    public string VirtualRoot { get; }

    private MountReference(MountId? id, string virtualRoot)
    {
        Id = id;
        VirtualRoot = virtualRoot;
    }

    /// <summary>The character between the id and the root: the mount string's own.</summary>
    public const char IdSeparator = FileSystemMount.IdSeparator;

    /// <summary>A reference by root alone.</summary>
    /// <param name="virtualRoot">The virtual root, starting with <c>/</c>.</param>
    public static MountReference ForRoot(string virtualRoot) => new(null, NormalizeRoot(virtualRoot));

    /// <summary>A reference pinned to one settings entry.</summary>
    /// <param name="id">The entry's id.</param>
    /// <param name="virtualRoot">The root that entry declares, starting with <c>/</c>.</param>
    public static MountReference ForId(MountId id, string virtualRoot)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new(id, NormalizeRoot(virtualRoot));
    }

    /// <summary>Reads <c>/root</c> or <c>&lt;ulid&gt;|/root</c>; false for anything else.</summary>
    /// <param name="text">The item as the crew wrote it.</param>
    /// <param name="reference">The reference when the text is one.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out MountReference? reference)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        var separator = trimmed.IndexOf(IdSeparator, StringComparison.Ordinal);
        if (separator < 0)
        {
            if (!IsRoot(trimmed))
                return false;

            reference = new MountReference(null, NormalizeRoot(trimmed));
            return true;
        }

        var root = trimmed[(separator + 1)..].Trim();
        if (!MountId.TryParse(trimmed[..separator], out var id) || !IsRoot(root))
            return false;

        reference = new MountReference(id, NormalizeRoot(root));
        return true;
    }

    /// <summary>Reads <c>/root</c> or <c>&lt;ulid&gt;|/root</c>.</summary>
    /// <param name="text">The item as the crew wrote it.</param>
    /// <exception cref="FormatException">The text is neither form.</exception>
    public static MountReference Parse(string text)
    {
        if (TryParse(text, out var reference))
            return reference;

        throw new FormatException(
            $"'{text}' is neither '/root' nor '<ulid>|/root': a virtual root starts with '/', and a mount id is the "
            + $"26-character ULID an Orkeon:FileSystem:Mounts entry carries before its '{IdSeparator}'.");
    }

    /// <summary>True when this reference and <paramref name="other"/> name the same root.</summary>
    /// <param name="other">The other reference.</param>
    public bool SameRootAs(MountReference other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(VirtualRoot, other.VirtualRoot, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override string ToString() => Id is null ? VirtualRoot : $"{Id}{IdSeparator}{VirtualRoot}";

    private static bool IsRoot(string text) => text.Length > 0 && text[0] == '/';

    private static string NormalizeRoot(string virtualRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRoot);
        var trimmed = virtualRoot.Trim();
        if (!IsRoot(trimmed))
            throw new FormatException($"A virtual root starts with '/': '{virtualRoot}'.");

        var normalized = trimmed.TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }
}
