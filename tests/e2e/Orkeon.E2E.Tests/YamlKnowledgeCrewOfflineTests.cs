using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Rag.DependencyInjection;

namespace Orkeon.E2E.Tests;

/// <summary>
/// RAG-03 acceptance n°2: a crew declared entirely in YAML (crew-level <c>rag:</c> block +
/// agent-level <c>knowledge:</c> attachment) answers from its attached knowledge base,
/// with citations, fully offline. Loading the crew ingests the declared collection
/// (incremental manifest), and the knowledge-context augmenter used by the execution
/// path produces the cited block for the task input.
/// </summary>
public class YamlKnowledgeCrewOfflineTests
{
    private const string FaqContent =
        "Refund policy: customers may request a full refund within 30 days of purchase. " +
        "Battery: the device lasts 12 hours on a full charge.";

    private const string CrewYaml = """
name: support-crew
goal: Answer customer questions from the product FAQ
rag:
  collections:
    produits:
      sources: ["/kb/faq.md"]
agents:
  support:
    role: Support agent
    goal: Answer questions from the knowledge base
    knowledge: [produits]
tasks:
  answer:
    description: Answer the customer question
    expected_output: A grounded answer with citations
    agent: support
""";

    [Fact]
    public async Task YamlCrew_WithRagBlockAndKnowledgeAttachment_ProducesCitedKnowledgeContext_Offline()
    {
        var ct = TestContext.Current.CancellationToken;

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/kb")
            .AddMount("/output", Orkeon.Domain.FileSystem.FileAccessRights.Read
                | Orkeon.Domain.FileSystem.FileAccessRights.Write
                | Orkeon.Domain.FileSystem.FileAccessRights.Create)
            .AddFile("/kb/faq.md", FaqContent));
        services.AddSingleton<IEmbeddingProvider>(new LexicalEmbeddingProvider());
        services.AddSingleton<IChatClient, NullChatClient>();
        services.AddOrkeonInfrastructure();
        services.AddOrkeonRag(configuration);
        services.AddOrkeonRagTools();

        await using var provider = services.BuildServiceProvider();

        // 1. YAML → configuration → crew: loading ingests the declared collection.
        var loader = provider.GetRequiredService<ICrewDefinitionLoader>();
        var config = await loader.LoadFromStringAsync(CrewYaml, ct);
        var factory = provider.GetRequiredService<ICrewFactory>();
        var crew = await factory.CreateFromConfigAsync(config, ct);
        Assert.NotNull(crew);

        // 2. The declared collection is searchable (ingested at crew load, offline).
        var store = provider.GetRequiredService<IDocumentStore>();
        var embedder = provider.GetRequiredService<IEmbeddingProvider>();
        var queryEmbedding = await embedder.GetEmbeddingAsync("refund within 30 days", ct);
        var hits = await store.SearchAsync(
            "produits",
            new RetrievalQuery { Text = "refund", Embedding = [.. queryEmbedding], TopK = 3 },
            ct);
        Assert.NotEmpty(hits);

        // 3. The agent carries its YAML knowledge attachment.
        var agentRepository = provider.GetRequiredService<Orkeon.Domain.Agent.IAgentRepository>();
        var agents = await agentRepository.GetByIdsAsync(crew.Agents, ct);
        var agent = Assert.Single(agents);
        var attachment = Assert.Single(agent.KnowledgeAttachments);
        Assert.Equal("produits", attachment.Collection);

        // 4. The execution-path augmenter builds the cited block from the attachment
        //    (prompt injection itself is covered byte-for-byte in Application tests).
        var augmenter = provider.GetRequiredService<IKnowledgeContextAugmenter>();
        var block = await augmenter.BuildContextAsync(
            agent.KnowledgeAttachments,
            "How many days do customers have to request a refund?",
            ct);

        Assert.NotNull(block);
        Assert.Contains("[1]", block!.Text, StringComparison.Ordinal);
        Assert.Contains("refund", block.Text, StringComparison.OrdinalIgnoreCase);
        var citation = Assert.Single(block.Citations);
        Assert.Equal(1, citation.Marker);
        Assert.EndsWith("faq.md", citation.SourceId, StringComparison.Ordinal);
    }

    /// <summary>Hand-written double: deterministic bag-of-words embeddings (cosine ≈ lexical overlap).</summary>
    private sealed class LexicalEmbeddingProvider : IEmbeddingProvider
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
            foreach (var word in text.ToLowerInvariant()
                .Split([' ', '.', ',', '?', '!', ':'], StringSplitOptions.RemoveEmptyEntries))
            {
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

    /// <summary>Offline stub so AddOrkeonInfrastructure never wires a real LLM client.</summary>
    private sealed class NullChatClient : IChatClient
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
