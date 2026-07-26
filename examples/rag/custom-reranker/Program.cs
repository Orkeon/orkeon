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
using Orkeon.Rag.Factories;
using Orkeon.Rag.Reranking;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.Examples.Rag.CustomReranker;

/// <summary>
/// Offline demo of a custom <see cref="IReranker"/> (RAG-04/C3) plugged in
/// through DI: a trivial lexical-bonus reranker is contributed via
/// <see cref="IRerankerRegistrar"/> (the same extension point the opt-in ONNX
/// package uses), selected by configuration (<c>Orkeon:Rag:Rerank:Kind</c>),
/// and exercised through the DI-resolved <see cref="IRagPipeline"/> — showing
/// the CandidateK → TopN cascade and the <c>rerank</c> trace step.
/// </summary>
internal static class Program
{
    private const string Collection = "suncore-docs";

    /// <summary>
    /// The embedding model ranks the generic fault guide first (the question
    /// *sounds* like a fault); the exact log code "R-102" only appears in the
    /// relay self-test page — the lexical bonus flips it to rank [1].
    /// </summary>
    private const string Question = "The inverter maintenance log shows code R-102, what does it mean?";

    private static async Task<int> Main(string[] args)
    {
        var dataDir = ResolveDataDir(args);
        var outputDir = Directory.CreateTempSubdirectory("orkeon-rag-custom-reranker-").FullName;

        Console.WriteLine("RAG custom-reranker — a host-provided IReranker wired by DI (offline, local BGE embeddings)");
        Console.WriteLine();

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(dataDir, "/data", FileAccessRights.ReadOnly),
            new FileSystemMount(outputDir, "/output", FileAccessRights.ReadWrite),
        ]);

        using var offlineChatClient = new OfflineChatClient();
        await using var provider = BuildProvider(registry, offlineChatClient);

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

        // The pipeline comes straight from DI: AddOrkeonRag composed it from the
        // `fast` preset + the configuration overrides below (rerank enabled,
        // kind = lexical-bonus, CandidateK = 4). The reranker itself was
        // contributed by the host through IRerankerRegistrar.
        var rag = provider.GetRequiredService<IRagPipeline>();

        Console.WriteLine($"Q: \"{Question}\"");
        Console.WriteLine();

        var answer = await rag.QueryAsync(new RagQuery
        {
            Text = Question,
            Collection = Collection,
            TopN = 2, // narrow stage of the CandidateK (4) -> TopN (2) cascade
        });

        Console.WriteLine("Reranked result (lexical-bonus):");
        foreach (var citation in answer.Citations)
        {
            Console.WriteLine(
                $"  [{citation.Marker}] {citation.Score.ToString("F4", CultureInfo.InvariantCulture)}  {citation.SourceId}");
        }

        Console.WriteLine();
        Console.WriteLine("Trace:");
        foreach (var step in answer.Trace.Steps)
        {
            var data = string.Join(", ", step.Data.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));
            Console.WriteLine($"  - {step.Name,-9} {(step.Detail is null ? "" : $"({step.Detail}) ")}{data}");
        }

        Console.WriteLine();

        // Control run: the same query WITHOUT reranking (candidates truncated in
        // retrieval order) to make the rank flip visible.
        var noRerank = new Orkeon.Rag.Pipeline.StagedRagPipeline(
            provider.GetRequiredService<IDocumentStore>(),
            provider.GetRequiredService<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>(),
            offlineChatClient,
            Orkeon.Rag.Abstractions.Options.RagProfilePresets.Create(Orkeon.Rag.Abstractions.Options.RagProfile.Fast));

        var control = await noRerank.QueryAsync(new RagQuery { Text = Question, Collection = Collection, TopN = 2 });
        Console.WriteLine("Control (no reranker — cosine order):");
        foreach (var citation in control.Citations)
        {
            Console.WriteLine(
                $"  [{citation.Marker}] {citation.Score.ToString("F4", CultureInfo.InvariantCulture)}  {citation.SourceId}");
        }

        Console.WriteLine();
        Console.WriteLine("  => cosine alone puts the generic fault guide first; the lexical bonus");
        Console.WriteLine("     pins the exact 'R-102' reference page at rank [1].");

        return 0;
    }

    private static ServiceProvider BuildProvider(FileSystemRegistry registry, IChatClient offlineChatClient)
    {
        // Configuration overrides on top of the default `fast` preset: enable the
        // rerank stage and select the host's reranker by name. CandidateK is the
        // wide stage of the cascade (kept tiny here — the corpus has 4 pages).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Rag:Retrieval:CandidateK"] = "4",
                ["Orkeon:Rag:Rerank:Enabled"] = "true",
                ["Orkeon:Rag:Rerank:Kind"] = LexicalBonusReranker.RerankerName,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(sp =>
            new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance));

        services.AddOrkeonLocalEmbeddings();
        services.AddSingleton(offlineChatClient);
        services.AddOrkeonInfrastructure();

        // The host contributes its reranker through the same extension point the
        // opt-in ONNX package uses (AddOrkeonOnnxReranker): an IRerankerRegistrar
        // applied when the singleton RerankerFactory is built — registration
        // order relative to AddOrkeonRag is irrelevant. (And since the factory
        // itself is TryAdd-registered, a host that registers its own
        // RerankerFactory first would win outright.)
        services.AddSingleton<IRerankerRegistrar, LexicalBonusRegistrar>();

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

    /// <summary>Registers <see cref="LexicalBonusReranker"/> on the factory under its name.</summary>
    private sealed class LexicalBonusRegistrar : IRerankerRegistrar
    {
        public void Register(RerankerFactory factory, IServiceProvider serviceProvider) =>
            factory.Register(LexicalBonusReranker.RerankerName, static () => new LexicalBonusReranker());
    }

    /// <summary>Permissive path validator for this standalone example (mount rights still apply).</summary>
    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
            PathValidationResult.Allowed(requestedPath);
    }

    /// <summary>
    /// Deterministic offline <see cref="IChatClient"/> — this demo is about the
    /// rerank cascade, so generation returns a fixed notice.
    /// </summary>
    private sealed class OfflineChatClient : IChatClient
    {
        private const string Notice =
            "(offline mode — no LLM configured; compare the reranked citations below)";

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

/// <summary>
/// Deliberately trivial custom <see cref="IReranker"/> (a lexical bonus): every
/// identifier-like query token (a token carrying a digit, e.g. a log code like
/// <c>R-102</c>, a part number, an RFC number) found verbatim in a chunk adds a
/// flat +1.0 to its retrieval score. Chunks with the exact identifier always
/// outrank chunks that merely *sound* related. A real host would plug a
/// cross-encoder or a richer domain policy here — the wiring is identical.
/// </summary>
public sealed class LexicalBonusReranker : IReranker
{
    /// <summary>Name used by the factory and by <c>Orkeon:Rag:Rerank:Kind</c>.</summary>
    public const string RerankerName = "lexical-bonus";

    private const double IdentifierBonus = 1.0;

    /// <inheritdoc />
    public string Name => RerankerName;

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        var identifiers = Tokens(query).Where(t => t.Any(char.IsAsciiDigit)).ToList();

        IReadOnlyList<ScoredChunk> reranked = candidates
            .Select(candidate =>
            {
                var content = candidate.Chunk.Content;
                var hits = identifiers.Count(id => content.Contains(id, StringComparison.OrdinalIgnoreCase));
                return candidate with
                {
                    Score = candidate.Score + (IdentifierBonus * hits),
                    ScoreOrigin = RerankerName,
                };
            })
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Chunk.Id, StringComparer.Ordinal)
            .Take(topN)
            .ToList();

        return Task.FromResult(reranked);
    }

    /// <summary>Whitespace-delimited tokens, stripped of surrounding punctuation.</summary>
    private static IEnumerable<string> Tokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('.', ',', '!', '?', ':', ';', '(', ')', '"', '\''))
            .Where(token => token.Length > 0);
}
