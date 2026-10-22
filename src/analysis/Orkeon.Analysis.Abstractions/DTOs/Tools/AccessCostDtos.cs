using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record AccessCostReport
{
    [JsonPropertyName("members")]
    public required ImmutableList<MemberAccessCost> Members { get; init; }

    [JsonPropertyName("cost")]
    public required CostLine Cost { get; init; }
}

public sealed record MemberAccessCost
{
    [JsonPropertyName("member")]
    public required string Member { get; init; }

    [JsonPropertyName("port_calls")]
    public required ImmutableList<PortCall> PortCalls { get; init; }

    [JsonPropertyName("in_memory_filters")]
    public required ImmutableList<InMemoryFilter> InMemoryFilters { get; init; }

    [JsonPropertyName("cost")]
    public required CostLine Cost { get; init; }
}

public sealed record PortCall
{
    [JsonPropertyName("receiver")]
    public required string Receiver { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("in_loop")]
    public bool InLoop { get; init; }

    [JsonPropertyName("in_lambda")]
    public bool InLambda { get; init; }
}

public sealed record InMemoryFilter
{
    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }
}

public sealed record CostLine
{
    [JsonPropertyName("reads")]
    public int Reads { get; init; }

    [JsonPropertyName("writes")]
    public int Writes { get; init; }

    [JsonPropertyName("loops")]
    public int Loops { get; init; }

    [JsonPropertyName("lambdas")]
    public int Lambdas { get; init; }

    [JsonPropertyName("memory_filters")]
    public int MemoryFilters { get; init; }

    public string Render()
    {
        var blocking = Loops + Lambdas + MemoryFilters;
        return $"Cost: {Plural(Reads, "read")} + {Plural(Writes, "write")} through ports outside loops; " +
               $"in a loop, a lambda or a memory filter: {(blocking == 0 ? "none" : blocking + " - back to design")}";
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? string.Empty : "s")}";
}
