using System.Runtime.CompilerServices;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Tools.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IDocumentLoader"/> standing in for the web page loader: it claims the
/// http(s) addresses, records each one exactly as it was handed over, and answers one short
/// document per address, without any network.
/// </summary>
public sealed class RecordingWebLoader : IDocumentLoader
{
    /// <summary>The locations <see cref="LoadAsync"/> was asked for, in order.</summary>
    public List<string> Loaded { get; } = [];

    /// <inheritdoc />
    public bool CanLoad(SourceDescriptor source) =>
        source is not null
        && (source.Location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || source.Location.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async IAsyncEnumerable<RagDocument> LoadAsync(
        SourceDescriptor source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Loaded.Add(source.Location);
        await Task.Yield();
        yield return new RagDocument
        {
            Id = $"web-{Loaded.Count}",
            SourceId = source.Location,
            Location = source.Location,
            Content = "A short page about the opening hours of the library.",
        };
    }
}
