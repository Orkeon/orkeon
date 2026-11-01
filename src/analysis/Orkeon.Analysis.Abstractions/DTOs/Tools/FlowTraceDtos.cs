using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Analysis.Abstractions.DTOs.Responses;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record FlowTraceRequest
{
    public string From { get; init; } = "";
    public string? To { get; init; }
    public Direction Direction { get; init; } = Direction.Forward;
    public int MaxDepth { get; init; } = 4;

    [FieldSchema(Description = "Edge kinds to follow, by name: contains, calls, imports, inherits, tests, documents, project_depends. Legacy enum names (Imports, Extends, Implements...) are still accepted. Empty follows calls.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("edge_kinds")]
    public ImmutableArray<string> EdgeKinds { get; init; } = ["calls"];
    public bool IncludeAllPaths { get; init; }
    public int MaxPaths { get; init; } = 20;
}

public sealed record FlowTraceResponse
{
    public required ImmutableArray<CallPath> Paths { get; init; }
    public bool Truncated { get; init; }
}
