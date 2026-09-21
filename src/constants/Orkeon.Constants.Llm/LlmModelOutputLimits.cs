namespace Orkeon.Constants.Llm;

/// <summary>
/// The documented maximum output length per model — the cap a request carries when nothing
/// pins one (LLM-10).
/// <para>
/// The engine used to send a blanket 4096 for every model. A reasoning model spends that
/// budget thinking and answers empty; the retry that follows cannot call tools; the run
/// goes green with no deliverable (owner recette 2026-09-19, kimi-k3). So the default is
/// now what the vendor documents as the model's own maximum, and 4096 is only the fallback
/// for a model this table does not know. An explicit value — <c>Llm:MaxTokens</c>, a Studio
/// profile, a crew's <c>max_tokens</c> — always wins over both.
/// </para>
/// <para>
/// <b>Only documented figures go in</b>, each with its page and the date it was read: an
/// invented number turns into an HTTP 400 on every call, which is worse than a gap. Where the
/// vendor documents no output cap and bounds the request by <c>window − prompt</c> (Mistral),
/// the entry is <see cref="Unbounded"/> and the field is left out — the endpoint then
/// generates up to its window. A model known to be bounded by <c>window − prompt</c> but
/// pinned here anyway (Kimi) says so in its comment; a rejection of a catalogue cap is retried
/// once without the field by the OpenAI-compatible providers.
/// </para>
/// <para>
/// Lookup: an exact id, else the longest registered prefix followed by a separator
/// (<c>claude-sonnet-5</c> covers <c>claude-sonnet-5-20260401</c>; <c>gpt-5.6-sol</c> does not
/// claim <c>gpt-5.6-solar</c>), else the segment after a vendor slash (<c>google/gemini-3.7-flash</c>
/// reads as <c>gemini-3.7-flash</c>). A <c>provider:model</c> entry is tried first when the
/// caller names the provider: the same id can carry a different bound on another endpoint.
/// Comparisons ignore case.
/// </para>
/// </summary>
public static class LlmModelOutputLimits
{
    /// <summary>
    /// A documented "no output cap": the request leaves the field out and the endpoint
    /// generates up to its window. Distinct from an unknown model, which gets the fallback.
    /// </summary>
    public const int Unbounded = 0;

    private static readonly Dictionary<string, int> Documented = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI — developers.openai.com/api/docs/models/{gpt-5.6-sol,gpt-6-astra}, read 2026-09-19:
        // 128,000 output tokens; reasoning counts inside max_completion_tokens.
        ["gpt-5.6-sol"] = 128_000,
        ["gpt-6-astra"] = 128_000,

        // Anthropic — platform.claude.com/docs/en/about-claude/models/overview, 2026-09-19: 128K on
        // the Messages API for the 5 generation; thinking counts toward max_tokens; a prompt plus
        // max_tokens above the window is accepted and stops early on these models.
        ["claude-sonnet-5"] = 128_000,
        ["claude-opus-5"] = 128_000,
        ["claude-fable-5-1"] = 128_000,

        // Gemini — ai.google.dev/gemini-api/docs/models/{gemini-3.7-flash,gemini-3.8-flash}, 2026-09-19:
        // 65,536 including thought tokens; 65,537 is a 400 INVALID_ARGUMENT.
        ["gemini-3.7-flash"] = 65_536,
        ["gemini-3.8-flash"] = 65_536,

        // DeepSeek — api-docs.deepseek.com/api/create-chat-completion, 2026-09-19: "between 1 and
        // 384K (393216)", reasoning inside; the retired V4 Flash names are routed to the same model.
        ["deepseek-flash"] = 393_216,
        ["deepseek-v4-flash"] = 393_216,
        ["deepseek-v4-flash-vision-exp"] = 393_216,

        // Kimi — platform.kimi.ai/docs/guide/kimi-k3-quickstart, 2026-09-19: max_completion_tokens
        // defaults to 131,072, real bound 1M − prompt. K2.6 documents no cap, only
        // "256K − prompt_tokens" (troubleshooting page): half its window — the K3 default carried
        // over — is a pin, not a documented maximum; a prompt above 128K gets the field dropped on
        // the retry. The 4096 the engine used to send starved K3 into empty answers.
        ["kimi-k3"] = 131_072,
        ["kimi-k2.6"] = 131_072,

        // Qwen — alibabacloud.com/help/en/model-studio/{qwen3-7-plus,qwen3-8-flash,qwen3-8-max},
        // 2026-09-19: 131,072 in both modes; the chain of thought has its own thinking_budget.
        ["qwen3.7-plus"] = 131_072,
        ["qwen3.8-flash"] = 131_072,
        ["qwen3.8-max"] = 131_072,

        // Z.AI — docs.z.ai/guides/overview/concept-param, 2026-09-19: max_tokens maximum 131072
        // on the GLM-5 family (default 65,536); 32,768 on GLM-4.6V-Flash.
        ["glm-5.2"] = 131_072,
        ["glm-5.3"] = 131_072,
        ["glm-5.3-flash"] = 131_072,
        ["glm-4.6v-flash"] = 32_768,

        // MiniMax — platform.minimax.io/docs/guides/models-intro, 2026-09-19: M2 "128k (including
        // CoT)". M3 is deliberately absent: the vendor publishes no output figure (512k appears
        // only as a benchmark setting), so M3 stays on the fallback until it does.
        ["MiniMax-M2"] = 131_072,

        // xAI — docs.x.ai/developers/rest-api-reference/inference/chat-completions, 2026-09-19: no
        // per-model cap; max_completion_tokens "defaults to 128,000 when unset", reasoning excluded.
        // The vendor's own default, written explicitly.
        ["grok-4.6"] = 128_000,

        // Mistral — docs.mistral.ai/api/endpoint/chat, 2026-09-19: no output cap, only "the token
        // count of your prompt plus max_tokens cannot exceed the model's context length" (256K on
        // medium-2604, which the model card names mistral-medium-3-5). A fixed value near the
        // window fails on any real prompt; leaving the field out lets the model write to its window.
        ["mistral-medium-2604"] = Unbounded,
        ["mistral-medium-3-5"] = Unbounded,

        // Together — docs.together.ai/docs/serverless-models: no per-model output cap, the context
        // is the bound, and a request whose prompt + max_tokens exceed it is refused. From
        // 2026-09-19 to 2026-09-21 the window itself sat here as the cap, on the assumption that
        // context_length_exceeded_behavior: "truncate" clamps it to window − prompt. The campaign
        // of 2026-09-21 measured otherwise: the TGI engine (Llama-3.3-70B, "`inputs` tokens +
        // `max_new_tokens` must be <= 131073") and the vLLM engine (Qwen3.5-9B, "Requested token
        // count exceeds the model's maximum context length") refuse regardless of the flag, and
        // the GLM-5.3-Flash engine honours it on the buffered path only. No entry: the fallback
        // holds, as it did through every green Together campaign, and the retry net drops it on
        // those wordings. Omitting the field would be worse — Together then stops at 2048 tokens
        // (finish_reason: length, measured the same day).

        // Mammouth — api.mammouth.ai/public/models, 2026-09-19 (max_output_tokens per model, the
        // proxy's own figures where they differ from the vendor's; its default when the field is
        // omitted is 2048, so the fallback is never left to it).
        ["mammouth:deepseek-v4-flash"] = 384_000,
        ["mammouth:qwen3.7-plus"] = 65_500,
        ["mammouth:claude-fable-5.1"] = 128_000,
        ["mammouth:minimax-m3"] = 512_000,
    };

    /// <summary>
    /// The documented maximum output tokens of <paramref name="model"/>, <see cref="Unbounded"/>
    /// when the vendor documents no cap, or null when the catalogue does not know the model.
    /// </summary>
    /// <param name="model">The model id as the endpoint expects it.</param>
    /// <param name="provider">The provider key (<see cref="LlmProviderKeys"/>), for an entry that only holds on that endpoint.</param>
    public static int? MaxOutputTokens(string? model, string? provider = null)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        var id = model.Trim();

        if (!string.IsNullOrWhiteSpace(provider) && Lookup($"{provider.Trim()}:{id}") is { } onThisEndpoint)
            return onThisEndpoint;

        if (Lookup(id) is { } known)
            return known;

        var slash = id.LastIndexOf('/');
        return slash >= 0 && slash < id.Length - 1 ? Lookup(id[(slash + 1)..]) : null;
    }

    private static int? Lookup(string id)
    {
        if (Documented.TryGetValue(id, out var exact))
            return exact;

        int? best = null;
        var bestLength = 0;
        foreach (var (key, value) in Documented)
        {
            if (key.Length <= bestLength || key.Length >= id.Length)
                continue;
            if (!id.StartsWith(key, StringComparison.OrdinalIgnoreCase) || !IsSeparator(id[key.Length]))
                continue;

            best = value;
            bestLength = key.Length;
        }

        return best;
    }

    // A family prefix only claims an id at a boundary: a date, a size or a routing suffix.
    private static bool IsSeparator(char c) => c is '-' or ':' or '@' or '_';
}
