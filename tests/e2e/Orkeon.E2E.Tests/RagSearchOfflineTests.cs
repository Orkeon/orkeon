using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Knowledge;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.E2E.Tests;

/// <summary>
/// RAG-01 acceptance: a crew-visible <c>rag_search</c> tool returns semantic results
/// end-to-end, fully offline (no LLM API key — generation is stubbed, retrieval is real).
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

        services.AddOrkeonInfrastructure();
        services.AddOrkeonKnowledge(configuration);
        services.AddOrkeonRag(configuration);

        // Offline: replace the LLM-backed generator, retrieval stays fully real.
        services.AddScoped<IResponseGenerator, EchoResponseGenerator>();

        return services.BuildServiceProvider();
    }

    private static async Task AssertRagSearchIsSemanticAsync(ServiceProvider provider, string question)
    {
        var knowledge = provider.GetRequiredService<IKnowledgeService>();
        var ct = TestContext.Current.CancellationToken;
        await knowledge.AddKnowledgeAsync(RefundDoc, source: "policy", cancellationToken: ct);
        await knowledge.AddKnowledgeAsync(QuantumDoc, source: "physics", cancellationToken: ct);
        await knowledge.AddKnowledgeAsync(BreadDoc, source: "cooking", cancellationToken: ct);

        using var scope = provider.CreateScope();
        var tools = scope.ServiceProvider.GetServices<IBaseTool>().ToList();
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
    private sealed class EchoResponseGenerator : IResponseGenerator
    {
        public Task<GeneratedResponse> GenerateAsync(
            AugmentedPrompt prompt,
            GenerationOptions options,
            CancellationToken ct = default)
            => Task.FromResult(new GeneratedResponse { Text = "offline stub answer", TokensUsed = 0 });
    }
}
