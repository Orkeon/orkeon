using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Capabilities;

/// <summary>
/// The built-in table of model capabilities, plus the host's overrides (<c>Llm:ModelCapabilities:&lt;id&gt;</c>).
/// An entry is a model id prefix that matches at a boundary; an override merges with the built-in value of the
/// same id field by field; the longest prefix of the model asked wins, case-insensitive. Like
/// <c>ModelPricingRegistry</c>, the table is in code: a model the vendors ship after this build is added by
/// an override, never by editing a file.
/// </summary>
[Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class ModelCapabilityRegistry : IModelCapabilityRegistry
{
    private static readonly string[] s_none = [];
    private static readonly string[] s_lowToHigh = ["low", "medium", "high"];
    private static readonly string[] s_minimalToHigh = ["minimal", "low", "medium", "high"];
    private static readonly string[] s_highOnly = ["high"];
    private static readonly string[] s_noneToXhigh = ["none", "low", "medium", "high", "xhigh"];
    private static readonly string[] s_lowToXhigh = ["low", "medium", "high", "xhigh"];
    private static readonly string[] s_noneToHigh = ["none", "low", "medium", "high"];
    private static readonly string[] s_minimalToXhigh = ["minimal", "low", "medium", "high", "xhigh"];
    private static readonly string[] s_minimalToMax = ["minimal", "low", "medium", "high", "xhigh", "max"];
    private static readonly string[] s_noneToXhighWithMinimal = ["none", "minimal", "low", "medium", "high", "xhigh"];
    private static readonly string[] s_noneOrHigh = ["none", "high"];
    private static readonly string[] s_lowHighMax = ["low", "high", "max"];

    private readonly Dictionary<string, ModelCapabilities> _entries;

    /// <summary>The built-in table alone, with no override.</summary>
    public static ModelCapabilityRegistry Default { get; } = new();

    /// <summary>Builds the registry over the built-in table, an entry of <paramref name="overrides"/> replacing a built-in of the same id.</summary>
    /// <param name="overrides">The host's entries by model id (a prefix), or null for none.</param>
    public ModelCapabilityRegistry(IReadOnlyDictionary<string, ModelCapabilityOverride>? overrides = null)
    {
        var builtIn = BuiltIn();
        _entries = new Dictionary<string, ModelCapabilities>(builtIn, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, entry) in overrides ?? new Dictionary<string, ModelCapabilityOverride>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(entry);
            // Field by field: a field the override leaves null keeps what the built-in table says of that id.
            var baseline = Lookup(builtIn, id.Trim());
            _entries[id.Trim()] = new ModelCapabilities
            {
                AudioInput = entry.AudioInput ?? baseline?.AudioInput ?? false,
                AudioOutput = entry.AudioOutput ?? baseline?.AudioOutput ?? false,
                Vision = entry.Vision ?? baseline?.Vision ?? false,
                ReasoningEfforts = entry.ReasoningEfforts ?? baseline?.ReasoningEfforts,
                Thinking = entry.Thinking ?? baseline?.Thinking,
            };
        }
    }

    /// <inheritdoc />
    public ModelCapabilities? TryGet(string? model) => Lookup(_entries, model);

    /// <inheritdoc />
    public ModelCapabilities? TryGet(string? model, string? provider)
    {
        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(model)
            && Longest(_entries, $"{provider.Trim()}/{model}") is { } served)
        {
            return served;
        }

        return Lookup(_entries, model);
    }

    /// <summary>
    /// The entry for <paramref name="model"/>: the id whole first, then, for an aggregator's
    /// <c>vendor/model</c> id (OpenRouter, Together, Hugging Face), the part after its last <c>/</c>.
    /// </summary>
    private static ModelCapabilities? Lookup(Dictionary<string, ModelCapabilities> entries, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        if (Longest(entries, model) is { } whole)
            return whole;

        var slash = model.LastIndexOf('/');
        return slash >= 0 && slash < model.Length - 1 ? Longest(entries, model[(slash + 1)..]) : null;
    }

    private static ModelCapabilities? Longest(Dictionary<string, ModelCapabilities> entries, string model)
    {
        ModelCapabilities? best = null;
        var bestLength = 0;
        foreach (var (id, entry) in entries)
        {
            if (id.Length > bestLength && MatchesAtBoundary(model, id))
            {
                best = entry;
                bestLength = id.Length;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether <paramref name="id"/> is a prefix of <paramref name="model"/> that ends at a boundary: the end of
    /// the model id, or a separator right after the prefix (<c>glm-5.3</c> covers <c>glm-5.3-flash</c>, never
    /// <c>glm-5.30</c>). A key that itself ends with a separator (<c>claude-</c>) covers any continuation.
    /// </summary>
    internal static bool MatchesAtBoundary(string model, string id)
    {
        if (!model.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            return false;

        return model.Length == id.Length || IsSeparator(id[^1]) || IsSeparator(model[id.Length]);
    }

    private static bool IsSeparator(char c) => c is '-' or '.' or ':' or '@' or '/';

    /// <summary>
    /// The model families the vendors document, and the values the provider campaign measured (each comment
    /// dates its source). An entry says what the family accepts; a snapshot suffix needs no entry of its own,
    /// the prefix covers it. The next campaign re-checks the measured rows: a vendor changes them without notice.
    /// </summary>
    private static Dictionary<string, ModelCapabilities> BuiltIn() => new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI chat models: no reasoning pass, so no thinking and no effort.
        ["gpt-3.5"] = new() { Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gpt-4"] = new() { Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gpt-4-turbo"] = new() { Vision = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gpt-4o"] = new() { Vision = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gpt-4.1"] = new() { Vision = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        // OpenAI audio models take audio parts and answer in audio.
        ["gpt-4o-audio"] = new() { AudioInput = true, AudioOutput = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gpt-4o-mini-audio"] = new() { AudioInput = true, AudioOutput = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gpt-audio"] = new() { AudioInput = true, AudioOutput = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        // OpenAI reasoning models.
        ["o1"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["o1-mini"] = new() { ReasoningEfforts = s_none },
        ["o1-preview"] = new() { ReasoningEfforts = s_none },
        ["o3"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["o3-mini"] = new() { ReasoningEfforts = s_lowToHigh },
        ["o4-mini"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["gpt-5"] = new() { Vision = true, ReasoningEfforts = s_minimalToHigh },
        ["gpt-5-chat"] = new() { Vision = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gpt-5-pro"] = new() { Vision = true, ReasoningEfforts = s_highOnly },
        // The 5.6 and 6.x tiers take `none` (function tools on chat/completions need it) up to `xhigh`, and refuse
        // `minimal` and `max`; `gpt-6-astra` and `gpt-6.1-sol` refuse `none` too (campaign M18, 2026-11-13).
        ["gpt-5.6"] = new() { Vision = true, ReasoningEfforts = s_noneToXhigh },
        ["gpt-6"] = new() { Vision = true, ReasoningEfforts = s_noneToXhigh },
        ["gpt-6-astra"] = new() { Vision = true, ReasoningEfforts = s_lowToXhigh },
        ["gpt-6.1"] = new() { Vision = true, ReasoningEfforts = s_noneToXhigh },
        ["gpt-6.1-sol"] = new() { Vision = true, ReasoningEfforts = s_lowToXhigh },
        // Gemini, through its OpenAI-compatible layer: audio and images in; the 2.0 and 1.5 lines do not reason.
        ["gemini-"] = new() { AudioInput = true, Vision = true },
        ["gemini-1.5"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gemini-2.0"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.None, ReasoningEfforts = s_none },
        ["gemini-2.5"] = new() { AudioInput = true, Vision = true, ReasoningEfforts = s_lowToHigh },
        // The 3.7, 3.8 and 4 lines take `none` to `high` and refuse `minimal`, `xhigh` and `max`; the 3.7 and 3.8
        // flash models refuse to stop reasoning ("Reasoning is mandatory", through OpenRouter) (M18, 2026-11-13).
        ["gemini-3.7"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_noneToHigh },
        ["gemini-3.8"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_noneToHigh },
        ["gemini-4"] = new() { AudioInput = true, Vision = true, ReasoningEfforts = s_noneToHigh },
        // Anthropic: images in, no audio. The 5 generation takes the five levels; the 4.x one knows none above `high`.
        ["claude-"] = new() { Vision = true },
        ["claude-opus-4"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["claude-sonnet-4"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["claude-haiku-4"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        // Opus 5.5 and the Fable models refuse every off value, `disabled` and `between_tools` (M18, 2026-11-13).
        ["claude-opus-5-5"] = new() { Vision = true, Thinking = ThinkingSupport.EffortOnly },
        ["claude-fable-5"] = new() { Vision = true, Thinking = ThinkingSupport.EffortOnly },
        // Grok 4.6 and 4.7 take `minimal` to `xhigh`, and refuse `none` and `max` (M18, 2026-11-13).
        ["grok-4.6"] = new() { Vision = true, ReasoningEfforts = s_minimalToXhigh },
        ["grok-4.7"] = new() { Vision = true, ReasoningEfforts = s_minimalToXhigh },
        // Mistral: the large model takes `low` to `high`; medium and small take `none` and `high` only, and
        // answer 400 "supported values: [high, none]" to the rest (M18, 2026-11-13; large changed since 2026-10-07).
        ["mistral-large-4"] = new() { ReasoningEfforts = s_lowToHigh },
        ["mistral-medium-2604"] = new() { ReasoningEfforts = s_noneOrHigh },
        ["mistral-small-2603"] = new() { ReasoningEfforts = s_noneOrHigh },
        // Qwen 3.7 Plus takes every value but `max`; the 3.8 models take them all (M18, 2026-11-13).
        ["qwen3.7-plus"] = new() { ReasoningEfforts = s_noneToXhighWithMinimal },
        // Kimi K2.7 Code always reasons ("only type=enabled is allowed") and refuses `none` (M18, 2026-11-13).
        ["kimi-k2.7-code"] = new() { Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_minimalToMax },
        // GLM-5.3 always reasons and takes `low`, `high` and `max` only: 400 code 1210 otherwise (#354, M18 2026-11-13).
        ["glm-5.3"] = new() { Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_lowHighMax },
        // MiniMax M3.1 Flash refuses both the off switch and `none` (M18, 2026-11-13).
        ["MiniMax-M3.1-Flash"] = new() { Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_minimalToMax },

        // A gateway that maps a model's values its own way: `<provider>/<model>`, read before the vendor's entry.
        // Mammouth and OpenRouter take `minimal` to `max` on Gemini 3.7 and 3.8 and refuse `none`, where Google
        // takes `none` to `high` (M18, 2026-11-13); OpenRouter also takes a budget there, but refuses the off switch.
        ["mammouth/gemini-3.7"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_minimalToMax },
        ["mammouth/gemini-3.8"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_minimalToMax },
        ["openrouter/google/gemini-3.7"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_minimalToMax },
        ["openrouter/google/gemini-3.8"] = new() { AudioInput = true, Vision = true, Thinking = ThinkingSupport.EffortOnly, ReasoningEfforts = s_minimalToMax },
        // Together serves GLM-5.3 with every value and both switches, where Z.AI refuses most (M18, 2026-11-13).
        ["together/zai-org/glm-5.3"] = new() { Vision = true },
    };
}
