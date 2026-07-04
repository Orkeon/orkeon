using System.Diagnostics.CodeAnalysis;
using System.Net;
using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Knowledge.Sources;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Coverage for <see cref="WebKnowledgeSource"/> using a mocked <see cref="HttpClient"/>
/// (via a stub <see cref="HttpMessageHandler"/>) and a deterministic stub chunker.
/// </summary>
public class CovMisc_WebKnowledgeSourceTests
{
    private const string Url = "https://example.com/page";

    /// <summary>Stub handler returning fixed HTML body content.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            });
    }

    /// <summary>Stub chunker that splits content into fixed-size character windows.</summary>
    private sealed class FixedChunker : ITextChunker
    {
        private readonly int _size;
        public FixedChunker(int size = 10) => _size = size;
        public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
        {
            var chunks = new List<TextChunk>();
            for (int i = 0; i < text.Length; i += _size)
            {
                var len = Math.Min(_size, text.Length - i);
                chunks.Add(new TextChunk(text.Substring(i, len), i, i + len));
            }
            return chunks;
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The handler and HttpClient are captured by the returned knowledge source and must outlive this factory; they live for the duration of the test.")]
    private static WebKnowledgeSource Create(string body, ITextChunker chunker)
    {
        var innerHandler = new StubHandler(body);
        var http = new HttpClient(innerHandler);
        var loader = new WebPageLoader(http);
        return new WebKnowledgeSource(new Uri(Url), loader, chunker);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        using var innerHandler2 = new StubHandler("x");
        using var http = new HttpClient(innerHandler2);
        var loader = new WebPageLoader(http);
        var chunker = new FixedChunker();

        Assert.Throws<ArgumentNullException>(() => new WebKnowledgeSource(null!, loader, chunker));
        Assert.Throws<ArgumentNullException>(() => new WebKnowledgeSource(new Uri(Url), null!, chunker));
        Assert.Throws<ArgumentNullException>(() => new WebKnowledgeSource(new Uri(Url), loader, null!));
    }

    [Fact]
    public void Metadata_NameIsHost_TypeIsWeb()
    {
        var source = Create("hello", new FixedChunker());

        Assert.Equal("example.com", source.Name);
        Assert.Equal("web", source.Type);
        Assert.NotEqual(default, source.Id);
    }

    [Fact]
    public async Task GetContentAsync_CombinesChunks_AndPopulatesMetadata()
    {
        // "abcdefghij" -> 3 chunks of size 4: "abcd","efgh","ij"
        var source = Create("abcdefghij", new FixedChunker(size: 4));

        var content = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("example.com", content.Title);
        Assert.Equal(Url, content.Source);
        Assert.Contains("abcd", content.Content);
        Assert.Contains("ij", content.Content);
        Assert.Equal(3, content.Metadata["chunk_count"]);
        Assert.Equal("web", content.Metadata["source_type"]);
        Assert.Equal(Url, content.Metadata["url"]);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsChunksUpToLimit()
    {
        var source = Create("abcdefghijklmnop", new FixedChunker(size: 4)); // 4 chunks

        var results = (await source.SearchAsync("   ", limit: 2, TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_RanksByTermMatches()
    {
        // Chunk by lines so each sentence is its own chunk.
        var body = "apple banana cherry|date elderberry|apple grape";
        var source = Create(body, new PipeChunker());

        var results = (await source.SearchAsync("apple", limit: 10, TestContext.Current.CancellationToken)).ToList();

        // Two chunks contain "apple"; the one with no match is filtered out.
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("apple", r.Content, StringComparison.OrdinalIgnoreCase));
        Assert.All(results, r => Assert.True(r.Relevance > 0));
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var source = Create("alpha|beta|gamma", new PipeChunker());

        var results = (await source.SearchAsync("zeta", limit: 10, TestContext.Current.CancellationToken)).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public async Task EnsureLoaded_IsCachedAcrossCalls()
    {
        // A handler that throws on the second request proves loading happens only once.
        using var innerHandler1 = new OnceHandler("hello world");
        using var http = new HttpClient(innerHandler1);
        var loader = new WebPageLoader(http);
        var source = new WebKnowledgeSource(new Uri(Url), loader, new PipeChunker());

        var first = await source.GetContentAsync(TestContext.Current.CancellationToken);
        var second = await source.SearchAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotEmpty(second);
    }

    private sealed class PipeChunker : ITextChunker
    {
        public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
        {
            var parts = text.Split('|');
            var result = new List<TextChunk>();
            int pos = 0;
            foreach (var p in parts)
            {
                result.Add(new TextChunk(p, pos, pos + p.Length));
                pos += p.Length + 1;
            }
            return result;
        }
    }

    /// <summary>Returns the body on the first call, throws on subsequent calls.</summary>
    private sealed class OnceHandler : HttpMessageHandler
    {
        private readonly string _body;
        private int _calls;
        public OnceHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) > 1)
                throw new InvalidOperationException("loader should only fetch once");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body)
            });
        }
    }
}
