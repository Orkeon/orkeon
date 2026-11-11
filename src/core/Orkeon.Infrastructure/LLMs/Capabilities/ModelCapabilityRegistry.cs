using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.LLMs.Capabilities;

/// <summary>
/// The built-in table of model capabilities, plus the host's overrides (<c>Llm:ModelCapabilities:&lt;id&gt;</c>).
/// An entry is a model id prefix; an override merges with the built-in value of the same id field by field; the longest prefix of the model asked wins, case-insensitive. Like
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
            var baseline = Longest(builtIn, id.Trim());
            _entries[id.Trim()] = new ModelCapabilities
            {
                AudioInput = entry.AudioInput ?? baseline?.AudioInput ?? false,
                AudioOutput = entry.AudioOutput ?? baseline?.AudioOutput ?? false,
                Vision = entry.Vision ?? baseline?.Vision ?? false,
                ReasoningEfforts = entry.ReasoningEfforts ?? baseline?.ReasoningEfforts,
            };
        }
    }

    /// <inheritdoc />
    public ModelCapabilities? TryGet(string? model) => Longest(_entries, model);

    private static ModelCapabilities? Longest(Dictionary<string, ModelCapabilities> entries, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        ModelCapabilities? best = null;
        var bestLength = 0;
        foreach (var (id, entry) in entries)
        {
            if (id.Length > bestLength && model.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            {
                best = entry;
                bestLength = id.Length;
            }
        }

        return best;
    }

    /// <summary>
    /// The model families the vendors document (checked at the build date of this table). An entry says what
    /// the family accepts; a snapshot suffix needs no entry of its own, the prefix covers it.
    /// </summary>
    private static Dictionary<string, ModelCapabilities> BuiltIn() => new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI chat models: no reasoning pass, so no effort.
        ["gpt-3.5"] = new() { ReasoningEfforts = s_none },
        ["gpt-4"] = new() { ReasoningEfforts = s_none },
        ["gpt-4-turbo"] = new() { Vision = true, ReasoningEfforts = s_none },
        ["gpt-4o"] = new() { Vision = true, ReasoningEfforts = s_none },
        ["gpt-4.1"] = new() { Vision = true, ReasoningEfforts = s_none },
        // OpenAI audio models take audio parts and answer in audio.
        ["gpt-4o-audio"] = new() { AudioInput = true, AudioOutput = true, ReasoningEfforts = s_none },
        ["gpt-4o-mini-audio"] = new() { AudioInput = true, AudioOutput = true, ReasoningEfforts = s_none },
        ["gpt-audio"] = new() { AudioInput = true, AudioOutput = true, ReasoningEfforts = s_none },
        // OpenAI reasoning models.
        ["o1"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["o1-mini"] = new() { ReasoningEfforts = s_none },
        ["o1-preview"] = new() { ReasoningEfforts = s_none },
        ["o3"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["o3-mini"] = new() { ReasoningEfforts = s_lowToHigh },
        ["o4-mini"] = new() { Vision = true, ReasoningEfforts = s_lowToHigh },
        ["gpt-5"] = new() { Vision = true, ReasoningEfforts = s_minimalToHigh },
        ["gpt-5-chat"] = new() { Vision = true, ReasoningEfforts = s_none },
        ["gpt-5-pro"] = new() { Vision = true, ReasoningEfforts = s_highOnly },
        // The 5.6 tiers are no `gpt-5`: they take `none` (function tools on chat/completions need it), measured
        // live on 2026-10-07 and 2026-11-11. Their efforts are left unrestricted, as for a model the table does not know.
        ["gpt-5.6"] = new() { Vision = true },
        // Gemini, through its OpenAI-compatible layer: audio and images in; the 2.0 and 1.5 lines do not reason.
        ["gemini-"] = new() { AudioInput = true, Vision = true },
        ["gemini-1.5"] = new() { AudioInput = true, Vision = true, ReasoningEfforts = s_none },
        ["gemini-2.0"] = new() { AudioInput = true, Vision = true, ReasoningEfforts = s_none },
        ["gemini-2.5"] = new() { AudioInput = true, Vision = true, ReasoningEfforts = s_lowToHigh },
        // Anthropic: images in, no audio. Its effort field is judged by the provider's own declared levels.
        ["claude-"] = new() { Vision = true },
    };
}
