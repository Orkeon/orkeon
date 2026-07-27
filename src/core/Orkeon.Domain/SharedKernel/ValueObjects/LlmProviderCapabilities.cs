namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// How far a provider's API goes in constraining the shape of a response.
/// Ordered by increasing capability, so <c>&gt;=</c> comparisons are meaningful.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> named <c>OutputFormat</c> or <c>StructuredOutput</c>: both are
/// already taken in this solution for a different concept — the format of a *task result*
/// produced by Orkeon (<see cref="OutputFormat"/> in this namespace, its twin in
/// <c>Orkeon.Application.Interfaces.Ports</c>, and <c>IStructuredOutputParser</c>). This enum
/// is about a constraint sent *to the vendor*, so it borrows the wire vocabulary:
/// <c>response_format</c>.
/// </remarks>
public enum ResponseFormatSupport
{
    /// <summary>The API has no response-format field; the constraint can only be prompted.</summary>
    None = 0,

    /// <summary>The API guarantees syntactically valid JSON, but accepts no schema.</summary>
    JsonObject = 1,

    /// <summary>The API accepts a full JSON Schema and validates the response against it.</summary>
    JsonSchema = 2,
}

/// <summary>
/// How much control a provider's API gives over its reasoning / "thinking" pass.
/// Ordered by increasing control: each level also offers everything below it.
/// </summary>
public enum ThinkingSupport
{
    /// <summary>No reasoning pass is exposed.</summary>
    None = 0,

    /// <summary>Only an effort hint is accepted (e.g. <c>reasoning_effort</c>); it cannot be switched off.</summary>
    EffortOnly = 1,

    /// <summary>The reasoning pass can be explicitly enabled or disabled, on top of the effort hint.</summary>
    Toggle = 2,

    /// <summary>An explicit token budget can be set for the reasoning pass, on top of the toggle.</summary>
    Budget = 3,
}

/// <summary>
/// What a provider's API actually supports, declared by the provider itself.
/// </summary>
/// <remarks>
/// <para>
/// Two problems motivate this type. First, every provider that wired a cross-cutting option
/// rewrote the same translation code — the <c>thinking</c> block and the
/// <c>reasoning_content</c> extraction existed in two near-identical copies before this was
/// introduced, and satisfying the audit's G-15/G-16 literally would have meant ten more.
/// Declaring the capability instead lets the OpenAI dialect be written once, in
/// <c>OpenAICompatibleProviderBase</c>.
/// </para>
/// <para>
/// Second, and more importantly: an option declared on a provider that does not support it
/// used to be <em>silently dropped</em>. A YAML crew could set <c>thinking</c> or
/// <c>response_format</c> and nothing would reach the wire, with no diagnostic anywhere. A
/// declared capability makes that case reportable — the framework can say what it ignored and
/// why, instead of saying nothing.
/// </para>
/// <para>
/// The declaration is made at <em>provider</em> level while the truth is often at
/// <em>model</em> level (a text-only and a multimodal model can share one provider). It should
/// therefore be read as "this API accepts this field on the models that support it", not as
/// "every model of this provider supports it".
/// </para>
/// </remarks>
public sealed record LlmProviderCapabilities
{
    /// <summary>How far the API can constrain the response format.</summary>
    public ResponseFormatSupport ResponseFormat { get; init; }

    /// <summary>How much control the API gives over the reasoning pass.</summary>
    public ThinkingSupport Thinking { get; init; }

    /// <summary>Whether the API accepts image content parts alongside text.</summary>
    public bool Vision { get; init; }

    /// <summary>
    /// Whether prompt caching must be requested explicitly (cache breakpoints) rather than
    /// being applied automatically by the vendor.
    /// </summary>
    public bool ExplicitPromptCaching { get; init; }

    /// <summary>
    /// Whether the API requires the word "json" to appear in the prompt when a JSON response
    /// format is requested. DeepSeek does: without it the model can emit an unbounded
    /// whitespace stream until <c>max_tokens</c>.
    /// </summary>
    public bool RequiresJsonKeywordInPrompt { get; init; }

    /// <summary>
    /// Whether the previous assistant turn's <c>reasoning_content</c> must be replayed on
    /// every subsequent request. This is a DeepSeek constraint (HTTP 400 without it), not a
    /// general property of thinking models — Z.AI documents the opposite.
    /// </summary>
    public bool ReplaysReasoningContent { get; init; }

    /// <summary>
    /// The conservative default for a provider that has not declared anything: no capability
    /// is assumed, so nothing is written to the wire on its behalf.
    /// </summary>
    public static LlmProviderCapabilities Unknown { get; } = new();
}
