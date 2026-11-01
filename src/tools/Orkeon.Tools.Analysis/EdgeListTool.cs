using System.Buffers.Text;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

/// <summary>
/// Lists every edge of the given kinds in the whole index, one page at a time: the exhaustive answer to "every base
/// class" (the targets of <c>inherits</c>), where <c>dependency_graph</c> samples its nodes first. The cursor is the
/// position of the last edge returned, so a page resumes after it even when the index changed in between.
/// </summary>
public sealed class EdgeListTool : ToolBase<EdgeListRequest, EdgeListResponse>
{
    private readonly IRaggableStore _store;

    public EdgeListTool(IRaggableStore store, ILogger<EdgeListTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "edge_list";
    public override string Description =>
        "Which edges of these kinds exist across the whole index? Lists every edge of edge_kinds (required), from and to as "
        + "FQNs with their ids, in a stable order, page_size at a time (200, up to 1000), with total and the next_cursor to pass "
        + "back as cursor until it is null; root_fqn keeps the edges with an end at or under one node (a package or file by path, "
        + "a type or member by FQN, a namespace by its files and name). For the edges among the nodes of a scope "
        + "with its cycles, dependency_graph; for the neighbourhood of one symbol, sub_graph.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override string? ValidateTypedRequest(EdgeListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EdgeKinds.IsDefaultOrEmpty || request.EdgeKinds.All(string.IsNullOrWhiteSpace))
            return $"edge_kinds is required: one or more of {string.Join(", ", GraphKindNames.EdgeKindNames)}.";
        if (request.PageSize is < 1 or > EdgeListRequest.MaxPageSize)
            return $"page_size must be between 1 and {EdgeListRequest.MaxPageSize}.";
        return null;
    }

    protected override Task<EdgeListResponse> ExecuteTypedAsync(EdgeListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<EdgeListResponse> ExecuteCoreAsync()
        {
            var kinds = GraphKindNames.ParseEdgeKinds(request.EdgeKinds);
            var after = DecodeCursor(request.Cursor);
            var mask = EdgeKind.None;
            foreach (var kind in kinds) mask |= kind;
            var root = string.IsNullOrEmpty(request.RootFqn) ? null : request.RootFqn;

            var page = await _store.ListEdgesAsync(
                new EdgeListQuery { Kinds = mask, RootFqn = root, After = after, Take = request.PageSize },
                cancellationToken).ConfigureAwait(false);

            return new EdgeListResponse
            {
                Edges = [.. page.Edges.Select(e => new EdgeListItem
                {
                    From = e.FromFqn,
                    To = e.ToFqn,
                    Kind = e.Kind,
                    FromId = e.FromId,
                    ToId = e.ToId,
                })],
                NextCursor = page.HasMore && page.Edges.Length > 0 ? EncodeCursor(page.Edges[^1].Key) : null,
                Total = page.Total,
            };
        }
    }

    internal static string EncodeCursor(EdgeListKey key)
    {
        var parts = new[] { key.FromFqn, key.ToFqn, ((int)key.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture), key.EdgeId };
        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(parts));
    }

    internal static EdgeListKey? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        try
        {
            var parts = JsonSerializer.Deserialize<string[]>(Base64Url.DecodeFromChars(cursor));
            if (parts is { Length: 4 }
                && parts.All(p => p is not null)
                && int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var kind))
            {
                return new EdgeListKey(parts[0], parts[1], (EdgeKind)kind, parts[3]);
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // Falls through to the refusal below.
        }

        throw new ArgumentException(
            $"Invalid cursor '{Truncate(cursor)}': pass back the next_cursor of the previous edge_list page unchanged.", nameof(cursor));
    }

    private static string Truncate(string text) => text.Length <= 40 ? text : string.Concat(text.AsSpan(0, 40), "...");
}
