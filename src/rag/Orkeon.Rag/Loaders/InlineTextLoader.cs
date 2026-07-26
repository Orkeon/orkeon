using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Loads inline text sources: the content travels inside the
/// <see cref="SourceDescriptor.Options"/> bag instead of being read from disk
/// or the network. Used by callers that already hold (and possibly preprocess)
/// their corpus in memory — notably the ephemeral-collection search façades
/// (RAG-03/C5), which strip frontmatter or extract PDF pages before ingestion.
/// </summary>
/// <remarks>
/// A source is claimed only when <see cref="SourceDescriptor.Kind"/> equals
/// <see cref="TextKind"/> and the <see cref="ContentOptionKey"/> option is
/// present. Options prefixed with <see cref="MetadataOptionPrefix"/> become
/// document metadata (prefix stripped) and are therefore inherited by every
/// chunk — provenance such as page numbers survives the store round-trip.
/// </remarks>
public sealed class InlineTextLoader : IDocumentLoader
{
    /// <summary><see cref="SourceDescriptor.Kind"/> hint claimed by this loader.</summary>
    public const string TextKind = "text";

    /// <summary><see cref="SourceDescriptor.Options"/> key carrying the document content.</summary>
    public const string ContentOptionKey = "content";

    /// <summary>Prefix of <see cref="SourceDescriptor.Options"/> keys promoted to document metadata.</summary>
    public const string MetadataOptionPrefix = "meta.";

    /// <inheritdoc />
    public bool CanLoad(SourceDescriptor source)
    {
        return source is not null
            && string.Equals(source.Kind, TextKind, StringComparison.OrdinalIgnoreCase)
            && source.Options.ContainsKey(ContentOptionKey);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagDocument> LoadAsync(
        SourceDescriptor source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Location);

        if (!source.Options.TryGetValue(ContentOptionKey, out var content))
        {
            throw new ArgumentException(
                $"Inline text source '{source.Location}' carries no '{ContentOptionKey}' option.",
                nameof(source));
        }

        var metadata = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (key, value) in source.Options)
        {
            if (key.StartsWith(MetadataOptionPrefix, StringComparison.Ordinal)
                && key.Length > MetadataOptionPrefix.Length)
            {
                metadata[key[MetadataOptionPrefix.Length..]] = value;
            }
        }

        var sourceId = string.IsNullOrWhiteSpace(source.SourceId) ? source.Location : source.SourceId;

        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);

        yield return new RagDocument
        {
            Id = sourceId,
            SourceId = sourceId,
            Content = content,
            Location = source.Location,
            Metadata = metadata.ToImmutable(),
        };
    }
}
