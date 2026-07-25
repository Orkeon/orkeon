using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Describes a knowledge source to load (file, directory, URL, inline text…).
/// Consumed by <see cref="Interfaces.IDocumentLoader"/> implementations.
/// </summary>
public sealed record SourceDescriptor
{
    /// <summary>Source locator: a virtual file path, directory, URL, or inline reference.</summary>
    public required string Location { get; init; }

    /// <summary>
    /// Stable identity of the source across re-ingestions. When <c>null</c>,
    /// implementations use <see cref="Location"/> as the identity.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>Optional loader hint (e.g. <c>file</c>, <c>directory</c>, <c>url</c>, <c>text</c>).</summary>
    public string? Kind { get; init; }

    /// <summary>Loader-specific options (e.g. glob patterns, encoding).</summary>
    public ImmutableDictionary<string, string> Options { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
