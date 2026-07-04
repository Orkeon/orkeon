using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// Builds <see cref="LanceDbMemoryProvider"/> instances over a
/// <see cref="FakeHttpMessageHandler"/> and crafts the LanceDB REST responses:
/// plain JSON/text for control endpoints and genuine Arrow IPC file payloads
/// (the wire format of <c>POST /v1/table/{name}/query</c>) for query responses.
/// </summary>
public class LanceDbMemoryProviderTestsFixture
{
    public const string Endpoint = "https://orkeon-tests.us-east-1.api.lancedb.com";
    public const string TableName = "test_memories";
    public const int Dimension = 4;

    private readonly TestLogger<LanceDbMemoryProvider> _logger = new();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The HttpClient is captured by the returned provider and must outlive this factory; it lives for the duration of the test.")]
    public LanceDbMemoryProvider CreateProvider(FakeHttpMessageHandler handler, LanceDbOptions? options = null)
    {
        options ??= DefaultOptions();
        var httpClient = new HttpClient(handler);
        return new LanceDbMemoryProvider(httpClient, Options.Create(options), _logger);
    }

    public static LanceDbOptions DefaultOptions() => new()
    {
        Endpoint = Endpoint,
        ApiKey = TestApiKey,
        TableName = TableName,
        EmbeddingDimension = Dimension,
        DefaultTopK = 10
    };

    public TestLogger<LanceDbMemoryProvider> GetLogger() => _logger;

    // --- Response helpers ---

    /// <summary>An empty 200 response (table exists, create, merge_insert, create_index…).</summary>
    public static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    /// <summary>A JSON response with the given status code.</summary>
    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    /// <summary>A plain-text body, as returned by <c>count_rows</c> (plain integer).</summary>
    public static HttpResponseMessage PlainText(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/plain")
    };

    /// <summary>A 200 response whose body is an Arrow IPC file containing the given rows.</summary>
    public static HttpResponseMessage ArrowRows(params LanceDbTestRow[] rows) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(BuildArrowFile(rows))
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.apache.arrow.file") }
        }
    };

    /// <summary>One row of a crafted query response, with optional server ranking columns.</summary>
    public sealed record LanceDbTestRow(
        string Id,
        string Content,
        float[]? Embedding = null,
        float Importance = 0.5f,
        string Source = "test",
        float? Distance = null,
        float? Score = null);

    /// <summary>
    /// Builds an Arrow IPC <b>file</b> payload (magic <c>ARROW1</c>) shaped like a
    /// LanceDB query response: the storage columns plus <c>_distance</c>/<c>_score</c>
    /// when any row carries them.
    /// </summary>
    public static byte[] BuildArrowFile(IReadOnlyList<LanceDbTestRow> rows, int dimension = Dimension)
    {
        var includeDistance = rows.Any(r => r.Distance.HasValue);
        var includeScore = rows.Any(r => r.Score.HasValue);

        var vectorType = new FixedSizeListType(new Field("item", FloatType.Default, nullable: true), dimension);
        var schemaBuilder = new Schema.Builder()
            .Field(f => f.Name("id").DataType(StringType.Default).Nullable(false))
            .Field(f => f.Name("content").DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name("vector").DataType(vectorType).Nullable(true))
            .Field(f => f.Name("importance").DataType(FloatType.Default).Nullable(true))
            .Field(f => f.Name("source").DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name("tags").DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name("created_at").DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name("metadata_json").DataType(StringType.Default).Nullable(true));

        if (includeDistance)
            schemaBuilder.Field(f => f.Name("_distance").DataType(FloatType.Default).Nullable(true));
        if (includeScore)
            schemaBuilder.Field(f => f.Name("_score").DataType(FloatType.Default).Nullable(true));

        var schema = schemaBuilder.Build();

        var ids = new StringArray.Builder();
        var contents = new StringArray.Builder();
        var importances = new FloatArray.Builder();
        var sources = new StringArray.Builder();
        var tags = new StringArray.Builder();
        var createdAts = new StringArray.Builder();
        var metadata = new StringArray.Builder();
        var distances = new FloatArray.Builder();
        var scores = new FloatArray.Builder();

        var vectorValues = new FloatArray.Builder();
        var vectorValidity = new ArrowBuffer.BitmapBuilder();
        var vectorNullCount = 0;

        foreach (var row in rows)
        {
            ids.Append(row.Id);
            contents.Append(row.Content);
            importances.Append(row.Importance);
            sources.Append(row.Source);
            tags.AppendNull();
            createdAts.Append(DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            metadata.AppendNull();

            if (row.Embedding is { Length: > 0 })
            {
                foreach (var value in row.Embedding)
                    vectorValues.Append(value);
                vectorValidity.Append(true);
            }
            else
            {
                for (var i = 0; i < dimension; i++)
                    vectorValues.Append(0f);
                vectorValidity.Append(false);
                vectorNullCount++;
            }

            if (includeDistance)
            {
                if (row.Distance is { } distance) distances.Append(distance);
                else distances.AppendNull();
            }

            if (includeScore)
            {
                if (row.Score is { } score) scores.Append(score);
                else scores.AppendNull();
            }
        }

        var vectorArray = new FixedSizeListArray(
            vectorType, rows.Count, vectorValues.Build(), vectorValidity.Build(), vectorNullCount);

        var arrays = new List<IArrowArray>
        {
            ids.Build(),
            contents.Build(),
            vectorArray,
            importances.Build(),
            sources.Build(),
            tags.Build(),
            createdAts.Build(),
            metadata.Build()
        };

        if (includeDistance)
            arrays.Add(distances.Build());
        if (includeScore)
            arrays.Add(scores.Build());

        using var batch = new RecordBatch(schema, arrays, rows.Count);

        using var stream = new MemoryStream();
        using (var writer = new ArrowFileWriter(stream, schema, leaveOpen: true))
        {
            writer.WriteRecordBatch(batch);
            writer.WriteEnd();
        }

        return stream.ToArray();
    }
}
