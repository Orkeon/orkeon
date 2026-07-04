using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Base;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Memory provider backed by a remote LanceDB Cloud / Enterprise server.
/// All operations — storage (Arrow IPC merge-insert), retrieval, deletion, counting,
/// vector search and full-text search — are executed server-side through the
/// Lance REST Namespace protocol (<c>https://docs.lancedb.com/api-reference/rest/</c>).
/// </summary>
/// <remarks>
/// CRUD facade only. Search entry points live in <c>LanceDbMemoryProvider.Search.cs</c>,
/// the REST transport in <see cref="LanceDbRestClient"/>, Arrow IPC (de)serialization in
/// <see cref="LanceDbArrowCodec"/>, SQL predicates in <see cref="LanceDbFilterBuilder"/>,
/// DTO mapping in <see cref="LanceDbRecordMapper"/> and logging delegates in
/// <c>LanceDbMemoryProvider.Logging.cs</c>.
/// <para>
/// <see cref="IMemoryProvider"/> is re-listed on purpose (same pattern as
/// <c>SqliteMemoryProvider</c>, R3.2): without re-implementation, calls made through
/// the interface would resolve <c>SearchSimilarAsync</c> to the default interface
/// method (empty results) instead of the server-side vector search defined here.
/// </para>
/// </remarks>
public partial class LanceDbMemoryProvider : MemoryProviderBase, IMemoryProvider, IDisposable
{
    private readonly LanceDbOptions _options;
    private readonly LanceDbRestClient _client;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    /// <inheritdoc />
    public override string Name => "LanceDB";

    /// <summary>Initializes a new instance of <see cref="LanceDbMemoryProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client used for LanceDB REST calls.</param>
    /// <param name="options">LanceDB configuration options (endpoint, API key, table).</param>
    /// <param name="logger">Optional logger.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither <paramref name="httpClient"/>.<see cref="HttpClient.BaseAddress"/>
    /// nor <see cref="LanceDbOptions.Endpoint"/> provides a base URL.
    /// </exception>
    public LanceDbMemoryProvider(
        HttpClient httpClient,
        IOptions<LanceDbOptions> options,
        ILogger<LanceDbMemoryProvider>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _httpClient = httpClient;
        _client = new LanceDbRestClient(httpClient, _options);
    }

    /// <inheritdoc />
    public override async Task InitializeAsync(MemoryProviderConfig config, CancellationToken cancellationToken = default)
    {
        await base.InitializeAsync(config, cancellationToken).ConfigureAwait(false);
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var record = LanceDbRecord.FromMemoryItem(key, item);
            var payload = LanceDbArrowCodec.EncodeRecords([record], _options.EmbeddingDimension);
            await _client.MergeInsertAsync(payload, cancellationToken).ConfigureAwait(false);
            LogStoredMemoryItemWithKey(key);
        }
        catch (Exception ex)
        {
            LogException(ex, "StoreAsync", key);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await _client.QueryAsync(new LanceDbQueryRequest
            {
                Vector = null,
                K = 1,
                Filter = LanceDbFilterBuilder.KeyEquals(key)
            }, cancellationToken).ConfigureAwait(false);

            if (rows.Count == 0)
            {
                LogMemoryItemNotFoundWith(key);
                return null;
            }

            LogRetrievedMemoryItemWithKey(key);
            return rows[0].Record.ToMemoryItem();
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAsync", key);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!await KeyExistsAsync(key, cancellationToken).ConfigureAwait(false))
            {
                LogCannotUpdateNonExistentMemory(key);
                return false;
            }

            var record = LanceDbRecord.FromMemoryItem(key, item);
            var payload = LanceDbArrowCodec.EncodeRecords([record], _options.EmbeddingDimension);
            await _client.MergeInsertAsync(payload, cancellationToken).ConfigureAwait(false);
            LogUpdatedMemoryItemWithKey(key);
            return true;
        }
        catch (Exception ex)
        {
            LogException(ex, "UpdateAsync", key);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // The REST delete endpoint reports a commit version, not a deleted-row
            // count, so existence is checked first to preserve the boolean contract.
            var removed = await KeyExistsAsync(key, cancellationToken).ConfigureAwait(false);
            if (removed)
            {
                await _client.DeleteAsync(LanceDbFilterBuilder.KeyEquals(key), cancellationToken).ConfigureAwait(false);
            }

            LogDeletedMemoryItemWithKey(key, removed);
            return removed;
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteAsync", key);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var count = await _client.CountRowsAsync(cancellationToken).ConfigureAwait(false);
            await _client.DeleteAsync(LanceDbFilterBuilder.MatchAllPredicate, cancellationToken).ConfigureAwait(false);
            LogClearedAllMemoryItems((int)count);
        }
        catch (Exception ex)
        {
            LogException(ex, "ClearAsync");
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var count = (int)await _client.CountRowsAsync(cancellationToken).ConfigureAwait(false);
            LogCountedMemoryItems(count);
            return count;
        }
        catch (Exception ex)
        {
            LogException(ex, "CountAsync");
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (take <= 0)
            {
                LogListedMemoryKeys(0, skip, take);
                return [];
            }

            var rows = await _client.QueryAsync(new LanceDbQueryRequest
            {
                Vector = null,
                K = take,
                Offset = skip > 0 ? skip : null,
                Columns = new LanceDbQueryColumns { ColumnNames = [LanceDbArrowCodec.IdColumn] }
            }, cancellationToken).ConfigureAwait(false);

            var keys = rows.Select(r => r.Record.Id).ToList();
            LogListedMemoryKeys(keys.Count, skip, take);
            return keys;
        }
        catch (Exception ex)
        {
            LogException(ex, "ListKeysAsync");
            throw;
        }
    }

    // --- Private helper methods ---

    /// <summary>
    /// Lazily ensures the remote table exists, creating it (with the fixed Arrow schema
    /// and, optionally, the full-text index) on first use.
    /// </summary>
    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            if (!await _client.TableExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                await CreateTableWithSchemaAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
            LogEnsuredTableExists(_options.TableName);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task CreateTableWithSchemaAsync(CancellationToken cancellationToken)
    {
        var emptyTablePayload = LanceDbArrowCodec.EncodeRecords([], _options.EmbeddingDimension);

        try
        {
            await _client.CreateTableAsync(emptyTablePayload, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Another instance may have created the table concurrently.
            if (await _client.TableExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                LogTableCreationRaceLost(_options.TableName);
                return;
            }

            throw;
        }

        if (_options.CreateFullTextIndexOnInit)
        {
            try
            {
                await _client.CreateIndexAsync(
                    LanceDbArrowCodec.ContentColumn,
                    LanceDbRestClient.FullTextIndexType,
                    cancellationToken).ConfigureAwait(false);
                LogCreatedFullTextIndex(LanceDbArrowCodec.ContentColumn, _options.TableName);
            }
            catch (HttpRequestException ex)
            {
                // Surface honestly: SearchAsync will propagate the server error if the
                // index is genuinely missing; CRUD and vector search remain functional.
                LogFullTextIndexCreationFailed(ex, LanceDbArrowCodec.ContentColumn, _options.TableName);
            }
        }
    }

    private async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken)
    {
        var rows = await _client.QueryAsync(new LanceDbQueryRequest
        {
            Vector = null,
            K = 1,
            Filter = LanceDbFilterBuilder.KeyEquals(key),
            Columns = new LanceDbQueryColumns { ColumnNames = [LanceDbArrowCodec.IdColumn] }
        }, cancellationToken).ConfigureAwait(false);

        return rows.Count > 0;
    }

    /// <summary>
    /// Releases the semaphore guarding lazy table initialization and the HTTP client
    /// whose ownership was transferred to this provider.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources held by the provider.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _initLock.Dispose();
            _httpClient.Dispose();
        }
    }
}
