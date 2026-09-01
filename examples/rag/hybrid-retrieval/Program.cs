using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Pipeline;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.Examples.Rag.HybridRetrieval;

/// <summary>
/// Offline demo of hybrid retrieval (RAG-04/C2): the same corpus and the same
/// questions run through two <see cref="StagedRagPipeline"/> instances — one
/// vector-only (the <c>fast</c> preset) and one with hybrid BM25 + RRF fusion
/// enabled (<c>Orkeon:Rag:Retrieval:Hybrid</c>) — to show when lexical evidence
/// (an exact error code) rescues a query that pure semantic similarity misses.
/// Every stage trace is printed, including the <c>fuse</c> step (method, in/out).
/// </summary>
internal static class Program
{
    private const string Collection = "atlascam-docs";

    /// <summary>Query 1 — exact rare token: BM25 pins the release-notes page, cosine drifts to the generic troubleshooting page.</summary>
    private const string LexicalQuery = "The camera screen shows E-417 after a reboot, how do I fix it?";

    /// <summary>Query 2 — pure paraphrase (no shared keyword): only the vector side can route it; hybrid must not degrade it.</summary>
    private const string SemanticQuery = "How can I make the picture look sharper?";

    private static async Task<int> Main(string[] args)
    {
        var dataDir = ResolveDataDir(args);
        var outputDir = Directory.CreateTempSubdirectory("orkeon-rag-hybrid-").FullName;

        Console.WriteLine("RAG hybrid-retrieval — vector-only vs hybrid BM25 + RRF (offline, local BGE embeddings)");
        Console.WriteLine($"  corpus    : {dataDir}  (mounted read-only at /data)");
        Console.WriteLine();

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(dataDir, "/data", FileAccessRights.ReadOnly),
            new FileSystemMount(outputDir, "/output", FileAccessRights.ReadWrite),
        ]);

        using var offlineChatClient = new OfflineChatClient();
        await using var provider = BuildProvider(registry, offlineChatClient);

        // Ingestion feeds BOTH sides at once: the inner store receives the
        // embedded chunks, and the hybrid decorator (always wrapping the store,
        // see AddOrkeonHybridRetrieval) indexes the same chunks into its
        // in-process BM25 index.
        var ingestion = provider.GetRequiredService<IIngestionPipeline>();
        var report = await ingestion.IngestAsync(new IngestionRequest
        {
            Collection = Collection,
            Sources = Directory.EnumerateFiles(dataDir, "*.md")
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new SourceDescriptor { Location = $"/data/{Path.GetFileName(f)}", Kind = "file" })
                .ToImmutableList(),
        });
        Console.WriteLine($"Ingested {report.DocumentsLoaded} documents / {report.ChunksEmbedded} chunks embedded into '{Collection}'.");
        Console.WriteLine();

        // Two pipelines over the SAME store and embeddings; only the options
        // differ. `fast` = vector only; the hybrid variant flips
        // Retrieval:Hybrid:Enabled (in configuration terms:
        // Orkeon:Rag:Retrieval:Hybrid:Enabled = true), which the hybrid-capable
        // store honours per query.
        var store = provider.GetRequiredService<IDocumentStore>();
        var embeddings = provider.GetRequiredService<IEmbeddingProvider>();

        var vectorOnly = new StagedRagPipeline(
            store, embeddings, offlineChatClient, RagProfilePresets.Create(RagProfile.Fast));

        var hybridOptions = RagProfilePresets.Create(RagProfile.Fast);
        hybridOptions.Retrieval.Hybrid.Enabled = true; // BM25 + RRF fusion (RrfK = 60)
        var hybrid = new StagedRagPipeline(store, embeddings, offlineChatClient, hybridOptions);

        Console.WriteLine("=== Query 1 — exact error code (lexical evidence) ===");
        await CompareAsync(vectorOnly, hybrid, LexicalQuery);
        Console.WriteLine("  => vector-only drifts to the generic troubleshooting page; the BM25 side");
        Console.WriteLine("     pins 'E-417' in the firmware notes and RRF pushes it to rank [1].");
        Console.WriteLine();

        Console.WriteLine("=== Query 2 — pure paraphrase (semantic evidence) ===");
        await CompareAsync(vectorOnly, hybrid, SemanticQuery);
        Console.WriteLine("  => no keyword overlap: the vector side carries the query, and hybrid");
        Console.WriteLine("     fusion keeps the semantic winner on top (it never degrades this case).");
        Console.WriteLine();

        // Bonus: the same comparison at the store level, where the score
        // provenance is visible (ScoreOrigin: 'local-cosine' passthrough vs
        // 'rrf' fused rank-aggregate — RRF scores are NOT similarities).
        Console.WriteLine("=== Store-level view (ScoreOrigin) — query 1 ===");
        await ShowStoreScoresAsync(store, embeddings, LexicalQuery);

        return 0;
    }

    /// <summary>Runs one question through both pipelines and prints rankings + the hybrid run's stage traces.</summary>
#pragma warning disable CA1859 // the example deliberately takes IRagPipeline: consuming the abstraction is the point
    private static async Task CompareAsync(IRagPipeline vectorOnly, IRagPipeline hybrid, string question)
    {
        Console.WriteLine($"Q: \"{question}\"");

        var vectorAnswer = await vectorOnly.QueryAsync(new RagQuery { Text = question, Collection = Collection, TopN = 3 });
        var hybridAnswer = await hybrid.QueryAsync(new RagQuery { Text = question, Collection = Collection, TopN = 3 });

        Console.WriteLine("  vector only:");
        PrintCitations(vectorAnswer);
        Console.WriteLine("  hybrid (BM25 + RRF):");
        PrintCitations(hybridAnswer);

        Console.WriteLine("  hybrid trace:");
        foreach (var step in hybridAnswer.Trace.Steps)
        {
            var data = string.Join(", ", step.Data.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));
            Console.WriteLine($"    - {step.Name,-9} {(step.Detail is null ? "" : $"({step.Detail}) ")}{data}");
        }
    }

#pragma warning restore CA1859
    private static void PrintCitations(RagAnswer answer)
    {
        foreach (var citation in answer.Citations)
        {
            Console.WriteLine(
                $"    [{citation.Marker}] {citation.Score.ToString("F4", CultureInfo.InvariantCulture)}  {citation.SourceId}");
        }
    }

    /// <summary>
    /// Queries the (hybrid-capable) document store directly, with and without
    /// per-query fusion, and prints each hit's score provenance.
    /// </summary>
    private static async Task ShowStoreScoresAsync(
        IDocumentStore store, IEmbeddingProvider embeddings, string question)
    {
        var embedding = await embeddings.GetEmbeddingAsync(question);

        foreach (var (label, hybridFlag) in new[] { ("vector only", false), ("hybrid", true) })
        {
            var hits = await store.SearchAsync(Collection, new RetrievalQuery
            {
                Text = question,
                Embedding = [.. embedding],
                TopK = 4,
                Hybrid = hybridFlag,
            });

            Console.WriteLine($"  {label}:");
            foreach (var hit in hits)
            {
                Console.WriteLine(
                    $"    {hit.Score.ToString("F4", CultureInfo.InvariantCulture)}  origin={hit.ScoreOrigin,-12}  {hit.Chunk.SourceId}");
            }
        }
    }

    private static ServiceProvider BuildProvider(FileSystemRegistry registry, IChatClient offlineChatClient)
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(sp =>
            new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance));

        // On-device BGE-micro-v2 (384 dims, CPU, no network) — registered before
        // AddOrkeonInfrastructure() so the semantic-first resolver adopts it.
        services.AddOrkeonLocalEmbeddings();
        services.AddSingleton(offlineChatClient);
        services.AddOrkeonInfrastructure();
        services.AddOrkeonRag(configuration);

        return services.BuildServiceProvider();
    }

    private static string ResolveDataDir(string[] args)
    {
        if (args.Length > 0)
            return Path.GetFullPath(args[0]);

        var beside = Path.Combine(AppContext.BaseDirectory, "data");
        if (Directory.Exists(beside))
            return beside;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "data"));
    }

    /// <summary>Permissive path validator for this standalone example (mount rights still apply).</summary>
    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
            PathValidationResult.Allowed(requestedPath);
    }

    /// <summary>
    /// Deterministic offline <see cref="IChatClient"/>: this demo is about
    /// retrieval rankings and traces, so generation returns a fixed notice.
    /// </summary>
    private sealed class OfflineChatClient : IChatClient
    {
        private const string Notice =
            "(offline mode — no LLM configured; compare the retrieved rankings below)";

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
