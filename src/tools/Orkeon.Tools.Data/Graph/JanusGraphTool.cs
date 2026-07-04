using System.Diagnostics;
using System.Text.RegularExpressions;
using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Tool for executing Gremlin traversals on JanusGraph databases via WebSocket.
/// </summary>
[ToolContract("janusgraph_query",
    Name = "janusgraph_query",
    Description = "Execute Gremlin traversals on JanusGraph databases",
    Category = "Data Operations")]
public partial class JanusGraphTool : ToolBase<JanusGraphRequest, JanusGraphResponse>
{
    [GeneratedRegex(@"\.\s*(drop|remove)\s*\(", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 5000)]
    private static partial Regex DestructiveRegex();

    private readonly IGremlinClient? _injectedClient;

    /// <summary>
    /// Initializes a new instance of <see cref="JanusGraphTool"/> with an optional injected client for testability.
    /// </summary>
    /// <param name="client">Optional Gremlin client instance. If null, a client will be created from the request's GremlinEndpoint.</param>
    /// <param name="logger">Optional logger instance.</param>
    public JanusGraphTool(IGremlinClient? client = null, ILogger<JanusGraphTool>? logger = null) : base(logger)
    {
        _injectedClient = client;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(JanusGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GremlinEndpoint))
            return "GremlinEndpoint cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Traversal))
            return "Traversal cannot be empty";

        if (DestructiveRegex().IsMatch(request.Traversal))
            return "Destructive operations (drop, remove) are not allowed";

        return null;
    }

    /// <inheritdoc />
    protected override Task<JanusGraphResponse> ExecuteTypedAsync(
        JanusGraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<JanusGraphResponse> ExecuteTypedCoreAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            // ownedClient is non-null only when this method creates the client, so the
            // finally block can dispose it unconditionally (the injected client is owned
            // by the caller and must not be disposed here).
            var ownedClient = _injectedClient is null ? CreateClient(request) : null;
            var client = _injectedClient ?? ownedClient!;

            try
            {
                var requestMessage = BuildRequestMessage(request);
                var resultSet = await client.SubmitAsync<Dictionary<string, object?>>(requestMessage, cancellationToken).ConfigureAwait(false);
                var results = resultSet.Take(request.MaxResults).ToList();

                stopwatch.Stop();

                var resultType = DetectResultType(results);

                LogTraversalCompleted(results.Count, resultType, stopwatch.ElapsedMilliseconds);

                return new JanusGraphResponse
                {
                    Results = results,
                    ResultCount = results.Count,
                    ResultType = resultType,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            finally
            {
                (ownedClient as IDisposable)?.Dispose();
            }
        }
    }

    private static GremlinClient CreateClient(JanusGraphRequest request)
    {
        var uri = new Uri(request.GremlinEndpoint);
        var server = new GremlinServer(
            uri.Host,
            uri.Port,
            uri.Scheme == "wss");

        return new GremlinClient(
            server,
            new GraphSON3MessageSerializer());
    }

    internal static RequestMessage BuildRequestMessage(JanusGraphRequest request)
    {
        var builder = RequestMessage.Build(Tokens.OpsEval)
            .AddArgument(Tokens.ArgsGremlin, request.Traversal)
            .AddArgument(Tokens.ArgsEvalTimeout, request.TimeoutMs);

        if (request.Bindings is { Count: > 0 })
            builder.AddArgument(Tokens.ArgsBindings, request.Bindings);

        return builder.Create();
    }

    internal static string DetectResultType(List<Dictionary<string, object?>> results)
    {
        if (results.Count == 0)
            return "value";

        var first = results[0];

        if (first.ContainsKey("label") && first.ContainsKey("id"))
        {
            if (first.ContainsKey("inV") || first.ContainsKey("outV") || first.ContainsKey("inVLabel") || first.ContainsKey("outVLabel"))
                return "edge";
            return "vertex";
        }

        if (first.ContainsKey("labels") && first.ContainsKey("objects"))
            return "path";

        return "value";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "JanusGraph traversal returned {ResultCount} results of type '{ResultType}' in {ElapsedMs}ms")]
    private partial void LogTraversalCompleted(int resultCount, string resultType, long elapsedMs);
}
