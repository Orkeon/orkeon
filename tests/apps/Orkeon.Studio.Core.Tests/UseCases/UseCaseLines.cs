using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Studio.Core.Tests.UseCases;

/// <summary>
/// Protocol lines of <c>orkeon usecases --events jsonl</c>, spelled the way the CLI's
/// <c>UseCaseEventWriter</c> writes them: the envelope first, the payload flat beside it.
/// </summary>
internal static class UseCaseLines
{
    private static int _sequence;

    /// <summary>One sheet, under the manifest's field names.</summary>
    public static JsonObject Sheet(
        string id,
        string category,
        string process = "sequential",
        string? titleFr = null,
        string? titleEn = null,
        string? problemFr = null,
        string? problemEn = null,
        string? problemZh = null,
        bool requiresNetwork = false,
        string[]? requiresKeys = null,
        bool importable = true,
        string[]? tags = null) => new()
    {
        ["agents"] = 2,
        ["category"] = category,
        ["format"] = "yaml",
        ["hasSampleData"] = false,
        ["id"] = id,
        ["importable"] = importable,
        ["mounts"] = new JsonArray(),
        ["number"] = 1,
        ["problem"] = Texts(problemFr, problemEn, problemZh),
        ["process"] = process,
        ["requiresKeys"] = new JsonArray([.. (requiresKeys ?? []).Select(key => (JsonNode?)key)]),
        ["requiresNetwork"] = requiresNetwork,
        ["tags"] = new JsonArray([.. (tags ?? []).Select(tag => (JsonNode?)tag)]),
        ["tasks"] = 2,
        ["title"] = Texts(titleFr, titleEn, null),
        ["tools"] = new JsonArray("file_read"),
    };

    /// <summary>The <c>usecases.catalog</c> line of <paramref name="sheets"/>.</summary>
    public static string Catalog(params JsonObject[] sheets) => Line("usecases.catalog", null, new JsonObject
    {
        ["count"] = sheets.Length,
        ["languages"] = new JsonArray("fr", "en", "es", "de", "zh-Hans"),
        ["useCases"] = new JsonArray([.. sheets]),
    });

    /// <summary>The <c>usecases.ready</c> line a session opens with.</summary>
    public static string Ready(int count) => Line("usecases.ready", null, new JsonObject
    {
        ["count"] = count,
        ["languages"] = new JsonArray("fr", "en", "es", "de", "zh-Hans"),
        ["modes"] = new JsonObject { ["fr"] = "bm25", ["en"] = "hybrid" },
    });

    /// <summary>One result of an answer.</summary>
    public static JsonObject Result(int rank, string id, string reason, params string[] terms) => new()
    {
        ["rank"] = rank,
        ["id"] = id,
        ["score"] = 1.5 / rank,
        ["reason"] = reason,
        ["terms"] = new JsonArray([.. terms.Select(term => (JsonNode?)term)]),
    };

    /// <summary>The <c>usecases.results</c> line answering the query <paramref name="correlationId"/>.</summary>
    public static string Results(string? correlationId, string query, params JsonObject[] results) =>
        Line("usecases.results", correlationId, new JsonObject
        {
            ["query"] = query,
            ["lang"] = "fr",
            ["langSource"] = "detected",
            ["mode"] = "bm25",
            ["degraded"] = null,
            ["results"] = new JsonArray([.. results]),
        });

    /// <summary>An <c>error</c> line; with a correlation id it answers that query.</summary>
    public static string Error(string code, string message, string? correlationId = null, bool recoverable = true) =>
        Line("error", correlationId, new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
            ["recoverable"] = recoverable,
        });

    /// <summary>The correlation id of an inbound query line.</summary>
    public static string CorrelationIdOf(string queryLine) =>
        JsonNode.Parse(queryLine)!["correlationId"]!.GetValue<string>();

    /// <summary>The text of an inbound query line.</summary>
    public static string TextOf(string queryLine) =>
        JsonNode.Parse(queryLine)!["text"]!.GetValue<string>();

    private static JsonObject Texts(string? fr, string? en, string? zh)
    {
        var texts = new JsonObject();
        if (fr is not null)
            texts["fr"] = fr;
        if (en is not null)
            texts["en"] = en;
        if (zh is not null)
            texts["zh-Hans"] = zh;
        return texts;
    }

    private static string Line(string kind, string? correlationId, JsonObject payload)
    {
        var line = new JsonObject
        {
            ["v"] = 2,
            ["seq"] = Interlocked.Increment(ref _sequence),
            ["ts"] = "2026-09-24T10:00:00Z",
            ["kind"] = kind,
        };
        if (correlationId is not null)
            line["correlationId"] = correlationId;

        foreach (var property in payload.ToList())
        {
            payload.Remove(property.Key);
            if (property.Value is not null)
                line[property.Key] = property.Value;
        }

        return line.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
