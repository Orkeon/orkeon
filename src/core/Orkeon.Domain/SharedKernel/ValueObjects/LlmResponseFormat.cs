namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// A JSON Schema the model's response must conform to, when the provider validates it
/// server-side rather than merely guaranteeing syntactically valid JSON.
/// </summary>
/// <param name="Name">
/// Schema name. Required by the OpenAI dialect; used by vendors for diagnostics and caching.
/// </param>
/// <param name="Schema">
/// The JSON Schema document itself, as a JSON string. Kept as a string rather than a typed
/// tree so a schema authored in YAML, in a script or in C# travels unchanged to the wire.
/// </param>
/// <param name="Strict">
/// Whether the vendor should reject any deviation from the schema instead of doing its best.
/// Providers that do not offer a strict mode ignore it.
/// </param>
public sealed record LlmJsonSchema(string Name, string Schema, bool Strict = true);

/// <summary>
/// Output-format constraint forwarded to providers that can constrain a response:
/// <c>response_format</c> on the OpenAI-compatible family, <c>output_config.format</c> on
/// Anthropic, <c>format</c> on Ollama.
/// </summary>
/// <remarks>
/// <see cref="Type"/> is kept an open string rather than an enum so a provider-specific value
/// can be passed through without a Domain change. <see cref="Schema"/> is additive: a
/// configuration that only sets <see cref="Type"/> behaves exactly as before.
/// </remarks>
public sealed record LlmResponseFormat
{
    /// <summary>
    /// Format identifier. Provider-specific. Known values: <c>"text"</c>,
    /// <c>"json_object"</c>, <c>"json_schema"</c>.
    /// </summary>
    public string Type { get; init; } = "text";

    /// <summary>
    /// The schema to validate against, set only when <see cref="Type"/> is
    /// <c>"json_schema"</c>. Providers that only guarantee well-formed JSON degrade to
    /// <c>json_object</c> and say so.
    /// </summary>
    public LlmJsonSchema? Schema { get; init; }

    /// <summary>Returns a <see cref="LlmResponseFormat"/> with <c>Type = "json_object"</c>.</summary>
    public static LlmResponseFormat JsonObject() => new() { Type = "json_object" };

    /// <summary>Returns a <see cref="LlmResponseFormat"/> with <c>Type = "text"</c> (provider default).</summary>
    public static LlmResponseFormat Text() => new() { Type = "text" };

    /// <summary>
    /// Returns a schema-constrained <see cref="LlmResponseFormat"/>.
    /// </summary>
    /// <param name="name">Schema name.</param>
    /// <param name="schema">The JSON Schema document, as a JSON string.</param>
    /// <param name="strict">Whether the vendor must reject any deviation. Defaults to true.</param>
    public static LlmResponseFormat JsonSchema(string name, string schema, bool strict = true) =>
        new() { Type = "json_schema", Schema = new LlmJsonSchema(name, schema, strict) };
}
