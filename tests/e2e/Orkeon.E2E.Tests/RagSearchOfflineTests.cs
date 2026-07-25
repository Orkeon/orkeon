using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;
using Orkeon.Tools.Rag;
using Orkeon.Tools.Rag.DependencyInjection;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.E2E.Tests;

/// <summary>
/// RAG acceptance (RAG-01, re-based on the RAG-02 subsystem): a crew-visible
/// <c>rag_search</c> tool returns semantic results end-to-end, fully offline
/// (no LLM API key — generation is stubbed, retrieval is real). Wiring is the
/// new world only: <c>AddOrkeonRag</c> + <c>AddOrkeonRagTools</c> over the
/// memory-provider-backed <see cref="IDocumentStore"/>.
/// </summary>
public class RagSearchOfflineTests
{
    private const string RefundDoc = "Customers may request a full refund within 30 days of purchase.";
    private const string QuantumDoc = "Quantum entanglement links the states of two distant particles.";
    private const string BreadDoc = "Sourdough bread should be baked at 220 degrees for 35 minutes.";

    private static ServiceProvider BuildProvider(bool useLocalBge)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService, Orkeon.Tests.Shared.FileSystem.FakeFileSystemService>();

        if (useLocalBge)
        {
            // Registers the Analysis-side BGE provider; the default resolver (RAG-01/C4)
            // must pick it up and adapt it to the Application port.
            services.AddOrkeonLocalEmbeddings();
        }
        else
        {
            // Host-registered port provider wins over the default resolution chain.
            services.AddSingleton<IEmbeddingProvider>(new BagOfWordsEmbeddingProvider());
        }

        // Offline: stub chat client registered first — the TryAdd default of
        // AddOrkeonInfrastructure() must not wire a real LLM. Retrieval stays fully real.
        services.AddSingleton<IChatClient, EchoChatClient>();

        services.AddOrkeonInfrastructure();
        services.AddOrkeonRag(configuration);
        services.AddOrkeonRagTools();

        return services.BuildServiceProvider();
    }

    private static async Task IngestAsync(ServiceProvider provider, CancellationToken ct)
    {
        // Ingestion through the subsystem's IDocumentStore default (the
        // MemoryProviderDocumentStore over the ambient IMemoryProvider), embeddings
        // from the resolved Application port — the exact chain rag_search queries.
        var store = provider.GetRequiredService<IDocumentStore>();
        var embedder = provider.GetRequiredService<IEmbeddingProvider>();

        var documents = new (string Id, string Source, string Content)[]
        {
            ("doc-refund", "policy", RefundDoc),
            ("doc-quantum", "physics", QuantumDoc),
            ("doc-bread", "cooking", BreadDoc),
        };

        foreach (var (id, source, content) in documents)
        {
            var vector = await embedder.GetEmbeddingAsync(content, ct);
            var chunk = new Chunk
            {
                Id = $"{id}#0",
                DocumentId = id,
                SourceId = source,
                Content = content,
                EndOffset = content.Length,
            };

            await store.UpsertAsync(
                RagSearchTool.DefaultCollection,
                [new EmbeddedChunk { Chunk = chunk, Embedding = [.. vector] }],
                ct);
        }
    }

    private static async Task AssertRagSearchIsSemanticAsync(ServiceProvider provider, string question)
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestAsync(provider, ct);

        var tools = provider.GetServices<IBaseTool>().ToList();
        var ragSearch = tools.FirstOrDefault(t => t.Name == "rag_search");
        Assert.NotNull(ragSearch); // RAG-01/C1: the tool is discoverable by agent registries

        var response = await ragSearch!.CallAsync(
            new ToolCallRequest(
                "rag_search",
                new Dictionary<string, object?>
                {
                    ["question"] = question,
                    ["top_k"] = 1,
                }),
            ct);

        Assert.True(response.Success, response.Error);
        var text = Assert.IsType<string>(response.Result);
        Assert.Contains("refund", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("entanglement", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RagSearch_IsDiscoverable_AndSemantic_WithHostRegisteredProvider()
    {
        await using var provider = BuildProvider(useLocalBge: false);
        // Lexical-overlap phrasing: bag-of-words cosine cannot bridge a paraphrase.
        await AssertRagSearchIsSemanticAsync(provider, "How many days do customers have to request a refund?");
    }

    [Fact]
    [Trait("Category", "Slow")] // boots the real BGE ONNX runtime — host/CI only
    public async Task RagSearch_IsSemantic_Offline_WithLocalBge()
    {
        await using var provider = BuildProvider(useLocalBge: true);
        // True paraphrase (no shared keywords with the document): only a real
        // semantic model can rank the refund policy first.
        await AssertRagSearchIsSemanticAsync(provider, "How long do buyers have to get their money back?");
    }

    /// <summary>Hand-written double: deterministic bag-of-words embeddings (cosine ≈ lexical overlap).</summary>
    private sealed class BagOfWordsEmbeddingProvider : IEmbeddingProvider
    {
        public string Name => "bag-of-words-test";
        public string Model => "bag-of-words";
        public int Dimensions => 256;

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Embed(text));

        public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult<IList<float[]>>(texts.Select(Embed).ToList());

        private float[] Embed(string text)
        {
            var vector = new float[Dimensions];
            var words = text.ToLowerInvariant()
                .Split([' ', '.', ',', '?', '!'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                // Stable, culture-invariant slot per word (no String.GetHashCode: randomized).
                var slot = 0;
                foreach (var c in word)
                    slot = (slot * 31 + c) & 0xFF;
                vector[slot] += 1f;
            }

            var norm = MathF.Sqrt(vector.Sum(v => v * v));
            if (norm > 0)
                for (var i = 0; i < vector.Length; i++)
                    vector[i] /= norm;
            return vector;
        }
    }

    /// <summary>Hand-written double: echoes that generation happened; sources carry the signal.</summary>
    private sealed class EchoChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "offline stub answer")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
            // Nothing to dispose.
        }
    }
}
