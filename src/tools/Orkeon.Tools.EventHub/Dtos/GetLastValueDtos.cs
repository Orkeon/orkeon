using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>get_last_value</c> tool.</summary>
public sealed record GetLastValueRequest
{
    /// <summary>Cache key.</summary>
    [JsonPropertyName("key")]
    [FieldSchema(Description = "Cache key", Example = "current.order")]
    public string Key { get; init; } = "";

    /// <summary>Optional crew scope. Defaults to the caller's crew.</summary>
    [JsonPropertyName("crew_scope")]
    [FieldSchema(Description = "Optional crew scope. Defaults to caller's crew.", IsRequired = false)]
    public string? CrewScope { get; init; }
}

/// <summary>Response for the <c>get_last_value</c> tool.</summary>
public sealed record GetLastValueResponse
{
    /// <summary>The retained payload, or <see langword="null"/> if no value was stored.</summary>
    [JsonPropertyName("value")]
    [ReturnSchema(Description = "Retained payload (null if absent)")]
    public JsonNode? Value { get; init; }

    /// <summary>UTC timestamp of when the value was last retained.</summary>
    [JsonPropertyName("set_at")]
    [ReturnSchema(Description = "UTC set timestamp (null if absent)")]
    public string? SetAt { get; init; }

    /// <summary>Whether a value was found.</summary>
    [JsonPropertyName("found")]
    [ReturnSchema(Description = "True if a value was found")]
    public bool Found { get; init; }
}
