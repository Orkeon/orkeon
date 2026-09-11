using System.Dynamic;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>rag</c> namespace on a Jint engine (RAG-03/C3, plan §8.4):
/// first-class <c>rag.ingest({ collection, sources, chunkingStrategy?, reindex? })</c>,
/// <c>rag.query(question, { collection, profile?, topN? })</c> and
/// <c>rag.retrieve(question, { … })</c> over the RAG subsystem pipelines. The
/// namespace is always registered — calls fail loudly with an actionable message
/// when the host did not wire the RAG subsystem (<c>AddOrkeonRag(configuration)</c>).
/// <para><c>query</c> and <c>retrieve</c> return the SAME payload shape; only
/// <c>query</c> runs the generation stage, so <c>retrieve</c> comes back with an
/// empty <c>text</c> and costs no LLM call. A script that reads only
/// <c>citations</c> — the common case when the evidence itself must be quoted —
/// should call <c>retrieve</c>.</para>
/// </summary>
public static partial class RagNamespaceBinding
{
    /// <summary>Name of the global namespace exposed to scripts.</summary>
    public const string GlobalName = "rag";

    /// <summary>
    /// Adds <c>rag</c> to <paramref name="engine"/>'s global scope.
    /// </summary>
    /// <param name="engine">Jint engine receiving the binding.</param>
    /// <param name="backend">Pipelines + VFS backing the namespace; null when the host has no RAG subsystem (calls then fail loudly).</param>
    /// <param name="logger">Diagnostic sink; defaults to <see cref="NullLogger.Instance"/>.</param>
    public static void Register(
        Engine engine,
        RagScriptingBackend? backend = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var log = logger ?? NullLogger.Instance;

        IDictionary<string, object?> ns = new ExpandoObject();
        ns["ingest"] = BuildIngest(backend);
        ns["query"] = BuildQuery(backend, log, generate: true);
        ns["retrieve"] = BuildQuery(backend, log, generate: false);
        engine.SetValue(GlobalName, ns);
    }

    private static Func<JsValue?, Task<object>> BuildIngest(RagScriptingBackend? backend)
    {
        return async options =>
        {
            if (backend is null)
            {
                throw new InvalidOperationException(
                    "rag.ingest: the RAG subsystem is not configured on this host. " +
                    "Register it with AddOrkeonRag(configuration) (Orkeon.Rag.DependencyInjection).");
            }

            if (options is null || !options.IsObject())
                throw new ArgumentException("rag.ingest expects an options object: { collection, sources[, chunkingStrategy, reindex] }.");

            var obj = options.AsObject();
            var collection = GetRequiredString(obj, "collection", "rag.ingest");
            var locations = ReadSources(obj);
            if (locations.Count == 0)
                throw new ArgumentException("rag.ingest: 'sources' must be a non-empty array of paths or glob patterns.");

            var chunking = GetOptionalString(obj, "chunkingStrategy");
            var reindexValue = obj.Get("reindex");
            var reindex = reindexValue.IsBoolean() && reindexValue.AsBoolean();

            IReadOnlyList<SourceDescriptor> sources = locations.Any(SourceGlobExpander.HasWildcard)
                ? await SourceGlobExpander.ExpandAsync(backend.FileSystem, locations).ConfigureAwait(false)
                : locations.Select(l => new SourceDescriptor { Location = l }).ToList();

            if (sources.Count == 0)
                throw new InvalidOperationException($"rag.ingest: no source matched: {string.Join(", ", locations)}");

            var report = await backend.IngestionPipeline.IngestAsync(new IngestionRequest
            {
                Collection = collection,
                Sources = [.. sources],
                ChunkingStrategy = chunking,
                Reindex = reindex,
            }).ConfigureAwait(false);

            var payload = new Dictionary<string, object?>
            {
                ["collection"] = report.Collection,
                ["documentsLoaded"] = report.DocumentsLoaded,
                ["chunksCreated"] = report.ChunksCreated,
                ["chunksEmbedded"] = report.ChunksEmbedded,
                ["chunksSkipped"] = report.ChunksSkipped,
                ["sourcesAdded"] = report.SourcesAdded,
                ["sourcesUnchanged"] = report.SourcesUnchanged,
                ["sourcesReingested"] = report.SourcesReingested,
                ["durationMs"] = report.Duration.TotalMilliseconds,
                ["errors"] = report.Errors.ToArray(),
            };
            // A CLR payload, converted by Jint on its own event loop: this continuation is on
            // a thread-pool thread, where the engine must not be touched.
            return payload;
        };
    }

    /// <summary>
    /// Builds <c>rag.query</c> (<paramref name="generate"/> true) or
    /// <c>rag.retrieve</c> (false). The two surfaces share every stage but the
    /// last: retrieval, fusion, reranking and assembly are identical, and only
    /// <c>query</c> pays for a grounded generation on top.
    /// </summary>
    private static Func<JsValue?, JsValue?, Task<object>> BuildQuery(
        RagScriptingBackend? backend, ILogger log, bool generate)
    {
        var surface = generate ? "rag.query" : "rag.retrieve";
        return async (question, options) =>
        {
            if (backend is null)
            {
                throw new InvalidOperationException(
                    $"{surface}: the RAG subsystem is not configured on this host. " +
                    "Register it with AddOrkeonRag(configuration) (Orkeon.Rag.DependencyInjection).");
            }

            var text = ReadQuestion(question, surface);
            var obj = ReadOptions(options, surface);
            // Read in this order on purpose: the collection error must win over anything
            // the profile lookup can raise, exactly as it did before this was split up.
            var collection = GetRequiredString(obj, "collection", surface);
            var query = BuildRagQuery(obj, text, collection);
            var pipeline = ResolvePipeline(backend, obj, log, surface);
            var answer = await RunPipelineAsync(pipeline, query, generate).ConfigureAwait(false);
            return BuildAnswerPayload(answer);
        };
    }

    private static string ReadQuestion(JsValue? question, string surface)
    {
        if (question is null || !question.IsString() || string.IsNullOrWhiteSpace(question.AsString()))
            throw new ArgumentException($"{surface} expects a non-empty question string as its first argument.");
        return question.AsString();
    }

    private static Jint.Native.Object.ObjectInstance ReadOptions(JsValue? options, string surface)
    {
        if (options is null || !options.IsObject())
            throw new ArgumentException($"{surface} expects an options object: {{ collection[, profile, topN] }}.");
        return options.AsObject();
    }

    private static RagQuery BuildRagQuery(
        Jint.Native.Object.ObjectInstance obj, string text, string collection)
    {
        var query = new RagQuery
        {
            Text = text,
            Collection = collection,
        };

        var topNValue = obj.Get("topN");
        return topNValue.IsNumber() ? query with { TopN = (int)topNValue.AsNumber() } : query;
    }

    /// <summary>
    /// Picks the pipeline serving this call: the host-wide one, or the one the
    /// per-call <c>profile</c> option names. Without a resolver on the host we
    /// FAIL rather than ignore: a script that asked for `corrective` and silently
    /// got `fast` would produce answers whose provenance it cannot describe,
    /// which is worse than an error.
    /// </summary>
    private static IRagPipeline ResolvePipeline(
        RagScriptingBackend backend, Jint.Native.Object.ObjectInstance obj, ILogger log, string surface)
    {
        var profile = GetOptionalString(obj, "profile");
        if (profile is null)
            return backend.RagPipeline;

        if (backend.ProfileResolver is null)
        {
            throw new InvalidOperationException(
                $"{surface}: profile '{profile}' was requested but this host registered no "
                + "IRagProfileResolver, so the profile cannot be honoured. Either drop the "
                + "option (the host-wide Orkeon:Rag:Profile then applies) or register the RAG "
                + "subsystem with AddOrkeonRag(configuration), which provides the resolver.");
        }

        IRagPipeline pipeline;
        try
        {
            pipeline = backend.ProfileResolver.Resolve(profile);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException
                                   or InvalidOperationException)
        {
            throw new ArgumentException(
                $"{surface}: unknown retrieval profile '{profile}'. Expected one of "
                + $"{RagProfilePresets.FastName}, {RagProfilePresets.BalancedName}, "
                + $"{RagProfilePresets.QualityName}, {RagProfilePresets.CorrectiveName}, "
                + $"{RagProfilePresets.AdaptiveName}.", ex);
        }

        LogProfileResolved(log, profile);
        return pipeline;
    }

    private static async Task<RagAnswer> RunPipelineAsync(
        IRagPipeline pipeline, RagQuery query, bool generate)
    {
        if (generate)
            return await pipeline.QueryAsync(query).ConfigureAwait(false);

        if (pipeline is IRagRetrievalCapable retriever)
            return await retriever.RetrieveAsync(query).ConfigureAwait(false);

        // Retrieval without generation is refused rather than quietly served by the
        // generating surface. A caller reaches for the retrieve entry point precisely
        // to avoid paying for an answer it will discard, so generating anyway would
        // charge it exactly what it asked to avoid, with no way for it to notice.
        throw new NotSupportedException(
            $"rag.retrieve: the resolved pipeline ({pipeline.GetType().Name}) does not implement "
            + "IRagRetrievalCapable, so retrieval cannot be run without generation. Use rag.query, "
            + "or select a profile served by the staged pipeline (fast/balanced/quality).");
    }

    private static Dictionary<string, object?> BuildAnswerPayload(RagAnswer answer) => new()
    {
        ["text"] = answer.Text,
        ["citations"] = answer.Citations.Select(citation => new Dictionary<string, object?>
        {
            ["marker"] = citation.Marker,
            ["chunkId"] = citation.ChunkId,
            ["sourceId"] = citation.SourceId,
            ["documentId"] = citation.DocumentId,
            ["snippet"] = citation.Snippet,
            ["score"] = citation.Score,
        }).ToArray(),
    };

    private static string GetRequiredString(Jint.Native.Object.ObjectInstance obj, string key, string surface)
    {
        var value = obj.Get(key);
        if (!value.IsString() || string.IsNullOrWhiteSpace(value.AsString()))
            throw new ArgumentException($"{surface}: '{key}' is required and must be a non-empty string.");
        return value.AsString();
    }

    private static string? GetOptionalString(Jint.Native.Object.ObjectInstance obj, string key)
    {
        var value = obj.Get(key);
        return value.IsString() && !string.IsNullOrWhiteSpace(value.AsString()) ? value.AsString() : null;
    }

    private static List<string> ReadSources(Jint.Native.Object.ObjectInstance obj)
    {
        var sources = new List<string>();
        var value = obj.Get("sources");
        if (value.IsString())
        {
            if (!string.IsNullOrWhiteSpace(value.AsString()))
                sources.Add(value.AsString());
            return sources;
        }

        if (!value.IsArray())
            return sources;

        foreach (var item in value.AsArray())
        {
            if (item.IsString() && !string.IsNullOrWhiteSpace(item.AsString()))
                sources.Add(item.AsString());
        }

        return sources;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "rag.query: resolved retrieval profile '{Profile}' for this call.")]
    static partial void LogProfileResolved(ILogger logger, string profile);
}
