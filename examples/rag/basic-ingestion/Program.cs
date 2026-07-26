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
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;
using Orkeon.Tools.Rag.DependencyInjection;

namespace Orkeon.Examples.Rag.BasicIngestion;

/// <summary>
/// End-to-end offline demo of the RAG subsystem (RAG-03/C6):
/// incremental ingestion of a small FAQ corpus (local BGE embeddings, no API
/// key), a second ingestion proving zero re-embedding, then two cited queries
/// through <see cref="IRagPipeline"/> — one of them a pure paraphrase to show
/// the retrieval is semantic, not lexical.
/// </summary>
internal static class Program
{
    private const string Collection = "demo-faq";

    private static async Task<int> Main(string[] args)
    {
        var dataDir = ResolveDataDir(args);
        var corpusFiles = Directory.EnumerateFiles(dataDir, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        if (corpusFiles.Length == 0)
        {
            await Console.Error.WriteLineAsync($"No corpus files found under {dataDir}.");
            return 1;
        }

        // Per-run scratch directory for the writable /output mount (ingestion
        // manifests land in /output/rag/manifests). A fresh directory keeps the
        // manifest aligned with the in-memory document store, which is empty at
        // every process start.
        var outputDir = Directory.CreateTempSubdirectory("orkeon-rag-basic-ingestion-").FullName;

        Console.WriteLine("RAG basic-ingestion — offline demo (local BGE embeddings, zero API key)");
        Console.WriteLine($"  corpus    : {dataDir}  (mounted read-only at /data)");
        Console.WriteLine($"  manifests : {outputDir}  (mounted read-write at /output)");
        Console.WriteLine();

        // VFS: the corpus is read-only under /data; manifests are written under
        // /output (default manifest directory: /output/rag/manifests).
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(dataDir, "/data", FileAccessRights.ReadOnly),
            new FileSystemMount(outputDir, "/output", FileAccessRights.ReadWrite),
        ]);

        // Offline generation: StagedRagPipeline requires an IChatClient, and the
        // TryAdd default of AddOrkeonInfrastructure() would wire a real LLM
        // provider (API key required at call time). Registering this stub first
        // keeps the example key-free: retrieval and citations are fully real,
        // only the answer text is the deterministic offline notice.
        using var offlineChatClient = new OfflineChatClient();

        await using var provider = BuildProvider(registry, offlineChatClient);

        var ingestion = provider.GetRequiredService<IIngestionPipeline>();
        var request = new IngestionRequest
        {
            Collection = Collection,
            Sources = corpusFiles
                .Select(f => new SourceDescriptor { Location = $"/data/{Path.GetFileName(f)}", Kind = "file" })
                .ToImmutableList(),
        };

        // [1/3] Initial ingestion: everything is new, every chunk is embedded.
        Console.WriteLine($"[1/3] Initial ingestion into collection '{Collection}'...");
        PrintReport(await ingestion.IngestAsync(request));

        // [2/3] Same corpus again: the per-collection manifest short-circuits every
        // source — zero chunks embedded, zero store writes (RAG-03/C1).
        Console.WriteLine("[2/3] Second ingestion of the unchanged corpus (incremental)...");
        var second = await ingestion.IngestAsync(request);
        PrintReport(second);
        Console.WriteLine(second.ChunksEmbedded == 0
            ? "  => unchanged corpus: 0 embeddings computed on the second run."
            : "  => UNEXPECTED: the second run re-embedded chunks.");
        Console.WriteLine();

        // [3/3] Two questions through the query pipeline. No LLM is configured in
        // this offline demo (see OfflineChatClient below), so the value shown is
        // the real part: semantic retrieval with scored, cited passages.
        var rag = provider.GetRequiredService<IRagPipeline>();

        Console.WriteLine("[3/3] Questions (semantic retrieval + citations)...");
        Console.WriteLine();

        // Lexical overlap with the corpus ("battery", "full charge").
        await AskAsync(rag, "How long does the battery last on a full charge?");

        // Pure paraphrase: "buyers" / "money back" never appear in the corpus —
        // warranty-and-returns.md talks about "returns" and "refund". Only a
        // semantic embedding model can rank that page first.
        await AskAsync(rag, "How long do buyers have to get their money back?");

        return 0;
    }

    /// <summary>
    /// Wires the whole chain: VFS mounts, local BGE embeddings, offline chat
    /// client, core infrastructure, RAG subsystem, and the rag_search agent tool.
    /// </summary>
    private static ServiceProvider BuildProvider(FileSystemRegistry registry, IChatClient offlineChatClient)
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddSingleton<IFileSystemService>(sp =>
            new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance));

        // On-device BGE-micro-v2 (384 dims, CPU, no network): registered BEFORE
        // AddOrkeonInfrastructure() so the semantic-first default resolver adapts
        // it as the Application-port IEmbeddingProvider.
        services.AddOrkeonLocalEmbeddings();

        // Offline stub first — the TryAdd IChatClient default of
        // AddOrkeonInfrastructure() must not wire a real LLM (see Main).
        services.AddSingleton(offlineChatClient);

        services.AddOrkeonInfrastructure();
        services.AddOrkeonRag(configuration);
        services.AddOrkeonRagTools(); // rag_search becomes discoverable by agent registries

        return services.BuildServiceProvider();
    }

    private static async Task AskAsync(IRagPipeline rag, string question)
    {
        Console.WriteLine($"Q: \"{question}\"");

        var answer = await rag.QueryAsync(new RagQuery
        {
            Text = question,
            Collection = Collection,
            TopN = 3,
        });

        Console.WriteLine($"A: {answer.Text}");
        foreach (var citation in answer.Citations)
        {
            var snippet = (citation.Snippet ?? string.Empty).ReplaceLineEndings(" ").Trim();
            if (snippet.Length > 80)
                snippet = string.Concat(snippet.AsSpan(0, 77), "...");
            Console.WriteLine(
                $"   [{citation.Marker}] {citation.Score.ToString("F4", CultureInfo.InvariantCulture)}  {citation.SourceId}  \"{snippet}\"");
        }

        Console.WriteLine();
    }

    private static void PrintReport(IngestionReport report)
    {
        Console.WriteLine($"  documents loaded  : {report.DocumentsLoaded}");
        Console.WriteLine($"  chunks created    : {report.ChunksCreated}");
        Console.WriteLine($"  chunks embedded   : {report.ChunksEmbedded}");
        Console.WriteLine(
            $"  sources           : {report.SourcesAdded} added, {report.SourcesUnchanged} unchanged, {report.SourcesReingested} re-ingested");
        Console.WriteLine(
            $"  duration          : {report.Duration.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)}s");
        foreach (var error in report.Errors)
            Console.WriteLine($"  error             : {error}");
        Console.WriteLine();
    }

    private static string ResolveDataDir(string[] args)
    {
        if (args.Length > 0)
            return Path.GetFullPath(args[0]);

        // The csproj copies data/** beside the binary.
        var beside = Path.Combine(AppContext.BaseDirectory, "data");
        if (Directory.Exists(beside))
            return beside;

        // Source layout fallback: bin/Debug/net10.0/ -> ../../../data/
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "data"));
    }

    /// <summary>
    /// Permissive path validator for this standalone example (same pattern as
    /// <c>examples/raggable-tree/basic-indexing</c>): mount rights still apply,
    /// only the extra path-traversal heuristics are relaxed.
    /// </summary>
    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
            PathValidationResult.Allowed(requestedPath);
    }

    /// <summary>
    /// Deterministic <see cref="IChatClient"/> for the offline demo: instead of a
    /// generated answer it returns a fixed notice, and the program prints the
    /// retrieved passages (with scores) that a real LLM would have cited. Replace
    /// this registration with a real chat client (or drop it and configure an LLM
    /// provider) to get generated, grounded answers.
    /// </summary>
    private sealed class OfflineChatClient : IChatClient
    {
        private const string Notice =
            "(offline mode — no LLM configured; showing the retrieved passages instead of a generated answer)";

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
