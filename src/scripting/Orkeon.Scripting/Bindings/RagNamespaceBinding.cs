using System.Dynamic;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>rag</c> namespace on a Jint engine (RAG-03/C3, plan §8.4):
/// first-class <c>rag.ingest({ collection, sources, chunkingStrategy?, reindex? })</c>
/// and <c>rag.query(question, { collection, profile?, topN? })</c> over the RAG
/// subsystem pipelines. The namespace is always registered — calls fail loudly with
/// an actionable message when the host did not wire the RAG subsystem
/// (<c>AddOrkeonRag(configuration)</c>). The <c>profile</c> option is accepted but
/// is a documented no-op until retrieval profiles land (RAG-04).
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
        ns["ingest"] = BuildIngest(engine, backend);
        ns["query"] = BuildQuery(engine, backend, log);
        engine.SetValue(GlobalName, ns);
    }

    private static Func<JsValue?, Task<JsValue>> BuildIngest(
        Engine engine, RagScriptingBackend? backend)
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
            return JsValue.FromObject(engine, payload);
        };
    }

    private static Func<JsValue?, JsValue?, Task<JsValue>> BuildQuery(
        Engine engine, RagScriptingBackend? backend, ILogger log)
    {
        return async (question, options) =>
        {
            if (backend is null)
            {
                throw new InvalidOperationException(
                    "rag.query: the RAG subsystem is not configured on this host. " +
                    "Register it with AddOrkeonRag(configuration) (Orkeon.Rag.DependencyInjection).");
            }

            if (question is null || !question.IsString() || string.IsNullOrWhiteSpace(question.AsString()))
                throw new ArgumentException("rag.query expects a non-empty question string as its first argument.");

            if (options is null || !options.IsObject())
                throw new ArgumentException("rag.query expects an options object: { collection[, profile, topN] }.");

            var obj = options.AsObject();
            var collection = GetRequiredString(obj, "collection", "rag.query");

            var topNValue = obj.Get("topN");
            var topN = topNValue.IsNumber() ? (int)topNValue.AsNumber() : (int?)null;

            // Accepted for forward-compatibility; retrieval profiles land in RAG-04.
            var profile = GetOptionalString(obj, "profile");
            if (profile is not null)
                LogProfileIgnored(log, profile);

            var query = new RagQuery
            {
                Text = question.AsString(),
                Collection = collection,
            };
            if (topN is int n)
                query = query with { TopN = n };

            var answer = await backend.RagPipeline.QueryAsync(query).ConfigureAwait(false);

            var payload = new Dictionary<string, object?>
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
            return JsValue.FromObject(engine, payload);
        };
    }

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
        Message = "rag.query: profile '{Profile}' accepted but ignored — retrieval profiles arrive with RAG-04.")]
    static partial void LogProfileIgnored(ILogger logger, string profile);
}
