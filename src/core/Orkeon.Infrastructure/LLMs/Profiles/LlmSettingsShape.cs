using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// The model every call runs on unless a crew names a profile: the provider follows from
/// <c>BaseUrl</c>, then from the model's name, then from the shape of the key. Without this section a
/// host runs on the echo provider, and says so once.
/// </summary>
/// <remarks>
/// The keys of the <c>Llm</c> section, which <see cref="LlmSettings"/> reads raw (GAP-40): the
/// default profile's, its named profiles under <c>Profiles</c>, and <c>AvailableModels</c>, which the
/// session tools read. Declared by <c>AddOrkeonLlmProfiles</c>, so a key the section does not carry —
/// <c>Llm:Providr</c>, a typo — refuses the host's start, naming it. A key
/// <see cref="LlmSettings"/> starts to read is added here. Nothing binds it: its properties are the
/// keys, and their values on a new instance the defaults <see cref="LlmSettings"/> and the provider
/// apply to a key left out, each named where it is defined.
/// </remarks>
internal class LlmSectionShape : LlmProfileShape
{
    /// <summary>
    /// The named profiles, each another provider with the keys of the section itself: a crew, an
    /// agent or a task picks one by its name.
    /// </summary>
    public Dictionary<string, LlmNamedProfileShape>? Profiles { get; set; }

    /// <summary>The models a scripted <c>/model</c> may offer: a list, or one comma-separated value.</summary>
    public List<string>? AvailableModels { get; set; }

    /// <summary>
    /// What a model accepts, by model id (a prefix), overriding the built-in table (FWK-21): audio input,
    /// image input, the reasoning efforts it takes.
    /// </summary>
    public Dictionary<string, LlmModelCapabilityShape>? ModelCapabilities { get; set; }

    /// <summary>Record the calls to the models in a cassette, or answer them from it (FWK-11).</summary>
    public LlmReplayShape? Replay { get; set; }
}

/// <summary>The <c>Llm:Replay</c> section.</summary>
internal sealed class LlmReplayShape
{
    /// <summary>
    /// <c>record</c> writes every call of the run and its answer to the cassette; <c>replay</c> answers every call
    /// from it, and a call it does not hold fails; <c>off</c> does neither. Left out, <c>off</c>.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>The virtual path of the cassette, a JSONL file; <c>record</c> and <c>replay</c> need it.</summary>
    public string? Cassette { get; set; }
}

/// <summary>One <c>Llm:ModelCapabilities:&lt;id&gt;</c> entry.</summary>
internal sealed class LlmModelCapabilityShape
{
    /// <summary>Whether the model takes audio parts as input. Left out, the built-in value of the model (false when the table does not know it).</summary>
    public bool? AudioInput { get; set; }

    /// <summary>Whether the model answers in audio. Recorded only: nothing sends or reads audio output. Left out, the built-in value of the model (false when the table does not know it).</summary>
    public bool? AudioOutput { get; set; }

    /// <summary>Whether the model takes image parts as input. Left out, the built-in value of the model (false when the table does not know it).</summary>
    public bool? Vision { get; set; }

    /// <summary>
    /// The reasoning efforts the model takes: a list, or one comma-separated value; written empty, none.
    /// Left out, the built-in list of the model (no restriction for a model the table does not know) and the provider's own levels rule.
    /// </summary>
    public List<string>? ReasoningEfforts { get; set; }

    /// <summary>
    /// The model's thinking support: <c>None</c>, <c>EffortOnly</c> (always reasons, cannot be switched off), <c>Toggle</c>
    /// or <c>Budget</c>. The effective value is the lower of this and the provider's. Left out, the built-in value of the
    /// model (the provider's own for a model the table does not know).
    /// </summary>
    public string? Thinking { get; set; }
}

/// <summary>A named profile: the keys of the <c>Llm</c> shape, and the two a default profile has no use for.</summary>
internal sealed class LlmNamedProfileShape : LlmProfileShape
{
    /// <summary>
    /// The profiles a call moves to, in order, when this one fails in a way a retry cannot cure: a transport
    /// error, a 5xx, a 429 once <c>MaxRetries</c> is spent, a timeout. A list of profile names, or one
    /// comma-separated value; <c>default</c> names the host's own. Left out, a failure is the call's.
    /// </summary>
    public List<string>? Fallback { get; set; }

    /// <summary>
    /// The longest the chain may spend before it stops trying the next profile, in seconds, counted from the
    /// first call. It is checked between two profiles and never cuts a call in flight. Left out, the chain is
    /// bounded only by each profile's own <c>TimeoutSeconds</c> and <c>MaxRetries</c>.
    /// </summary>
    public int? FallbackBudgetSeconds { get; set; }
}

/// <summary>The keys of one profile of the <c>Llm</c> shape — the default section's own, a named profile's.</summary>
internal class LlmProfileShape
{
    /// <summary>The default of <see cref="Grammar"/>: no endpoint is taken to honour a grammar unless told.</summary>
    internal const bool DefaultGrammar = false;

    /// <summary>
    /// The address of the provider's endpoint, <c>http://</c> or <c>https://</c>. Left out, the
    /// address of the provider the model's name or the key points to.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// <c>mock</c>, the only value: the offline provider that answers from the JSONL file
    /// <c>Responses</c> names. Left out, the provider follows from <c>BaseUrl</c>, the model's name
    /// and the key.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// With <c>Provider: mock</c>, the virtual path of the JSONL file — one <c>{match, regex?, content,
    /// toolCalls?}</c> object per line — the mock provider answers from.
    /// </summary>
    public string? Responses { get; set; }

    /// <summary>The API key, in clear text. Prefer <c>ApiKeyEnvVar</c>: a key written here is in the settings file.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The name of the environment variable that holds the API key, read when <c>ApiKey</c> resolves
    /// none: the process environment, then the Windows user scope.
    /// </summary>
    public string? ApiKeyEnvVar { get; set; }

    /// <summary>The model's name. Left out, the provider runs its own default model.</summary>
    public string? Model { get; set; }

    /// <summary>The sampling temperature. Left out, none is sent and the model applies its own.</summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// The most tokens an answer may hold: a pin. Left out, a request carries the documented maximum
    /// of its model, and 4096 for a model the catalogue does not know.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// How long one call may take, in seconds. Too short at its default for a model that thinks
    /// before it answers: write 600 for one, or turn its thinking off.
    /// </summary>
    public int? TimeoutSeconds { get; set; } = LlmDefaults.DefaultTimeoutSeconds;

    /// <summary>
    /// The longest silence a streamed answer may hold between two of its chunks, in seconds.
    /// Left out, nothing bounds it: <c>TimeoutSeconds</c> alone bounds the whole call, streamed
    /// or not. A model that thinks before it writes may stay silent for a while: set it only
    /// above that silence, or turn its thinking off.
    /// </summary>
    public int? StreamIdleSeconds { get; set; }

    /// <summary>
    /// How a call reaches the model: <c>Auto</c>, <c>Buffer</c> or <c>Stream</c>, case ignored. Left
    /// out, <c>Auto</c>: the call streams, and falls back on a buffered call when the provider
    /// refuses streaming.
    /// </summary>
    public string? CallMode { get; set; } = "Auto";

    /// <summary>How many times a call that fails on a passing error is tried again; <c>0</c> never retries.</summary>
    public int? MaxRetries { get; set; } = LlmDefaults.DefaultMaxRetries;

    /// <summary>
    /// The longest rate-limit window a call waits out before it fails: a provider that announces a
    /// longer one makes the call fail at once with a rate-limit error. <c>00:00:00</c> waits for nothing.
    /// </summary>
    public TimeSpan? MaxRateLimitWait { get; set; } = Orkeon.Domain.Constants.Resilience.ResilienceDefaults.DefaultMaxRateLimitWait;

    /// <summary>What a provider that can think before it answers is asked to do.</summary>
    public LlmThinkingShape? Thinking { get; set; }

    /// <summary>
    /// The prompt cache every agent on a provider with explicit prompt caching (Anthropic) asks for
    /// when it sets none of its own; an agent's own <c>cache</c>, one that marks nothing included,
    /// wins. Elsewhere it is not applied: those providers cache on their own.
    /// </summary>
    public LlmCacheShape? Cache { get; set; }

    /// <summary>
    /// Whether the endpoint honours a GBNF grammar — a llama.cpp-compatible server, such as Docker
    /// Model Runner. Elsewhere a grammar is dropped with a warning.
    /// </summary>
    public bool? Grammar { get; set; } = DefaultGrammar;
}

/// <summary>The keys of <c>Llm:Thinking</c>.</summary>
internal class LlmThinkingShape
{
    /// <summary>Whether the model thinks before it answers. Left out, the provider's own behaviour.</summary>
    public bool? Enabled { get; set; }

    /// <summary>How hard it thinks, in the provider's own words (<c>low</c>, <c>medium</c>, <c>high</c>…). Left out, the provider's own.</summary>
    public string? Effort { get; set; }
}

/// <summary>The keys of <c>Llm:Cache</c>.</summary>
internal class LlmCacheShape
{
    /// <summary>
    /// Marks the system prompt cacheable for every agent that sets no cache of its own, on a provider
    /// whose prompt cache is explicit (Anthropic). Left out, it is not.
    /// </summary>
    public bool? SystemPrompt { get; set; }

    /// <summary>
    /// Marks the tool definitions cacheable for every agent that sets no cache of its own, on a provider
    /// whose prompt cache is explicit (Anthropic). Left out, they are not.
    /// </summary>
    public bool? Tools { get; set; }

    /// <summary>
    /// The cache lifetime, <c>5m</c> or <c>1h</c>; it needs <c>SystemPrompt</c> or <c>Tools</c>, and
    /// any other value refuses the load. Left out, the vendor default (5 minutes on Anthropic).
    /// </summary>
    public string? Ttl { get; set; }
}
