using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;
using Orkeon.Tools.Rag.DependencyInjection;

namespace Orkeon.Examples.Rag.CrewYaml;

/// <summary>
/// Offline demo of a crew declared entirely in YAML (RAG-03/C3+C4): the crew's
/// <c>rag:</c> block gets its collections ingested at crew creation (kickoff),
/// the agent's <c>knowledge:</c> attachment binds it to the collection, and the
/// knowledge-context augmenter — the exact component the execution path uses —
/// produces the cited <c>[1]</c> block for a customer question. No API key, no
/// network: local BGE embeddings, retrieval-only augmentation.
/// </summary>
internal static class Program
{
    private const string Question = "How many days do customers have to request a refund?";

    private static async Task<int> Main(string[] args)
    {
        var baseDir = ResolveBaseDir(args);
        var dataDir = Path.Combine(baseDir, "data");
        var yamlPath = Path.Combine(baseDir, "crew.yaml");
        var outputDir = Directory.CreateTempSubdirectory("orkeon-rag-crew-yaml-").FullName;

        Console.WriteLine("RAG crew-yaml — YAML `rag:` block + agent `knowledge:` attachment (offline)");
        Console.WriteLine($"  crew      : {yamlPath}");
        Console.WriteLine($"  corpus    : {dataDir}  (mounted read-only at /kb)");
        Console.WriteLine();

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(dataDir, "/kb", FileAccessRights.ReadOnly),
            new FileSystemMount(outputDir, "/output", FileAccessRights.ReadWrite),
        ]);

        using var offlineChatClient = new OfflineChatClient();
        await using var provider = BuildProvider(registry, offlineChatClient);

        // 1. YAML -> configuration -> crew. Creating the crew ingests the
        //    collections declared by the rag: block (RagCollectionsBootstrapper,
        //    incremental manifest — a second run with the same /output mount
        //    would re-embed nothing).
        var loader = provider.GetRequiredService<ICrewDefinitionLoader>();
        var config = await loader.LoadFromStringAsync(await File.ReadAllTextAsync(yamlPath));

        var factory = provider.GetRequiredService<ICrewFactory>();
        var crew = await factory.CreateFromConfigAsync(config);
        Console.WriteLine($"[1/3] Crew '{config.Name}' created — rag: collections ingested at kickoff.");

        // 2. Prove the declared collection is searchable, offline.
        var store = provider.GetRequiredService<IDocumentStore>();
        var embedder = provider.GetRequiredService<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>();
        var probe = await store.SearchAsync("product-kb", new RetrievalQuery
        {
            Text = Question,
            Embedding = [.. await embedder.GetEmbeddingAsync(Question)],
            TopK = 3,
        });
        Console.WriteLine($"[2/3] Collection 'product-kb' is searchable: {probe.Count} chunks retrieved.");
        Console.WriteLine();

        // 3. The agent carries its YAML knowledge attachment; the augmenter —
        //    the component the execution orchestrator calls when assembling the
        //    task context — builds the cited block injected into the prompt.
        var agentRepository = provider.GetRequiredService<Orkeon.Domain.Agent.IAgentRepository>();
        var agents = await agentRepository.GetByIdsAsync(crew.Agents);
        var agent = agents.Single();
        Console.WriteLine($"[3/3] Agent '{agent.Role}' knowledge attachments: " +
            string.Join(", ", agent.KnowledgeAttachments.Select(a => a.Collection)));
        Console.WriteLine();

        var augmenter = provider.GetRequiredService<IKnowledgeContextAugmenter>();
        var block = await augmenter.BuildContextAsync(agent.KnowledgeAttachments, Question);

        Console.WriteLine($"Q: \"{Question}\"");
        Console.WriteLine();
        if (block is null)
        {
            Console.WriteLine("UNEXPECTED: no knowledge context was produced.");
            return 1;
        }

        Console.WriteLine("Knowledge context block injected into the agent prompt:");
        Console.WriteLine("--------------------------------------------------------");
        Console.WriteLine(block.Text);
        Console.WriteLine("--------------------------------------------------------");
        Console.WriteLine("Citations:");
        foreach (var citation in block.Citations)
        {
            Console.WriteLine(
                $"  [{citation.Marker}] {citation.Score.ToString("F4", CultureInfo.InvariantCulture)}  {citation.SourceId}");
        }

        Console.WriteLine();
        Console.WriteLine("With an LLM configured, running the crew would ground the agent's answer");
        Console.WriteLine("on this block and keep the [n] markers as citations.");

        return 0;
    }

    private static ServiceProvider BuildProvider(FileSystemRegistry registry, IChatClient offlineChatClient)
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(sp =>
            new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance));

        // Local BGE embeddings first (semantic-first resolver), offline chat
        // stub before the infrastructure defaults (no real LLM is ever wired).
        services.AddOrkeonLocalEmbeddings();
        services.AddSingleton(offlineChatClient);
        services.AddOrkeonInfrastructure();
        services.AddOrkeonRag(configuration);
        services.AddOrkeonRagTools(); // rag_search stays available to the crew's agents

        return services.BuildServiceProvider();
    }

    /// <summary>Directory holding crew.yaml and data/ (copied beside the binary).</summary>
    private static string ResolveBaseDir(string[] args)
    {
        if (args.Length > 0)
            return Path.GetFullPath(args[0]);

        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "crew.yaml")))
            return AppContext.BaseDirectory;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    }

    /// <summary>Permissive path validator for this standalone example (mount rights still apply).</summary>
    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
            PathValidationResult.Allowed(requestedPath);
    }

    /// <summary>Offline stub so AddOrkeonInfrastructure never wires a real LLM client.</summary>
    private sealed class OfflineChatClient : IChatClient
    {
        private const string Notice =
            "(offline mode — no LLM configured; the cited knowledge block above is the real value)";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Notice)));

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
