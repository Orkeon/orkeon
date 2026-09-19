namespace Orkeon.Constants.Llm;

/// <summary>
/// The model each provider runs when a configuration names none.
/// <para>
/// Changing one of these changes the behaviour of every configuration that does not specify a
/// model, so they are written down in one visible place rather than scattered across provider
/// implementations. Each identifier was confirmed on the vendor's own documentation.
/// </para>
/// <para>
/// They live in a satellite (ADR-009) because three components must agree on them: the provider
/// implementations, <c>orkeon init</c>, and Orkeon Studio's connection form. Studio may not
/// reference <c>Orkeon.Infrastructure</c>, so it used to declare its own copy of all twelve and
/// a drift test compared them one by one against the runtime lookup.
/// </para>
/// <para>
/// Azure OpenAI is deliberately absent: it serves deployments an operator named, so there is
/// nothing to default to.
/// </para>
/// </summary>
public static class LlmProviderDefaultModels
{
    /// <summary>
    /// OpenAI — GPT-5.6 Sol, the flagship of the 5.6 family. Also the fallback model of a
    /// configuration that names neither provider nor model, which is why the Domain default
    /// reads it from here. Catalogue review 2026-09-19 (vendor pages): <c>gpt-6-astra</c>
    /// (10 / 50 $/M, effort <c>low</c>…<c>max</c> with no <c>none</c>) sits above it and stays
    /// off the default, because the function-tools workaround the 2026-08-30 campaign pinned
    /// (<c>reasoning_effort: none</c>) has no equivalent there — a bump is a campaign away.
    /// Sol is 4 / 20 $/M "at least through 2026-11-21" and the vendor's replacement target in
    /// both 2026 deprecation waves; <c>gpt-6-astra-pro</c> is an aggregator label, not an
    /// OpenAI id.
    /// </summary>
    public const string OpenAI = "gpt-5.6-sol";

    /// <summary>
    /// Anthropic Claude (the 3.5 generation was retired 2025-10-28). Catalogue review
    /// 2026-09-19: <c>claude-sonnet-5</c> is active until at least 2027-06-30 at 2 / 10 $/M
    /// (the rise to 3 / 15 announced for 2026-09-01 was cancelled); <c>claude-fable-5-1</c>
    /// (2026-09-01, 10 / 50 $/M, thinking always on, forced <c>tool_choice</c> refused) is the
    /// new top tier and the vendor's "most workloads" pick is <c>claude-opus-5</c> — both cost
    /// more, neither is campaigned, the default stays. <c>claude-opus-5-fast</c> is a
    /// <c>speed</c> flag, not a model id.
    /// </summary>
    public const string Anthropic = "claude-sonnet-5";

    /// <summary>
    /// Ollama, the local server's small default. Catalogue review 2026-09-19: still on the
    /// library (3B, 128 K context, tools, no vision, no thinking, a year old); the same size
    /// class now offers tools + thinking (<c>qwen3.5:4b</c>, <c>gemma4:e4b</c>,
    /// <c>granite4.2:3b</c>). It stays: a new default means a pull on every machine.
    /// </summary>
    public const string Ollama = "llama3.2";

    /// <summary>
    /// Together AI. Catalogue review 2026-09-19: still serverless and not scheduled for
    /// removal, at 1.04 / 1.04 $/M; the serverless list now carries
    /// <c>zai-org/GLM-5.3-Flash</c> (1 M context, tools + JSON, 0.15 / 0.50 $/M — Together's
    /// own replacement target), <c>Qwen/Qwen3.5-9B</c> and <c>deepseek-ai/DeepSeek-V4.1-Flash</c>,
    /// while Llama 4 left it. A candidate at a seventh of the price, once campaigned.
    /// </summary>
    public const string Together = "meta-llama/Llama-3.3-70B-Instruct-Turbo";

    /// <summary>
    /// DeepSeek — the vendor's rolling id for the Flash tier, V4.1 Flash since 2026-09-10
    /// (1 M context, 384 K output, thinking on by default, native vision). DeepSeek publishes
    /// no dated snapshot, so the rolling id is the only pin there is. The previous value,
    /// <c>deepseek-v4-flash</c>, names a model the vendor retired on 2026-09-10 and
    /// "temporarily" routes here, like <c>deepseek-v4-flash-vision-exp</c> (pricing page, read
    /// 2026-09-19); <c>deepseek-chat</c> was retired 2026-07-24. The 2026-09-07 campaign
    /// measured V4 Flash under the old name — to be replayed on this one.
    /// </summary>
    public const string DeepSeek = "deepseek-flash";

    /// <summary>
    /// Kimi (Moonshot AI). Catalogue review 2026-09-19: <c>kimi-k2.6</c> is served with no
    /// retirement date (<c>kimi-k2.5</c> and the <c>moonshot-v1-*</c> series went on
    /// 2026-08-31); the flagship <c>kimi-k3</c> (1 M context, 3 / 15 $/M) fixes
    /// <c>temperature</c>, <c>top_p</c>, <c>n</c> and both penalties, refuses a
    /// <c>thinking</c> field and always reasons — a candidate once the dialect strips those
    /// and a campaign has archived an M1. The vendor docs moved to <c>platform.kimi.ai</c>.
    /// </summary>
    public const string Kimi = "kimi-k2.6";

    /// <summary>
    /// Z.AI (Zhipu GLM). Catalogue review 2026-09-19: <c>glm-5.2</c> is still listed under
    /// "Latest Models", at the price of <c>glm-5.3</c> (2026-08-18, 1.40 / 4.40 $/M). The 5.3
    /// family (<c>glm-5.3-flash</c> 0.15 / 0.50 $/M with image and video input,
    /// <c>glm-5.3-flashx</c>) cannot disable thinking, so the <c>Toggle</c> declaration needs
    /// a per-model guard before any of them becomes the default, campaign included.
    /// </summary>
    public const string Zai = "glm-5.2";

    /// <summary>
    /// Qwen (Alibaba DashScope). Catalogue review 2026-09-19: <c>qwen3.7-plus</c> is still the
    /// Plus tier — one of the three models the vendor's overview recommends, with
    /// <c>qwen3.8-max</c> (alias of <c>qwen3.8-max-0902</c>) and <c>qwen3.8-flash</c>; there
    /// is no <c>qwen3.8-plus</c>. The 3.8 generation keeps <c>enable_thinking</c> /
    /// <c>thinking_budget</c> and adds <c>preserve_thinking</c>.
    /// </summary>
    public const string Qwen = "qwen3.7-plus";

    /// <summary>
    /// Google Gemini, over its OpenAI-compatibility surface. Catalogue review 2026-09-19:
    /// <c>gemini-3.8-flash</c> went GA on 2026-09-02 at the same 0.75 / 3.75 $/M (both rise to
    /// 1.50 / 7.50 on 2027-01-01) and the vendor now labels 3.7 Flash "previous generation" —
    /// still served, no shutdown date. The first candidate for a default bump across the three
    /// transports (direct, OpenRouter, Mammouth), once campaigned.
    /// </summary>
    public const string Gemini = "gemini-3.7-flash";

    /// <summary>
    /// Mistral AI — the dated snapshot of the medium 3.5 generation. The previous value,
    /// <c>mistral-medium-3-5-26-04</c>, was never a served identifier: the API answers
    /// <c>Invalid model</c> (measured 2026-08-30), and the string reads like a concatenation
    /// of the two forms Mistral really serves — the alias <c>mistral-medium-3-5</c> and the
    /// vintage <c>2604</c>. Both were verified live the same day; the dated one is pinned,
    /// per this file's convention that a default does not drift under an alias. Catalogue
    /// review 2026-09-19: still the featured generalist (1.50 / 7.50 $/M), no retirement
    /// date, nothing newer for chat since April — <c>devstral</c> / <c>magistral</c> ids sit
    /// in the vendor's deprecated table.
    /// </summary>
    public const string Mistral = "mistral-medium-2604";


    /// <summary>
    /// Grok (x.AI) — the current chat flagship, verified live 2026-08-30 with a full 12-mode
    /// campaign (streaming, native tools, reasoning trace, vision, implicit cache).
    /// </summary>
    public const string Grok = "grok-4.6";

    /// <summary>
    /// MiniMax — the vendor's flagship, verified live 2026-08-30 with a full campaign
    /// (7 green modes on MiniMax-M2; the two reds are model-level: system prompt ignored,
    /// text-only vision). Catalogue review 2026-09-19: <c>MiniMax-M2</c> is listed as legacy
    /// with no retirement date; <c>MiniMax-M3</c> (2026-06-01, 1 M context, image and video
    /// input, the same 0.30 / 1.20 $/M) is the vendor's agentic pick, but its reasoning
    /// format on the OpenAI-compatible endpoint is undocumented — bumping the default is the
    /// maintainer's call, backed by a fresh campaign.
    /// </summary>
    public const string MiniMax = "MiniMax-M2";

    /// <summary>
    /// HuggingFace, through the Inference Providers router. Catalogue review 2026-09-19: still
    /// routed, but only one of its four providers advertises tool support and the default
    /// <c>:fastest</c> policy may land on another — which is what the routing-dependent reds
    /// of the 2026-09-07 sweep look like. <c>Qwen/Qwen3.5-9B</c> (tools on three providers,
    /// from 0.10 / 0.15 $/M) is the candidate, once campaigned.
    /// </summary>
    public const string HuggingFace = "meta-llama/Llama-3.1-8B-Instruct";

    /// <summary>
    /// OpenRouter — the fleet's Gemini default under its marketplace identifier
    /// (<c>vendor/model</c> is mandatory there). One model, three transports (LLM-09, D-03):
    /// direct Gemini is campaigned (11/0/1 on 2026-08-30), and the same model behind the
    /// aggregator isolates the transport — a red here with a green in direct is a fact about
    /// OpenRouter, not about the model. Present in the public catalog on 2026-09-18 with
    /// reasoning, <c>response_format</c> + schema, tools, vision and free
    /// <c>temperature</c>. <c>openrouter/auto</c> is refused as a default: an alias that
    /// drifts (the served model changes without notice, price -1) gives a verdict nobody
    /// can reproduce. Compiled from documentation only: a default is a claim until a
    /// campaign has archived an M1 on it — the Mistral lesson. Catalogue review 2026-09-19:
    /// <c>google/gemini-3.8-flash</c> is on the marketplace since 2026-09-02 at the same
    /// price; the aggregator defaults follow the direct one, never the other way round.
    /// </summary>
    public const string OpenRouter = "google/gemini-3.7-flash";

    /// <summary>
    /// Mammouth AI — the fleet's Gemini default under its bare identifier (Mammouth serves
    /// the vendors' own strings, no vendor prefix). Same one-model-three-transports rationale
    /// as <see cref="OpenRouter"/>; listed on <c>api.mammouth.ai/public/models</c> on
    /// 2026-09-18 at 1.5 / 7.5 $/M (twice the direct price — a market fact, not a reason to
    /// change the default). <c>mammouth-recommended</c> is refused as a default for the same
    /// reason as <c>openrouter/auto</c>. Compiled from documentation only: a default is a
    /// claim until a campaign has archived an M1 on it — the Mistral lesson, verbatim.
    /// Catalogue review 2026-09-19: <c>gemini-3.8-flash</c> is on the public list too; same
    /// rule, the direct default leads.
    /// </summary>
    public const string Mammouth = "gemini-3.7-flash";

    /// <summary>
    /// Docker Model Runner. Parity with the committed <c>examples/appsettings/appsettings.json</c>,
    /// which <c>orkeon init</c> writes and Studio offers.
    /// </summary>
    public const string DockerModelRunner = "ai/granite-4.0-h-tiny";

    /// <summary>
    /// Docker Model Runner authenticates nothing, but the OpenAI dialect wants a key. This is
    /// what the committed template and <c>orkeon init</c> both write.
    /// </summary>
    public const string DockerModelRunnerApiKeyPlaceholder = "not-needed";
}
