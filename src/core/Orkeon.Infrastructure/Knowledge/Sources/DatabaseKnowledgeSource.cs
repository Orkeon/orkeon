using System.Data.Common;
using Orkeon.Domain.Common;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Knowledge.Sources;

/// <summary>
/// Knowledge source backed by a SQL database.
/// Uses ADO.NET <see cref="DbProviderFactory"/> for provider-agnostic data access.
/// </summary>
public class DatabaseKnowledgeSource : IKnowledgeSource
{
    private readonly DatabaseKnowledgeSourceOptions _options;
    private readonly DbProviderFactory _providerFactory;

    /// <summary>
    /// Creates a new <see cref="DatabaseKnowledgeSource"/>.
    /// </summary>
    /// <param name="options">Database connection and query options.</param>
    /// <param name="providerFactory">
    /// ADO.NET provider factory. When null, defaults to
    /// <see cref="Microsoft.Data.Sqlite.SqliteFactory.Instance"/>.
    /// </param>
    public DatabaseKnowledgeSource(
        DatabaseKnowledgeSourceOptions options,
        DbProviderFactory? providerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("ConnectionString must not be empty.", nameof(options));

        if (string.IsNullOrWhiteSpace(options.Query))
            throw new ArgumentException("Query must not be empty.", nameof(options));

        _providerFactory = providerFactory
            ?? Microsoft.Data.Sqlite.SqliteFactory.Instance;
    }

    private readonly KnowledgeSourceId _id = KnowledgeSourceId.Create();

    /// <inheritdoc />
    public KnowledgeSourceId Id => _id;

    /// <inheritdoc />
    public string Name => $"Database ({_options.ProviderName})";

    /// <inheritdoc />
    public string Type => "database";

    /// <inheritdoc />
    public async Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteQueryAsync(_options.Query, parameters: null, cancellationToken).ConfigureAwait(false);

        var combinedContent = string.Join("\n\n", rows.Select(r => r.Content));

        return new KnowledgeContent
        {
            Id = KnowledgeContentId.Create(),
            Title = Name,
            Content = combinedContent,
            Source = _options.ProviderName,
            Metadata = new Dictionary<string, object>
            {
                ["row_count"] = rows.Count,
                ["source_type"] = Type,
                ["provider"] = _options.ProviderName
            }
        };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KnowledgeContent>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        // If a parameterized search query is configured, use it
        if (!string.IsNullOrWhiteSpace(_options.SearchQuery))
        {
            var parameters = new Dictionary<string, object> { ["@query"] = query };
            var rows = await ExecuteQueryAsync(_options.SearchQuery, parameters, cancellationToken).ConfigureAwait(false);
            return rows.Take(limit);
        }

        // Fall back to in-memory keyword filtering
        var allRows = await ExecuteQueryAsync(_options.Query, parameters: null, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(query))
            return allRows.Take(limit);

        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var scored = allRows
            .Select(row =>
            {
                var content = row.Content;
                int matchCount = queryTerms.Count(term =>
                    content.Contains(term, StringComparison.OrdinalIgnoreCase));

                double relevance = queryTerms.Length > 0
                    ? (double)matchCount / queryTerms.Length
                    : 0.0;

                return (Row: row, Relevance: relevance);
            })
            .Where(x => x.Relevance > 0)
            .OrderByDescending(x => x.Relevance)
            .Take(limit)
            .Select(x => x.Row with { Relevance = x.Relevance });

        return scored;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Executes the operator-configured query (_options.Query/_options.SearchQuery) as the documented core capability of this knowledge source; runtime values are passed via the parameters dictionary as DbParameter objects, not concatenated into the SQL text.")]
    private async Task<List<KnowledgeContent>> ExecuteQueryAsync(
        string sql,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var results = new List<KnowledgeContent>();

        using var connection = _providerFactory.CreateConnection()
            ?? throw new InvalidOperationException("DbProviderFactory returned a null connection.");

        connection.ConnectionString = _options.ConnectionString;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                var param = command.CreateParameter();
                param.ParameterName = name;
                param.Value = value;
                command.Parameters.Add(param);
            }
        }

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        int rowIndex = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var contentOrdinal = reader.GetOrdinal(_options.ContentColumn);
            var content = await reader.IsDBNullAsync(contentOrdinal, cancellationToken).ConfigureAwait(false)
                ? string.Empty
                : reader.GetString(contentOrdinal);

            string? title = null;
            if (_options.TitleColumn != null)
            {
                var titleOrdinal = reader.GetOrdinal(_options.TitleColumn);
                title = await reader.IsDBNullAsync(titleOrdinal, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(titleOrdinal);
            }

            string? id = null;
            if (_options.IdColumn != null)
            {
                var idOrdinal = reader.GetOrdinal(_options.IdColumn);
                id = await reader.IsDBNullAsync(idOrdinal, cancellationToken).ConfigureAwait(false) ? null : reader.GetValue(idOrdinal).ToString();
            }

            results.Add(new KnowledgeContent
            {
                Id = KnowledgeContentId.Create(),
                Title = title ?? $"Row {rowIndex}",
                Content = content,
                Source = _options.ProviderName,
                Metadata = new Dictionary<string, object>
                {
                    ["row_index"] = rowIndex,
                    ["source_type"] = Type,
                    ["provider"] = _options.ProviderName
                }
            });

            rowIndex++;
        }

        return results;
    }
}
