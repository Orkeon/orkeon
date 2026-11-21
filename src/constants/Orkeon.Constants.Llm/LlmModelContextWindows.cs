namespace Orkeon.Constants.Llm;

/// <summary>
/// The documented context window per model, in tokens (#432): the figure a loop compacts its
/// conversation against when the agent's profile sets no <c>ContextWindowTokens</c>.
/// <para>
/// <b>Only documented figures go in</b>, each with its page and the date it was read. A window
/// written as "256K" or "1M" is read as decimal (256,000; 1,000,000), the lower of the two
/// readings, so the trigger never sits past the real window. A model this table does not know
/// falls back to <c>Orkeon:Cli:Session:ContextWindowTokens</c>.
/// </para>
/// <para>
/// Lookup: as <see cref="LlmModelOutputLimits"/>, an exact id, else the longest registered prefix
/// followed by a separator, else the segment after a vendor slash. Comparisons ignore case.
/// </para>
/// </summary>
public static class LlmModelContextWindows
{
    private static readonly Dictionary<string, int> Documented = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kimi — platform.kimi.ai/docs/guide/kimi-k3-quickstart, read 2026-09-19: the request is
        // bounded by "1M - prompt". K2.6: "256K - prompt_tokens" (the troubleshooting page, same day).
        // K3.1 Pro is absent: no page read gives its window ("kimi-k3" does not claim it, '.' is not
        // a separator).
        ["kimi-k3"] = 1_000_000,
        ["kimi-k2.6"] = 256_000,

        // Mistral — docs.mistral.ai/api/endpoint/chat and the model card, 2026-09-19: a context
        // length of 256K on medium-2604, which the card names mistral-medium-3-5.
        ["mistral-medium-2604"] = 256_000,
        ["mistral-medium-3-5"] = 256_000,
    };

    /// <summary>
    /// The documented context window of <paramref name="model"/> in tokens, or null when the table
    /// does not know the model.
    /// </summary>
    /// <param name="model">The model id as the endpoint expects it.</param>
    public static int? ContextWindowTokens(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        var id = model.Trim();
        if (ModelIdTable.Lookup(Documented, id) is { } known)
            return known;

        return ModelIdTable.AfterVendorSlash(id) is { } bare ? ModelIdTable.Lookup(Documented, bare) : null;
    }
}
