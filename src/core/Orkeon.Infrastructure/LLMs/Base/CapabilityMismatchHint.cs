namespace Orkeon.Infrastructure.LLMs.Base;

/// <summary>
/// Turns a vendor's "this model cannot do that" refusal into a sentence that says whose
/// assumption was wrong.
/// </summary>
/// <remarks>
/// <para>
/// <c>LlmProviderCapabilities</c> is declared once per provider, but support is a property of
/// the <em>model</em>. Ollama advertises thinking and vision because some of its models have
/// them; ask <c>llama3.2</c> and the API answers <c>"llama3.2" does not support thinking</c>.
/// Z.AI advertises vision; <c>glm-5.2</c> answers <c>allowed values: ['text']</c>. The vendor
/// is right and precise in both cases, and in both cases it cannot know why Orkeon sent the
/// option at all — so a reader sees a framework that asks for impossible things.
/// </para>
/// <para>
/// Matching the vendor's wording rather than tracking what the request contained is deliberate.
/// The refusal arrives at a point where several providers no longer hold the request, and
/// threading it through every error path would spread this concern across the whole layer to
/// restate what the error already says. Nothing here guesses: a hint is added only when the
/// vendor itself named an unsupported capability.
/// </para>
/// <para>
/// This does not fix the granularity — Orkeon still has no per-model table, and building one
/// would go stale exactly the way the default models did. It removes the misattribution.
/// Measured on Z.AI and Ollama, campaigns of 2026-08-01 (D-03).
/// </para>
/// <para>
/// <strong>Tool calling</strong> joined the list on 2026-08-02, from the <c>llava</c> campaign:
/// <c>registry.ollama.ai/library/llava:latest does not support tools</c> reached the report bare
/// while the thinking refusal, one line above it, was fully attributed. Same provider, same
/// campaign, same class of mismatch — the table simply had not been asked about tools yet. That
/// is the shape of this defect: it is not a Z.AI problem or a vision problem, it recurs on every
/// capability declared per provider, and each new one arrives silently.
/// </para>
/// </remarks>
internal static class CapabilityMismatchHint
{
    /// <summary>Vendor phrasings, paired with the capability each one is really about.</summary>
    private static readonly (string Marker, string Capability)[] Signatures =
    [
        ("does not support thinking", "thinking"),
        ("does not support multimodal", "image input"),
        ("does not support vision", "image input"),
        ("does not support image", "image input"),
        ("allowed values: ['text']", "image input"),
        ("does not support tools", "tool calling"),
        ("does not support function calling", "tool calling"),
    ];

    /// <summary>
    /// Returns a sentence to append to a vendor error, or an empty string when the error is
    /// about something else.
    /// </summary>
    /// <param name="vendorError">The error text as the API returned it.</param>
    /// <param name="providerDisplayName">Provider name, as shown to the reader.</param>
    /// <param name="model">The model identifier the request used.</param>
    /// <returns>A leading-space sentence, or <see cref="string.Empty"/>.</returns>
    public static string ForVendorError(string? vendorError, string providerDisplayName, string? model)
    {
        if (string.IsNullOrWhiteSpace(vendorError))
            return "";

        var capability = Array.Find(
            Signatures,
            s => vendorError.Contains(s.Marker, StringComparison.OrdinalIgnoreCase)).Capability;

        if (capability is null)
            return "";

        var named = string.IsNullOrWhiteSpace(model) ? "this model" : $"'{model}'";
        return $" — {providerDisplayName} declares {capability} support, but that is declared per "
             + $"provider while models differ: {named} does not have it. Pick a model that does, "
             + "or drop the option.";
    }
}
