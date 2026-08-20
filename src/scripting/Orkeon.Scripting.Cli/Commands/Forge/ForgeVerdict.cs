using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>One diagnosis finding, tied to the acceptance criterion it judges (SPEC §7.4).</summary>
internal sealed record ForgeFinding
{
    /// <summary>Stable id (<c>F1</c>, <c>F2</c>, …).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary><c>blocking</c> | <c>major</c> | <c>minor</c>.</summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; init; }

    /// <summary>The acceptance criterion this finding is about (<c>A1</c>…), when one applies.</summary>
    [JsonPropertyName("acceptance")]
    public string? Acceptance { get; init; }

    /// <summary>What was found, as a verifiable sentence.</summary>
    [JsonPropertyName("statement")]
    public string? Statement { get; init; }

    /// <summary>Where in the output the finding shows.</summary>
    [JsonPropertyName("evidence")]
    public string? Evidence { get; init; }
}

/// <summary>One concrete fix proposal the refine cycle can act on.</summary>
internal sealed record ForgeSuggestion
{
    /// <summary><c>agent:&lt;key&gt;</c>, <c>task:&lt;key&gt;</c> or <c>crew</c>.</summary>
    [JsonPropertyName("target")]
    public string? Target { get; init; }

    /// <summary>The change, phrased for the assistant to apply.</summary>
    [JsonPropertyName("change")]
    public string? Change { get; init; }

    /// <summary>Why it should help.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// The diagnosis of one test run (SPEC-ORKEON-FORGE §7.4, §10): a score, the findings tied
/// to the brief's acceptance criteria, and the fixes a refine would apply. <c>Judge</c>
/// says how it was produced — a degraded verdict announces itself, it never fakes a score.
/// </summary>
internal sealed record ForgeVerdict
{
    /// <summary>The judge ran on an LLM.</summary>
    public const string JudgeLlm = "llm";

    /// <summary>No LLM judge was available; only the mechanical checks spoke.</summary>
    public const string JudgeDeterministic = "deterministic";

    /// <summary>0..1.</summary>
    [JsonPropertyName("score")]
    public double Score { get; init; }

    /// <summary>The arbitration outcome: score above threshold and no blocking finding.</summary>
    [JsonPropertyName("passing")]
    public bool Passing { get; init; }

    /// <summary>What was found, worst first.</summary>
    [JsonPropertyName("findings")]
    public IReadOnlyList<ForgeFinding> Findings { get; init; } = [];

    /// <summary>What a refine should change.</summary>
    [JsonPropertyName("suggestions")]
    public IReadOnlyList<ForgeSuggestion> Suggestions { get; init; } = [];

    /// <summary><see cref="JudgeLlm"/> or <see cref="JudgeDeterministic"/>.</summary>
    [JsonPropertyName("judge")]
    public string Judge { get; init; } = JudgeDeterministic;

    /// <summary>The conformity threshold (SPEC §10).</summary>
    public const double PassingThreshold = 0.7;

    /// <summary>True when a finding blocks regardless of score.</summary>
    [JsonIgnore]
    public bool HasBlockingFinding =>
        Findings.Any(f => string.Equals(f.Severity, "blocking", StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parses a judge response. Tolerates a fenced code block around the JSON — models do
    /// that — and clamps the score into 0..1. Errors, never exceptions.
    /// </summary>
    public static bool TryParse(string text, out ForgeVerdict? verdict, out IReadOnlyList<string> errors)
    {
        verdict = null;

        var json = Unfence(text);
        try
        {
            verdict = JsonSerializer.Deserialize<ForgeVerdict>(json, ParseOptions);
        }
        catch (JsonException ex)
        {
            errors = [$"The verdict is not valid JSON: {ex.Message}"];
            return false;
        }

        if (verdict is null)
        {
            errors = ["The verdict is empty."];
            return false;
        }

        verdict = verdict with
        {
            Score = Math.Clamp(verdict.Score, 0.0, 1.0),
            Judge = JudgeLlm,
        };
        // Passing is recomputed here, never trusted: the threshold rule belongs to the
        // engine side of the seam.
        verdict = verdict with { Passing = verdict.Score >= PassingThreshold && !verdict.HasBlockingFinding };

        errors = [];
        return true;
    }

    /// <summary>Strips a Markdown code fence when the whole payload sits inside one.</summary>
    private static string Unfence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }
}
