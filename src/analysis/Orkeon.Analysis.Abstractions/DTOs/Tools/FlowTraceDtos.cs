using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Analysis.Abstractions.DTOs.Responses;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record FlowTraceRequest
{
    [FieldSchema(Description = "FQN the paths start from.", IsRequired = true)]
    public string From { get; init; } = "";

    [FieldSchema(Description = "FQN the paths must reach; omit it to list every path from the start.", IsRequired = false)]
    public string? To { get; init; }

    [FieldSchema(Description = "Forward follows calls out of from; Backward follows them into from; Both.", IsRequired = false, Default = "Forward")]
    public Direction Direction { get; init; } = Direction.Forward;

    [FieldSchema(Description = "Most hops per path (default 4).", IsRequired = false, Default = 4)]
    public int MaxDepth { get; init; } = 4;

    [FieldSchema(Description = "Edge kinds to follow, by name: contains, calls, imports, inherits, tests, documents, project_depends. Legacy enum names (Imports, Extends, Implements...) are still accepted. Empty follows calls.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("edge_kinds")]
    public ImmutableArray<string> EdgeKinds { get; init; } = ["calls"];
    [FieldSchema(Description = "With to: every path up to max_paths instead of the shortest one.", IsRequired = false, Default = false)]
    public bool IncludeAllPaths { get; init; }

    [FieldSchema(Description = "Most paths returned (default 20); truncated says when more exist.", IsRequired = false, Default = 20)]
    public int MaxPaths { get; init; } = 20;
}

public sealed record FlowTraceResponse
{
    public required ImmutableArray<CallPath> Paths { get; init; }
    public bool Truncated { get; init; }
}
