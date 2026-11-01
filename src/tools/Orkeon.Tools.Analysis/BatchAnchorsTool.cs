using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class BatchAnchorsTool : ToolBase<BatchAnchorsRequest, BatchAnchorsResponse>
{
    private readonly IRaggableStore _store;

    public BatchAnchorsTool(IRaggableStore store, ILogger<BatchAnchorsTool>? logger = null) : base(logger)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public override string Name => "batch_anchors";
    public override string Description => "Closure of anchors (contains, callers, tests) and overlap of two anchor sets.";
    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<BatchAnchorsResponse> ExecuteTypedAsync(BatchAnchorsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = _store.GetIndexedRoots().Select(r => r.VirtualRoot).FirstOrDefault() ?? "/";
        switch (request.Action)
        {
            case "closure":
                var closure = await ClosureAsync(request.Fqns, request.Files, root, cancellationToken).ConfigureAwait(false);
                return new BatchAnchorsResponse { Nodes = closure.Nodes, Unresolved = closure.Unresolved, Truncated = closure.Truncated };
            case "overlap":
                var a = await ClosureAsync(request.A?.Fqns, request.A?.Files, root, cancellationToken).ConfigureAwait(false);
                var b = await ClosureAsync(request.B?.Fqns, request.B?.Files, root, cancellationToken).ConfigureAwait(false);
                var overlap = AnchorsQuery.Overlap(a, b);
                return new BatchAnchorsResponse
                {
                    Shared = overlap.Shared,
                    Disjoint = overlap.Disjoint,
                    Truncated = overlap.Truncated,
                    UnresolvedA = a.Unresolved,
                    UnresolvedB = b.Unresolved,
                };
            default:
                throw new ArgumentException($"Unknown action '{request.Action}': expected 'closure' or 'overlap'.", nameof(request));
        }
    }

    private Task<AnchorClosure> ClosureAsync(IReadOnlyList<string>? fqns, IReadOnlyList<string>? files, string root, CancellationToken ct)
        => AnchorsQuery.ClosureAsync(_store, fqns ?? [], files ?? [], root, ct);
}

public sealed record AnchorSet
{
    [JsonPropertyName("fqns")]
    public ImmutableList<string> Fqns { get; init; } = [];

    [JsonPropertyName("files")]
    public ImmutableList<string> Files { get; init; } = [];
}

public sealed record BatchAnchorsRequest
{
    [FieldSchema(Description = "closure expands fqns and files to the nodes they contain, their callers and their tests; overlap intersects the closures of a and b.", Enum = new[] { "closure", "overlap" }, IsRequired = true)]
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [FieldSchema(Description = "FQNs to expand (closure).", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("fqns")]
    public ImmutableList<string>? Fqns { get; init; }

    [FieldSchema(Description = "Virtual paths of files to expand (closure).", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("files")]
    public ImmutableList<string>? Files { get; init; }

    [FieldSchema(Description = "First anchor set (overlap): {fqns: [...], files: [...]}.", IsRequired = false)]
    [JsonPropertyName("a")]
    public AnchorSet? A { get; init; }

    [FieldSchema(Description = "Second anchor set (overlap): {fqns: [...], files: [...]}.", IsRequired = false)]
    [JsonPropertyName("b")]
    public AnchorSet? B { get; init; }
}

public sealed record BatchAnchorsResponse
{
    [JsonPropertyName("nodes")]
    public ImmutableList<string> Nodes { get; init; } = [];

    [JsonPropertyName("unresolved")]
    public ImmutableList<string> Unresolved { get; init; } = [];

    [JsonPropertyName("unresolved_a")]
    public ImmutableList<string> UnresolvedA { get; init; } = [];

    [JsonPropertyName("unresolved_b")]
    public ImmutableList<string> UnresolvedB { get; init; } = [];

    [JsonPropertyName("shared")]
    public ImmutableList<string> Shared { get; init; } = [];

    [JsonPropertyName("disjoint")]
    public bool Disjoint { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }
}
