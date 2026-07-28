using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag;

/// <summary>
/// Ingestion tool exposed to agents (<c>rag_ingest</c>, RAG-03/C3): loads, chunks,
/// embeds and upserts the given sources into a collection through the RAG
/// subsystem's <see cref="IIngestionPipeline"/>, then reports the incremental
/// counters (added / unchanged / re-ingested). Glob patterns in <c>sources</c>
/// are expanded through the virtual file system when one is available
/// (<see cref="SourceGlobExpander"/>) — same semantics as the CLI and scripting
/// surfaces.
/// </summary>
public class RagIngestTool : IBaseTool
{
    private readonly IIngestionPipeline _ingestionPipeline;
    private readonly IFileSystemService _fileSystem;

    /// <inheritdoc />
    public string Name => "rag_ingest";

    /// <inheritdoc />
    public string Description =>
        "Ingest documents into a knowledge collection so they become searchable via rag_search. " +
        "Accepts file paths and glob patterns; unchanged sources are skipped (incremental).";

    /// <inheritdoc />
    public ToolSchema Schema => new(
        Name: Name,
        Description: Description,
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["collection"] = new ParameterSchema(
                "string",
                "Target collection in the document store",
                Required: true),
            ["sources"] = new ParameterSchema(
                "array",
                "File paths and/or glob patterns to ingest (e.g. '/workspace/docs/**/*.md')",
                Required: true),
            ["chunking_strategy"] = new ParameterSchema(
                "string",
                "Optional chunking strategy name (recursive, sentence, structural, semantic); pipeline default when omitted",
                Required: false),
            ["reindex"] = new ParameterSchema(
                "boolean",
                "Force a full reindex of the collection (required after an embedding model change)",
                Required: false,
                Default: false),
        });

    /// <summary>
    /// Initializes a new instance of <see cref="RagIngestTool"/>. The virtual file
    /// system is a required dependency (VFS rule): it expands glob patterns in
    /// <c>sources</c>; wildcard-free locations pass through to the loaders verbatim.
    /// </summary>
    public RagIngestTool(IIngestionPipeline ingestionPipeline, IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(ingestionPipeline);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _ingestionPipeline = ingestionPipeline;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(
        ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallCoreAsync();

        async Task<ToolCallResponse> CallCoreAsync()
        {
            var collection = request.Parameters.TryGetValue("collection", out var c)
                ? c?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(collection))
                return new ToolCallResponse(false, null, "collection parameter is required");

            var locations = ExtractSourceLocations(
                request.Parameters.TryGetValue("sources", out var s) ? s : null);
            if (locations.Count == 0)
                return new ToolCallResponse(false, null, "sources parameter is required (list of paths or globs)");

            var chunkingStrategy = request.Parameters.TryGetValue("chunking_strategy", out var cs)
                ? cs?.ToString()
                : null;

            var reindex = request.Parameters.TryGetValue("reindex", out var r) && ToBool(r);

            var sources = await ResolveSourcesAsync(locations, cancellationToken).ConfigureAwait(false);
            if (sources.Count == 0)
                return new ToolCallResponse(false, null, $"no source matched: {string.Join(", ", locations)}");

            var report = await _ingestionPipeline.IngestAsync(
                new IngestionRequest
                {
                    Collection = collection!,
                    Sources = [.. sources],
                    ChunkingStrategy = string.IsNullOrWhiteSpace(chunkingStrategy) ? null : chunkingStrategy,
                    Reindex = reindex,
                },
                cancellationToken).ConfigureAwait(false);

            return new ToolCallResponse(true, FormatReport(report), null);
        }
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        // The legacy single-string entrypoint carries no collection/sources structure  —
        // rag_ingest is only meaningful through the structured protocol.
        => Task.FromResult(new ToolResult
        {
            Success = false,
            Error = "rag_ingest requires structured parameters: { collection, sources[, chunking_strategy, reindex] }",
        });

    /// <inheritdoc />
    public bool ValidateInput(string input) => false;

    /// <summary>
    /// Formats an <see cref="IngestionReport"/> as the agent-facing text block:
    /// the incremental source counters, chunk counters, duration, then a
    /// trailing <c>Errors:</c> block when the run was not clean. Public so the
    /// CLI (<c>orkeon rag ingest</c>) prints the exact same report.
    /// </summary>
    public static string FormatReport(IngestionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Ingestion report for collection '{report.Collection}':"));
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"- Sources: {report.SourcesAdded} added, {report.SourcesUnchanged} unchanged, {report.SourcesReingested} re-ingested"));
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"- Documents loaded: {report.DocumentsLoaded}"));
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"- Chunks: {report.ChunksCreated} created, {report.ChunksEmbedded} embedded, {report.ChunksSkipped} skipped"));
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"- Duration: {report.Duration.TotalSeconds:F2}s"));

        if (report.Errors.Count > 0)
        {
            sb.AppendLine("Errors:");
            foreach (var error in report.Errors)
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {error}");
        }

        return sb.ToString();
    }

    private async Task<IReadOnlyList<SourceDescriptor>> ResolveSourcesAsync(
        IReadOnlyList<string> locations, CancellationToken ct)
    {
        if (!locations.Any(SourceGlobExpander.HasWildcard))
            return locations.Select(l => new SourceDescriptor { Location = l }).ToList();

        return await SourceGlobExpander.ExpandAsync(_fileSystem, locations, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tolerant extraction of the <c>sources</c> parameter: a plain string, any
    /// enumerable of values, or a JSON array (the structured tool-calling protocol
    /// hands arrays over as <see cref="JsonElement"/>).
    /// </summary>
    internal static IReadOnlyList<string> ExtractSourceLocations(object? raw)
    {
        var locations = new List<string>();
        switch (raw)
        {
            case null:
                break;
            case string single:
                if (!string.IsNullOrWhiteSpace(single)) locations.Add(single);
                break;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                foreach (var item in array.EnumerateArray())
                {
                    var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) locations.Add(value!);
                }
                break;
            case JsonElement { ValueKind: JsonValueKind.String } str:
                var text = str.GetString();
                if (!string.IsNullOrWhiteSpace(text)) locations.Add(text!);
                break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    var value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) locations.Add(value!);
                }
                break;
            default:
                var fallback = raw.ToString();
                if (!string.IsNullOrWhiteSpace(fallback)) locations.Add(fallback!);
                break;
        }

        return locations;
    }

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.String } s => bool.TryParse(s.GetString(), out var parsed) && parsed,
        string text => bool.TryParse(text, out var parsed) && parsed,
        _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
    };
}
