using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Tool for executing Cypher or SQL queries on ArcadeDB via the Bolt protocol.
/// Uses the Neo4j.Driver to communicate with ArcadeDB's Bolt-compatible endpoint.
/// </summary>
[ToolContract("arcadedb_query",
    Name = "arcadedb_query",
    Description = "Execute Cypher or SQL queries on ArcadeDB via Bolt protocol",
    Category = "Data Operations")]
public partial class ArcadeDbTool : ToolBase<ArcadeDbRequest, ArcadeDbResponse>
{
    [GeneratedRegex(@"\bDELETE\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 5000)]
    private static partial Regex DeleteRegex();

    [GeneratedRegex(@"\bWHERE\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 5000)]
    private static partial Regex WhereRegex();

    private static readonly HashSet<string> BlockedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DROP", "GRANT", "REVOKE"
    };

    private readonly IDriver? _injectedDriver;

    /// <summary>
    /// Initializes a new instance of <see cref="ArcadeDbTool"/> with an optional injected driver for testability.
    /// </summary>
    /// <param name="driver">Optional Neo4j driver instance. If null, a driver will be created from the request's BoltUri.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ArcadeDbTool(IDriver? driver = null, ILogger<ArcadeDbTool>? logger = null) : base(logger)
    {
        _injectedDriver = driver;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ArcadeDbRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BoltUri is null)
            return "BoltUri cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Database))
            return "Database cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        var trimmedQuery = request.Query.TrimStart();

        // Block destructive keywords (DROP, GRANT, REVOKE)
        foreach (var keyword in BlockedKeywords)
        {
            if (trimmedQuery.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase) ||
                trimmedQuery.StartsWith(keyword + "\t", StringComparison.OrdinalIgnoreCase) ||
                trimmedQuery.StartsWith(keyword + "\n", StringComparison.OrdinalIgnoreCase) ||
                trimmedQuery.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return $"Destructive operation '{keyword}' is not allowed";
            }
        }

        // Block DELETE without WHERE clause anywhere in the query
        if (DeleteRegex().IsMatch(trimmedQuery) && !WhereRegex().IsMatch(trimmedQuery))
            return "DELETE without WHERE clause is not allowed";

        return null;
    }

    /// <inheritdoc />
    protected override Task<ArcadeDbResponse> ExecuteTypedAsync(
        ArcadeDbRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<ArcadeDbResponse> ExecuteTypedCoreAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            // ownedDriver is non-null only when this method creates the driver, so the
            // finally block can dispose it unconditionally (the injected driver is owned
            // by the caller and must not be disposed here).
            var ownedDriver = _injectedDriver is null ? CreateDriver(request) : null;
            var driver = _injectedDriver ?? ownedDriver!;

            try
            {
                var session = driver.AsyncSession(builder => builder.WithDatabase(request.Database));

                try
                {
                    var parameters = request.Parameters as IDictionary<string, object>;
                    var cursor = parameters != null
                        ? await session.RunAsync(request.Query, parameters).ConfigureAwait(false)
                        : await session.RunAsync(request.Query).ConfigureAwait(false);

                    var records = new List<Dictionary<string, object>>();
                    while (await cursor.FetchAsync().ConfigureAwait(false) && records.Count < request.MaxResults)
                    {
                        var record = new Dictionary<string, object>();
                        foreach (var kvp in cursor.Current.Values)
                        {
                            record[kvp.Key] = ConvertValue(kvp.Value);
                        }
                        records.Add(record);
                    }

                    var summary = await cursor.ConsumeAsync().ConfigureAwait(false);
                    stopwatch.Stop();

                    var counters = summary.Counters;

                    LogQueryCompleted(records.Count, counters.NodesCreated, stopwatch.ElapsedMilliseconds);

                    return new ArcadeDbResponse
                    {
                        Records = records,
                        RecordCount = records.Count,
                        NodesCreated = counters.NodesCreated,
                        RelationshipsCreated = counters.RelationshipsCreated,
                        PropertiesSet = counters.PropertiesSet,
                        QueryLanguage = request.QueryLanguage,
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }
                finally
                {
                    await session.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                ownedDriver?.Dispose();
            }
        }
    }

    private static IDriver CreateDriver(ArcadeDbRequest request)
    {
        var authToken = !string.IsNullOrWhiteSpace(request.Username)
            ? AuthTokens.Basic(request.Username, request.Password ?? "")
            : AuthTokens.None;

        return GraphDatabase.Driver(request.BoltUri!, authToken);
    }

    private static object ConvertValue(object value)
    {
        return value switch
        {
            INode node => new Dictionary<string, object>
            {
                ["_type"] = "node",
                ["_labels"] = node.Labels.ToList(),
                ["_properties"] = node.Properties.ToDictionary(p => p.Key, p => p.Value)
            },
            IRelationship rel => new Dictionary<string, object>
            {
                ["_type"] = "relationship",
                ["_relationshipType"] = rel.Type,
                ["_properties"] = rel.Properties.ToDictionary(p => p.Key, p => p.Value)
            },
            IPath path => new Dictionary<string, object>
            {
                ["_type"] = "path",
                ["_nodes"] = path.Nodes.Select(n => ConvertValue(n)).ToList(),
                ["_relationships"] = path.Relationships.Select(r => ConvertValue(r)).ToList()
            },
            _ => value
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ArcadeDB query returned {RecordCount} records, {NodesCreated} nodes created in {ElapsedMs}ms")]
    private partial void LogQueryCompleted(int recordCount, int nodesCreated, long elapsedMs);
}
