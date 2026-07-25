using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Minimal typed client for the LanceDB Cloud/Enterprise REST protocol
/// (Lance REST Namespace, <c>https://docs.lancedb.com/api-reference/rest/</c>).
/// Control operations use JSON; data payloads (create/merge-insert) are Arrow IPC
/// streams and query responses are Arrow IPC files, decoded by
/// <see cref="LanceDbArrowCodec"/>. Authentication uses the <c>x-api-key</c> header
/// plus the optional <c>x-lancedb-database</c> header.
/// </summary>
internal sealed class LanceDbRestClient
{
    internal const string ArrowStreamContentType = "application/vnd.apache.arrow.stream";
    internal const string ApiKeyHeader = "x-api-key";
    internal const string DatabaseHeader = "x-lancedb-database";
    internal const string FullTextIndexType = "FTS";

    private const string UriPathSeparator = "/";

    private const int ErrorBodyMaxLength = 500;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        MaxDepth = Orkeon.Domain.Constants.Serialization.SerializationDefaults.JsonMaxDepth
    };

    private readonly HttpClient _http;
    private readonly string _defaultTableName;

    public LanceDbRestClient(HttpClient httpClient, LanceDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TableName);

        _http = httpClient;
        _defaultTableName = options.TableName;
        ConfigureHttpClient(options);
    }

    /// <summary>Builds the REST route prefix of a table (<c>v1/table/{name}</c>).</summary>
    private static string TablePath(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return $"v1/table/{Uri.EscapeDataString(tableName)}";
    }

    /// <summary>Checks whether the default table exists (<c>POST /v1/table/{name}/exists</c>; 404 means absent).</summary>
    public Task<bool> TableExistsAsync(CancellationToken cancellationToken)
        => TableExistsAsync(_defaultTableName, cancellationToken);

    /// <summary>Checks whether <paramref name="tableName"/> exists (<c>POST /v1/table/{name}/exists</c>; 404 means absent).</summary>
    public async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        using var response = await PostJsonAsync($"{TablePath(tableName)}/exists", new { }, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, "TableExists", cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Creates the default table from an Arrow IPC stream payload (<c>POST /v1/table/{name}/create</c>).</summary>
    public Task CreateTableAsync(byte[] arrowStreamPayload, CancellationToken cancellationToken)
        => CreateTableAsync(_defaultTableName, arrowStreamPayload, cancellationToken);

    /// <summary>Creates <paramref name="tableName"/> from an Arrow IPC stream payload (<c>POST /v1/table/{name}/create</c>).</summary>
    public async Task CreateTableAsync(string tableName, byte[] arrowStreamPayload, CancellationToken cancellationToken)
    {
        using var response = await PostArrowAsync($"{TablePath(tableName)}/create", arrowStreamPayload, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "CreateTable", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops <paramref name="tableName"/> entirely (<c>POST /v1/table/{name}/drop</c>; 404 means already absent).</summary>
    public async Task DropTableAsync(string tableName, CancellationToken cancellationToken)
    {
        using var response = await PostJsonAsync($"{TablePath(tableName)}/drop", new { }, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        await EnsureSuccessAsync(response, "DropTable", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates an index on a column of the default table (<c>POST /v1/table/{name}/create_index</c>), e.g. an FTS index.</summary>
    public async Task CreateIndexAsync(string column, string indexType, CancellationToken cancellationToken)
    {
        var body = new { column, index_type = indexType };
        using var response = await PostJsonAsync($"{TablePath(_defaultTableName)}/create_index", body, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "CreateIndex", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Upserts rows into the default table, keyed on the <c>id</c> column
    /// (<c>POST /v1/table/{name}/merge_insert?on=id&amp;when_matched_update_all=true&amp;when_not_matched_insert_all=true</c>).
    /// </summary>
    public Task MergeInsertAsync(byte[] arrowStreamPayload, CancellationToken cancellationToken)
        => MergeInsertAsync(_defaultTableName, arrowStreamPayload, cancellationToken);

    /// <summary>
    /// Upserts rows into <paramref name="tableName"/>, keyed on the <c>id</c> column
    /// (<c>POST /v1/table/{name}/merge_insert?on=id&amp;when_matched_update_all=true&amp;when_not_matched_insert_all=true</c>).
    /// </summary>
    public async Task MergeInsertAsync(string tableName, byte[] arrowStreamPayload, CancellationToken cancellationToken)
    {
        var uri = $"{TablePath(tableName)}/merge_insert?on={Uri.EscapeDataString(LanceDbArrowCodec.IdColumn)}" +
                  "&when_matched_update_all=true&when_not_matched_insert_all=true";
        using var response = await PostArrowAsync(uri, arrowStreamPayload, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "MergeInsert", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the default-table rows matching a SQL predicate (<c>POST /v1/table/{name}/delete</c>).</summary>
    public Task DeleteAsync(string predicate, CancellationToken cancellationToken)
        => DeleteRowsAsync(_defaultTableName, predicate, cancellationToken);

    /// <summary>Deletes the rows of <paramref name="tableName"/> matching a SQL predicate (<c>POST /v1/table/{name}/delete</c>).</summary>
    public async Task DeleteRowsAsync(string tableName, string predicate, CancellationToken cancellationToken)
    {
        using var response = await PostJsonAsync($"{TablePath(tableName)}/delete", new { predicate }, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Delete", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Counts the default-table rows (<c>POST /v1/table/{name}/count_rows</c>; the body is a plain integer).</summary>
    public async Task<long> CountRowsAsync(CancellationToken cancellationToken)
    {
        using var response = await PostJsonAsync($"{TablePath(_defaultTableName)}/count_rows", new { }, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "CountRows", cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseCount(body);
    }

    /// <summary>
    /// Executes a query against the default table (<c>POST /v1/table/{name}/query</c>) —
    /// vector search, full-text search and/or SQL filtering run server-side — and decodes
    /// the Arrow response.
    /// </summary>
    public Task<IReadOnlyList<LanceDbQueryRow>> QueryAsync(LanceDbQueryRequest request, CancellationToken cancellationToken)
        => QueryAsync(_defaultTableName, request, cancellationToken);

    /// <summary>
    /// Executes a query against <paramref name="tableName"/> (<c>POST /v1/table/{name}/query</c>) —
    /// vector search, full-text search and/or SQL filtering run server-side — and decodes
    /// the Arrow response.
    /// </summary>
    public async Task<IReadOnlyList<LanceDbQueryRow>> QueryAsync(string tableName, LanceDbQueryRequest request, CancellationToken cancellationToken)
    {
        using var response = await PostJsonAsync($"{TablePath(tableName)}/query", request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Query", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return LanceDbArrowCodec.DecodeQueryResponse(payload);
    }

    private void ConfigureHttpClient(LanceDbOptions options)
    {
        if (_http.BaseAddress is null)
        {
            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                throw new InvalidOperationException(
                    "LanceDB endpoint is not configured. Set LanceDbOptions.Endpoint to the base URL of the " +
                    "LanceDB Cloud/Enterprise REST deployment (e.g. https://my-deployment.us-east-1.api.lancedb.com).");
            }

            _http.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + UriPathSeparator);
        }

        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            _http.DefaultRequestHeaders.Remove(ApiKeyHeader);
            _http.DefaultRequestHeaders.Add(ApiKeyHeader, options.ApiKey);
        }

        if (!string.IsNullOrEmpty(options.Database))
        {
            _http.DefaultRequestHeaders.Remove(DatabaseHeader);
            _http.DefaultRequestHeaders.Add(DatabaseHeader, options.Database);
        }
    }

    // CA2000: ownership of the content transfers to HttpClient.PostAsync, which sends and
    // releases it; disposing it here would prematurely free the request body still observed
    // by callers and message handlers.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Request content ownership is transferred to HttpClient.PostAsync.")]
    private async Task<HttpResponseMessage> PostJsonAsync(string uri, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, body.GetType(), s_jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(new Uri(uri, UriKind.Relative), content, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Request content ownership is transferred to HttpClient.PostAsync.")]
    private async Task<HttpResponseMessage> PostArrowAsync(string uri, byte[] payload, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ArrowStreamContentType);
        return await _http.PostAsync(new Uri(uri, UriKind.Relative), content, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = DescribeError(body);

        throw new HttpRequestException(
            $"LanceDB {operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {detail}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private static string DescribeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "no response body";

        try
        {
            var error = JsonSerializer.Deserialize<LanceDbErrorResponse>(body, s_jsonOptions);
            if (error is not null && (!string.IsNullOrEmpty(error.Error) || !string.IsNullOrEmpty(error.Detail)))
            {
                var message = string.IsNullOrEmpty(error.Error) ? error.Detail : error.Error;
                var detail = string.IsNullOrEmpty(error.Error) || string.IsNullOrEmpty(error.Detail)
                    ? ""
                    : $" — {error.Detail}";
                return $"{message}{detail} (lance error code {error.Code})";
            }
        }
        catch (JsonException)
        {
            // Not a JSON error envelope; fall through to the raw snippet.
        }

        return body.Length <= ErrorBodyMaxLength ? body : body[..ErrorBodyMaxLength];
    }

    private static long ParseCount(string body)
    {
        var trimmed = body.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain))
            return plain;

        // Tolerate non-REST-namespace servers replying with a JSON number or object.
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Number && root.TryGetInt64(out var number))
                return number;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("count", out var count) &&
                count.TryGetInt64(out var objectCount))
            {
                return objectCount;
            }
        }
        catch (JsonException)
        {
            // Fall through to the error below.
        }

        throw new FormatException($"Unexpected LanceDB count_rows response body: '{Truncate(trimmed)}'.");
    }

    private static string Truncate(string value)
        => value.Length <= ErrorBodyMaxLength ? value : value[..ErrorBodyMaxLength];
}
