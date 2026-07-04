using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Vectors;

namespace Orkeon.Analysis.Tests;

public class EmbeddingProviderTests
{
    private static readonly float[] TwoDimEmbedding = [1.0f, 2.0f];
    private static readonly float[] UnitEmbedding = [1f, 0f];

    [Fact]
    public async Task OpenAI_embeds_batch_returning_matching_vectors()
    {
        using var handler = new FakeHttpHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            var inputs = doc.RootElement.GetProperty("input").EnumerateArray().ToArray();
            var data = inputs
                .Select((_, i) => new { index = i, embedding = new float[] { 0.1f, 0.2f, (float)i } })
                .ToArray();
            var payload = JsonSerializer.Serialize(new { data });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
        using var provider = new OpenAIEmbeddingProvider(new OpenAIEmbeddingOptions
        {
            ApiKey = "test",
            Dimensions = 3,
            BatchSize = 2,
        }, client);

        var vectors = await provider.EmbedBatchAsync(["a", "b", "c", "d"], CancellationToken.None);
        Assert.Equal(4, vectors.Count);
        Assert.Equal(0.1f, vectors[0].Span[0]);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task OpenAI_retries_on_transient_failure()
    {
        var attempt = 0;
        using var handler = new FakeHttpHandler(_ =>
        {
            attempt++;
            if (attempt == 1) return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            var payload = "{\"data\":[{\"index\":0,\"embedding\":[1.0]}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
        using var provider = new OpenAIEmbeddingProvider(new OpenAIEmbeddingOptions
        {
            ApiKey = "test",
            Dimensions = 1,
            MaxRetries = 2,
        }, client);

        var vectors = await provider.EmbedBatchAsync(["a"], CancellationToken.None);
        Assert.Single(vectors);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task OpenAI_binary_splits_chunk_when_backend_rejects_payload_as_too_large()
    {
        // Backend rejects multi-text chunks deterministically (e.g. one text > 512 tokens).
        // Provider must binary-split until each text is isolated, then succeed for the
        // ones that fit and recover the others via further splitting.
        const string TooLarge = "{\"error\":{\"message\":\"input (603 tokens) is too large to process. increase the physical batch size (current batch size: 512)\"}}";
        var rejectAbove = 1; // server only accepts 1-text chunks
        using var handler = new FakeHttpHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            var inputs = doc.RootElement.GetProperty("input").EnumerateArray().ToArray();
            if (inputs.Length > rejectAbove)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(TooLarge, Encoding.UTF8, "application/json"),
                };
            }
            var data = inputs
                .Select((_, i) => new { index = i, embedding = TwoDimEmbedding })
                .ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data }), Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
        using var provider = new OpenAIEmbeddingProvider(new OpenAIEmbeddingOptions
        {
            ApiKey = "test",
            Dimensions = 2,
            BatchSize = 4, // initial chunk of 4 will be rejected
        }, client);

        var vectors = await provider.EmbedBatchAsync(["a", "b", "c", "d"], CancellationToken.None);
        Assert.Equal(4, vectors.Count);
        // Each vector must be the success payload (bisection isolated each text).
        foreach (var v in vectors)
        {
            Assert.Equal(2, v.Length);
            Assert.Equal(1.0f, v.Span[0]);
        }
    }

    [Fact]
    public async Task OpenAI_bisects_oversized_single_text_until_it_fits()
    {
        // A single text is too long; backend would 500 forever. Provider must halve
        // the text content recursively until it fits, instead of looping until the
        // circuit breaker trips.
        const string TooLarge = "{\"error\":{\"message\":\"input (1024 tokens) is too large to process. increase the physical batch size (current batch size: 512)\"}}";
        const int CharsThatFit = 800; // any payload <= 800 chars succeeds
        using var handler = new FakeHttpHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            var firstInput = doc.RootElement.GetProperty("input")[0].GetString() ?? "";
            if (firstInput.Length > CharsThatFit)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(TooLarge, Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[{\"index\":0,\"embedding\":[0.7]}]}", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
        using var provider = new OpenAIEmbeddingProvider(new OpenAIEmbeddingOptions
        {
            ApiKey = "test",
            Dimensions = 1,
            BatchSize = 1,
            MinTruncationChars = 16,
        }, client);

        var bigText = new string('x', 4096); // 4096 → 2048 → 1024 → 512 (≤ 800 ✓)
        var vectors = await provider.EmbedBatchAsync([bigText], CancellationToken.None);
        Assert.Single(vectors);
        Assert.Equal(0.7f, vectors[0].Span[0]);
        // 3 rejected attempts (4096, 2048, 1024) + 1 success (512) = 4 calls
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task OpenAI_does_not_loop_when_text_too_large_below_min_truncation()
    {
        // Pathological backend rejects everything: bisection must stop at MinTruncationChars
        // and propagate the exception instead of looping forever.
        const string TooLarge = "{\"error\":{\"message\":\"input is too large to process. increase the physical batch size (current batch size: 512)\"}}";
        using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(TooLarge, Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
        using var provider = new OpenAIEmbeddingProvider(new OpenAIEmbeddingOptions
        {
            ApiKey = "test",
            Dimensions = 1,
            BatchSize = 1,
            MinTruncationChars = 64,
        }, client);

        await Assert.ThrowsAsync<EmbeddingPayloadTooLargeException>(
            () => provider.EmbedBatchAsync([new string('x', 256)], CancellationToken.None));
        // 256 → 128 → 64 (= MinTruncationChars, stop) = 3 calls
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Ollama_embeds_sequentially()
    {
        using var handler = new FakeHttpHandler(_ =>
        {
            var payload = "{\"embedding\":[0.5,0.25,0.0]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var provider = new OllamaEmbeddingProvider(new OllamaEmbeddingOptions { Dimensions = 3 }, client);

        var vectors = await provider.EmbedBatchAsync(["x", "y"], CancellationToken.None);
        Assert.Equal(2, vectors.Count);
        Assert.Equal(0.5f, vectors[0].Span[0]);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task EmbedAsync_default_writes_embeddings_on_nodes()
    {
        using var handler = new FakeHttpHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            var inputs = doc.RootElement.GetProperty("input").EnumerateArray().ToArray();
            var data = inputs
                .Select((_, i) => new { index = i, embedding = UnitEmbedding })
                .ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data }), Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
        using var implementation = new OpenAIEmbeddingProvider(new OpenAIEmbeddingOptions { ApiKey = "k", Dimensions = 2 }, client);
        IEmbeddingProvider provider = implementation;

        var node = new RaggableNode
        {
            Id = "n1", Kind = UniversalNodeKind.Class, Name = "A",
            VirtualFilePath = "/a.ts", Range = new NodeRange(0, 0, 0, 0, 0),
            Level = NodeLevel.L3_Symbol, Language = "ts",
            SourceSnippet = "body", Sha256 = "",
            Fqn = "A",
            EmbeddingText = "class A",
        };
        await provider.EmbedAsync([node], "m", CancellationToken.None);
        Assert.NotNull(node.Embedding);
        Assert.Equal(2, node.Embedding!.Value.Length);
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int Calls;
        public List<string> Bodies { get; } = [];
        public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            Bodies.Add(body);
            return Task.FromResult(_responder(request));
        }
    }
}
