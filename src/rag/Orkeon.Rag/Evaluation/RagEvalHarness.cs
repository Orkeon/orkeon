using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Evaluation;

/// <summary>Everything one evaluation run needs: dataset path, profiles, judge, outputs.</summary>
public sealed record RagEvalRunRequest
{
    /// <summary>Virtual path of the dataset YAML file (e.g. <c>/workspace/examples/rag/eval/golden.yaml</c>).</summary>
    public required string DatasetPath { get; init; }

    /// <summary>
    /// Profiles to evaluate — one report per profile, resolved through
    /// <see cref="IRagProfileResolver"/>. Empty runs the single
    /// <see cref="RagEvalOptions.DefaultProfile"/>.
    /// </summary>
    public ImmutableList<string> Profiles { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Collection override; <c>null</c> uses the dataset default (or <c>rag-eval-{name}</c>).</summary>
    public string? Collection { get; init; }

    /// <summary>Metric cutoff (recall@K / precision@K). Defaults to 5.</summary>
    public int K { get; init; } = 5;

    /// <summary>Requests the LLM judge (honoured only when a chat client is available — see <see cref="RagEvalOptions.UseLlmJudge"/>).</summary>
    public bool UseLlmJudge { get; init; }

    /// <summary>Ingests the dataset's corpus (incremental) before evaluating. Defaults to <c>true</c>.</summary>
    public bool IngestCorpus { get; init; } = true;

    /// <summary>
    /// Forces a full corpus reindex. Needed when the incremental manifest survived
    /// but the backing store did not (e.g. default in-memory store across CLI
    /// processes), or after an embedding model change.
    /// </summary>
    public bool ReindexCorpus { get; init; }

    /// <summary>Virtual directory receiving the markdown/JSON reports.</summary>
    public string OutputDirectory { get; init; } = "/output/rag/eval";
}

/// <summary>Outcome of a harness run: the reports plus the files written.</summary>
public sealed record RagEvalRunResult
{
    /// <summary>The loaded dataset.</summary>
    public required RagEvalDataset Dataset { get; init; }

    /// <summary>Collection that was evaluated.</summary>
    public required string Collection { get; init; }

    /// <summary>One report per evaluated profile, in request order.</summary>
    public ImmutableList<RagEvalReport> Reports { get; init; } = ImmutableList<RagEvalReport>.Empty;

    /// <summary>Virtual paths of the markdown/JSON reports written.</summary>
    public ImmutableList<string> WrittenFiles { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Virtual path of the multi-profile comparison table (<c>null</c> for single-profile runs).</summary>
    public string? ComparisonFile { get; init; }

    /// <summary>Ingestion report of the corpus pre-pass (<c>null</c> when skipped).</summary>
    public IngestionReport? Ingestion { get; init; }
}

/// <summary>
/// One-call evaluation harness shared by the CLI (<c>orkeon rag eval</c>) and the
/// agent tool (<c>rag_eval</c>): loads the dataset, ingests its corpus
/// (incremental — unchanged sources are skipped), evaluates each requested
/// profile, and writes the markdown/JSON reports (plus the comparison table for
/// multi-profile runs) through the VFS.
/// </summary>
public interface IRagEvalHarness
{
    /// <summary>Runs the evaluation described by <paramref name="request"/>.</summary>
    Task<RagEvalRunResult> RunAsync(RagEvalRunRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IRagEvalHarness"/>.</summary>
public sealed partial class RagEvalHarness : IRagEvalHarness
{
    private readonly IRagEvalDatasetLoader _datasetLoader;
    private readonly IRagEvaluator _evaluator;
    private readonly IIngestionPipeline _ingestionPipeline;
    private readonly IFileSystemService _fileSystem;
    private readonly RagEvalReportWriter _reportWriter;
    private readonly ILogger<RagEvalHarness> _logger;

    /// <summary>Initializes the harness.</summary>
    public RagEvalHarness(
        IRagEvalDatasetLoader datasetLoader,
        IRagEvaluator evaluator,
        IIngestionPipeline ingestionPipeline,
        IFileSystemService fileSystem,
        RagEvalReportWriter reportWriter,
        ILogger<RagEvalHarness>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(datasetLoader);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(ingestionPipeline);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(reportWriter);

        _datasetLoader = datasetLoader;
        _evaluator = evaluator;
        _ingestionPipeline = ingestionPipeline;
        _fileSystem = fileSystem;
        _reportWriter = reportWriter;
        _logger = logger ?? NullLogger<RagEvalHarness>.Instance;
    }

    /// <inheritdoc />
    public async Task<RagEvalRunResult> RunAsync(
        RagEvalRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dataset = await _datasetLoader.LoadAsync(request.DatasetPath, cancellationToken)
            .ConfigureAwait(false);
        var collection = request.Collection
            ?? dataset.DefaultCollection
            ?? $"rag-eval-{dataset.Name}";

        IngestionReport? ingestion = null;
        if (request.IngestCorpus && dataset.CorpusPath is not null)
            ingestion = await IngestCorpusAsync(request, dataset, collection, cancellationToken).ConfigureAwait(false);

        var profiles = request.Profiles.Count > 0
            ? request.Profiles
            : [RagEvalOptions.DefaultProfile];

        var reports = ImmutableList.CreateBuilder<RagEvalReport>();
        var files = ImmutableList.CreateBuilder<string>();
        foreach (var profile in profiles)
        {
            var report = await _evaluator.RunAsync(
                dataset,
                new RagEvalOptions
                {
                    Collection = collection,
                    Profile = profile,
                    K = request.K,
                    TopN = Math.Max(5, request.K),
                    UseLlmJudge = request.UseLlmJudge,
                },
                cancellationToken).ConfigureAwait(false);

            reports.Add(report);
            files.AddRange(await _reportWriter.WriteAsync(report, request.OutputDirectory, cancellationToken)
                .ConfigureAwait(false));
        }

        var builtReports = reports.ToImmutable();
        string? comparisonFile = null;
        if (builtReports.Count > 1)
        {
            comparisonFile = await _reportWriter
                .WriteComparisonAsync(builtReports, request.OutputDirectory, cancellationToken)
                .ConfigureAwait(false);
        }

        return new RagEvalRunResult
        {
            Dataset = dataset,
            Collection = collection,
            Reports = builtReports,
            WrittenFiles = files.ToImmutable(),
            ComparisonFile = comparisonFile,
            Ingestion = ingestion,
        };
    }

    private async Task<IngestionReport> IngestCorpusAsync(
        RagEvalRunRequest request,
        RagEvalDataset dataset,
        string collection,
        CancellationToken cancellationToken)
    {
        var corpusDir = ResolveCorpusDirectory(request.DatasetPath, dataset.CorpusPath!);
        var sources = await SourceGlobExpander.ExpandAsync(
            _fileSystem, [$"{corpusDir}/**/*"], cancellationToken).ConfigureAwait(false);

        if (sources.Count == 0)
        {
            throw new InvalidOperationException(
                $"The dataset corpus '{corpusDir}' matched no file — check the 'corpus' entry " +
                $"of '{request.DatasetPath}' and the active mounts.");
        }

        LogCorpusIngestion(corpusDir, sources.Count, collection);
        return await _ingestionPipeline.IngestAsync(
            new IngestionRequest
            {
                Collection = collection,
                Sources = [.. sources],
                Reindex = request.ReindexCorpus,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the dataset's <c>corpus</c> entry: absolute virtual paths pass
    /// through; relative ones resolve against the dataset file's directory.
    /// </summary>
    internal static string ResolveCorpusDirectory(string datasetPath, string corpusPath)
    {
        var corpus = corpusPath.Replace('\\', '/').Trim().TrimEnd('/');
        if (corpus.StartsWith('/'))
            return corpus;

        if (corpus.StartsWith("./", StringComparison.Ordinal))
            corpus = corpus[2..];

        var directory = datasetPath.Replace('\\', '/');
        var slash = directory.LastIndexOf('/');
        directory = slash > 0 ? directory[..slash] : string.Empty;
        return $"{directory}/{corpus}";
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ingesting evaluation corpus '{CorpusDir}' ({SourceCount} file(s)) into collection '{Collection}' (incremental).")]
    private partial void LogCorpusIngestion(string corpusDir, int sourceCount, string collection);
}
