using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Domain.Common;
using Orkeon.Domain.Knowledge;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Knowledge.Sources;

/// <summary>
/// Knowledge source backed by a web page.
/// Loads the page using WebPageLoader and chunks content using ITextChunker.
/// </summary>
public class WebKnowledgeSource : IKnowledgeSource
{
    private readonly string _url;
    private readonly WebPageLoader _loader;
    private readonly ITextChunker _chunker;
    private readonly ChunkingOptions? _chunkingOptions;
    private List<KnowledgeContent>? _chunks;

    /// <summary>Initializes a new instance of <see cref="WebKnowledgeSource"/>.</summary>
    /// <param name="url">The URL of the web page to use as the knowledge source.</param>
    /// <param name="loader">The web page loader used to fetch page content.</param>
    /// <param name="chunker">The text chunker used to split content into chunks.</param>
    /// <param name="chunkingOptions">Optional chunking configuration options.</param>
    public WebKnowledgeSource(
        Uri url,
        WebPageLoader loader,
        ITextChunker chunker,
        ChunkingOptions? chunkingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = url.ToString();
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        ArgumentNullException.ThrowIfNull(chunker);
        _chunker = chunker;
        _chunkingOptions = chunkingOptions;
    }

    private readonly KnowledgeSourceId _id = KnowledgeSourceId.Create();

    /// <inheritdoc />
    public KnowledgeSourceId Id => _id;

    /// <inheritdoc />
    public string Name => new Uri(_url).Host;

    /// <inheritdoc />
    public string Type => "web";

    /// <inheritdoc />
    public async Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var combinedContent = string.Join("\n\n", _chunks!.Select(c => c.Content));

        return new KnowledgeContent
        {
            Id = KnowledgeContentId.Create(),
            Title = Name,
            Content = combinedContent,
            Source = _url,
            Metadata = new Dictionary<string, object>
            {
                ["chunk_count"] = _chunks!.Count,
                ["source_type"] = Type,
                ["url"] = _url
            }
        };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KnowledgeContent>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(query))
            return _chunks!.Take(limit);

        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var scored = _chunks!
            .Select(chunk =>
            {
                var content = chunk.Content;
                int matchCount = queryTerms.Count(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));

                double relevance = queryTerms.Length > 0
                    ? (double)matchCount / queryTerms.Length
                    : 0.0;

                return (Chunk: chunk, Relevance: relevance);
            })
            .Where(x => x.Relevance > 0)
            .OrderByDescending(x => x.Relevance)
            .Take(limit)
            .Select(x => x.Chunk with { Relevance = x.Relevance });

        return scored;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_chunks != null)
            return;

        var document = await _loader.LoadAsync(_url, cancellationToken).ConfigureAwait(false);
        var textChunks = _chunker.Chunk(document.Content, _chunkingOptions);

        _chunks = [];
        for (int i = 0; i < textChunks.Count; i++)
        {
            var chunk = textChunks[i];
            _chunks.Add(new KnowledgeContent
            {
                Id = KnowledgeContentId.Create(),
                Title = $"{Name} (chunk {i + 1}/{textChunks.Count})",
                Content = chunk.Content,
                Source = _url,
                Metadata = new Dictionary<string, object>(document.Metadata)
                {
                    ["chunk_index"] = i,
                    ["chunk_start"] = chunk.StartIndex,
                    ["chunk_end"] = chunk.EndIndex,
                    ["total_chunks"] = textChunks.Count
                }
            });
        }
    }
}
