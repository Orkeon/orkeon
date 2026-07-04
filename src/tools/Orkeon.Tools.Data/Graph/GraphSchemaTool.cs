using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.Graph.Models;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Tool for inspecting graph database schema (vertex labels, edge labels, properties, indexes).
/// Supports ArcadeDB (via Bolt/Neo4j driver) and JanusGraph (via Gremlin/WebSocket).
/// </summary>
[ToolContract("graph_schema",
    Name = "graph_schema",
    Description = "Inspect graph database schema (vertex labels, edge labels, properties, indexes)",
    Category = "Data Operations")]
public partial class GraphSchemaTool : ToolBase<GraphSchemaRequest, GraphSchemaResponse>
{
    private readonly IGremlinClient? _injectedGremlinClient;
    private readonly IDriver? _injectedBoltDriver;

    /// <summary>
    /// Initializes a new instance of <see cref="GraphSchemaTool"/> with optional injected clients for testability.
    /// </summary>
    public GraphSchemaTool(
        IGremlinClient? gremlinClient = null,
        IDriver? boltDriver = null,
        ILogger<GraphSchemaTool>? logger = null) : base(logger)
    {
        _injectedGremlinClient = gremlinClient;
        _injectedBoltDriver = boltDriver;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(GraphSchemaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return "Endpoint cannot be empty";

        if (request.GraphType == GraphType.ArcadeDB && string.IsNullOrWhiteSpace(request.Database))
            return "Database is required for ArcadeDB";

        return null;
    }

    /// <inheritdoc />
    protected override Task<GraphSchemaResponse> ExecuteTypedAsync(
        GraphSchemaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<GraphSchemaResponse> ExecuteCoreAsync()
        {
            return request.GraphType switch
            {
                GraphType.JanusGraph => await IntrospectJanusGraphAsync(request).ConfigureAwait(false),
                GraphType.ArcadeDB => await IntrospectArcadeDbAsync(request).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.GraphType, "Unsupported graph type")
            };
        }
    }

    private async Task<GraphSchemaResponse> IntrospectJanusGraphAsync(
        GraphSchemaRequest request)
    {
        // ownedClient is non-null only when this method creates the client, so the
        // finally block can dispose it unconditionally (the injected client is owned
        // by the caller and must not be disposed here).
        var ownedClient = _injectedGremlinClient is null ? CreateGremlinClient(request) : null;
        var client = _injectedGremlinClient ?? ownedClient!;

        try
        {
            var vertexLabels = await QueryGremlinStringsAsync(client, "g.V().label().dedup()").ConfigureAwait(false);
            var edgeLabels = await QueryGremlinStringsAsync(client, "g.E().label().dedup()").ConfigureAwait(false);

            var vertexInfos = new List<VertexLabelInfo>();
            foreach (var label in vertexLabels)
            {
                var props = await QueryGremlinStringsAsync(client,
                    $"g.V().hasLabel('{EscapeGremlinString(label)}').properties().key().dedup()").ConfigureAwait(false);
                vertexInfos.Add(new VertexLabelInfo
                {
                    Label = label,
                    Properties = props
                });
            }

            var edgeInfos = new List<EdgeLabelInfo>();
            foreach (var label in edgeLabels)
            {
                var props = await QueryGremlinStringsAsync(client,
                    $"g.E().hasLabel('{EscapeGremlinString(label)}').properties().key().dedup()").ConfigureAwait(false);
                edgeInfos.Add(new EdgeLabelInfo
                {
                    Label = label,
                    Properties = props
                });
            }

            LogSchemaIntrospected("JanusGraph", vertexInfos.Count, edgeInfos.Count);

            return new GraphSchemaResponse
            {
                VertexLabels = vertexInfos,
                EdgeLabels = edgeInfos,
                PropertyKeys = [],
                Indexes = [],
                GraphType = GraphType.JanusGraph
            };
        }
        finally
        {
            (ownedClient as IDisposable)?.Dispose();
        }
    }

    private async Task<GraphSchemaResponse> IntrospectArcadeDbAsync(
        GraphSchemaRequest request)
    {
        // ownedDriver is non-null only when this method creates the driver, so the
        // finally block can dispose it unconditionally (the injected driver is owned
        // by the caller and must not be disposed here).
        var ownedDriver = _injectedBoltDriver is null ? CreateBoltDriver(request) : null;
        var driver = _injectedBoltDriver ?? ownedDriver!;

        try
        {
            var session = driver.AsyncSession(builder => builder.WithDatabase(request.Database!));

            try
            {
                var vertexLabels = await QueryBoltStringsAsync(session, "MATCH (n) RETURN DISTINCT labels(n) AS labels").ConfigureAwait(false);
                var edgeTypes = await QueryBoltStringsAsync(session, "MATCH ()-[r]->() RETURN DISTINCT type(r) AS type").ConfigureAwait(false);

                var vertexInfos = vertexLabels.Select(l => new VertexLabelInfo
                {
                    Label = l,
                    Properties = []
                }).ToList();

                var edgeInfos = edgeTypes.Select(t => new EdgeLabelInfo
                {
                    Label = t,
                    Properties = []
                }).ToList();

                LogSchemaIntrospected("ArcadeDB", vertexInfos.Count, edgeInfos.Count);

                return new GraphSchemaResponse
                {
                    VertexLabels = vertexInfos,
                    EdgeLabels = edgeInfos,
                    PropertyKeys = [],
                    Indexes = [],
                    GraphType = GraphType.ArcadeDB
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

    private static async Task<List<string>> QueryGremlinStringsAsync(IGremlinClient client, string traversal)
    {
        var msg = RequestMessage.Build(Tokens.OpsEval)
            .AddArgument(Tokens.ArgsGremlin, traversal)
            .Create();

        var resultSet = await client.SubmitAsync<string>(msg).ConfigureAwait(false);
        return resultSet.ToList();
    }

    private static async Task<List<string>> QueryBoltStringsAsync(IAsyncSession session, string query)
    {
        var cursor = await session.RunAsync(query).ConfigureAwait(false);
        var results = new List<string>();

        while (await cursor.FetchAsync().ConfigureAwait(false))
        {
            var value = cursor.Current.Values.Values.FirstOrDefault();
            switch (value)
            {
                case IReadOnlyList<object> labels:
                    {
                        foreach (var label in labels)
                            if (label is string s && !results.Contains(s))
                                results.Add(s);
                        break;
                    }
                case string s:
                    if (!results.Contains(s))
                        results.Add(s);
                    break;
            }
        }

        await cursor.ConsumeAsync().ConfigureAwait(false);
        return results;
    }

    private static GremlinClient CreateGremlinClient(GraphSchemaRequest request)
    {
        var uri = new Uri(request.Endpoint);
        var server = new GremlinServer(
            uri.Host,
            uri.Port,
            uri.Scheme == "wss",
            request.Username,
            request.Password);

        return new GremlinClient(server, new GraphSON3MessageSerializer());
    }

    private static IDriver CreateBoltDriver(GraphSchemaRequest request)
    {
        var authToken = !string.IsNullOrWhiteSpace(request.Username)
            ? AuthTokens.Basic(request.Username, request.Password ?? "")
            : AuthTokens.None;

        return GraphDatabase.Driver(new Uri(request.Endpoint), authToken);
    }

    internal static string EscapeGremlinString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graph schema introspected for {GraphType}: {VertexCount} vertex labels, {EdgeCount} edge labels")]
    private partial void LogSchemaIntrospected(string graphType, int vertexCount, int edgeCount);
}
