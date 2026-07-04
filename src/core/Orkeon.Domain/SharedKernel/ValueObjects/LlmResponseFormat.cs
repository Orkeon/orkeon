namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Output-format constraint forwarded to OpenAI-compatible providers that implement the
/// <c>response_format</c> field. Today: DeepSeek (<c>"json_object"</c> guarantees valid JSON
/// at the API layer). The string is kept open (not an enum) to stay forward-compatible with
/// future provider values like <c>"json_schema"</c>.
/// </summary>
public sealed record LlmResponseFormat
{
    /// <summary>Format identifier. Provider-specific. Known values: <c>"text"</c>, <c>"json_object"</c>.</summary>
    public string Type { get; init; } = "text";

    /// <summary>Returns a <see cref="LlmResponseFormat"/> with <c>Type = "json_object"</c>.</summary>
    public static LlmResponseFormat JsonObject() => new() { Type = "json_object" };

    /// <summary>Returns a <see cref="LlmResponseFormat"/> with <c>Type = "text"</c> (provider default).</summary>
    public static LlmResponseFormat Text() => new() { Type = "text" };
}
