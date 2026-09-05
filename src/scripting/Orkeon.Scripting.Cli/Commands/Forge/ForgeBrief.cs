using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>One named input of the problem (SPEC-ORKEON-FORGE §7.2).</summary>
internal sealed record ForgeBriefInput
{
    /// <summary>Variable name, as the crew will receive it.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>What the value means.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>A realistic example value.</summary>
    [JsonPropertyName("example")]
    public string? Example { get; init; }
}

/// <summary>What the user expects out (format + description).</summary>
internal sealed record ForgeExpectedOutput
{
    /// <summary><c>markdown</c> | <c>json</c> | <c>text</c> | <c>file</c>.</summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    /// <summary>The expectation, in the user's words.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// One acceptance criterion — the pivot of the whole cycle: captured before generation,
/// judged after the test, in the same words.
/// </summary>
internal sealed record ForgeAcceptanceCriterion
{
    /// <summary>Stable id (<c>A1</c>, <c>A2</c>, …) the diagnosis refers back to.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The criterion, phrased so a judge can verify it.</summary>
    [JsonPropertyName("statement")]
    public string? Statement { get; init; }

    /// <summary><c>must</c> (blocking when missed) or <c>should</c>.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }
}

/// <summary>The sample input the sandboxed test will run on.</summary>
internal sealed record ForgeSample
{
    /// <summary>Values for the crew's <c>-V</c> variables.</summary>
    [JsonPropertyName("variables")]
    public Dictionary<string, string>? Variables { get; init; }

    /// <summary>Initial context handed to the crew.</summary>
    [JsonPropertyName("initialContext")]
    public string? InitialContext { get; init; }
}

/// <summary>
/// The structured brief the interview produces (SPEC-ORKEON-FORGE §7.2), submitted by the
/// assistant through <c>brief_submit</c> and validated here — never trusted on format alone.
/// </summary>
internal sealed record ForgeBrief
{
    /// <summary>What the crew must accomplish.</summary>
    [JsonPropertyName("goal")]
    public string? Goal { get; init; }

    /// <summary>Domain, business constraints.</summary>
    [JsonPropertyName("context")]
    public string? Context { get; init; }

    /// <summary>The problem's named inputs.</summary>
    [JsonPropertyName("inputs")]
    public IReadOnlyList<ForgeBriefInput>? Inputs { get; init; }

    /// <summary>What comes out.</summary>
    [JsonPropertyName("expectedOutput")]
    public ForgeExpectedOutput? ExpectedOutput { get; init; }

    /// <summary>Tone, length, language, allowed sources…</summary>
    [JsonPropertyName("constraints")]
    public IReadOnlyList<string>? Constraints { get; init; }

    /// <summary>Needs the user voiced ("read PDFs", "call an API") — hints, not tool names.</summary>
    [JsonPropertyName("toolHints")]
    public IReadOnlyList<string>? ToolHints { get; init; }

    /// <summary>The acceptance criteria. The only interview question that refuses silence.</summary>
    [JsonPropertyName("acceptance")]
    public IReadOnlyList<ForgeAcceptanceCriterion>? Acceptance { get; init; }

    /// <summary>The test's sample input.</summary>
    [JsonPropertyName("sample")]
    public ForgeSample? Sample { get; init; }

    /// <summary><c>fr</c> | <c>en</c>.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parses a submitted brief. A malformed document is a list of errors, never an
    /// exception — the errors go straight back into the repair prompt.
    /// </summary>
    public static bool TryParse(string json, out ForgeBrief? brief, out IReadOnlyList<string> errors)
    {
        brief = null;

        try
        {
            brief = JsonSerializer.Deserialize<ForgeBrief>(json, ParseOptions);
        }
        catch (JsonException ex)
        {
            errors = [$"The brief is not valid JSON: {ex.Message}"];
            return false;
        }

        if (brief is null)
        {
            errors = ["The brief is empty."];
            return false;
        }

        errors = brief.Validate();
        return errors.Count == 0;
    }

    /// <summary>The structural rules a brief must satisfy before the cycle may continue.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Goal))
            errors.Add("'goal' is required: the brief must say what the crew accomplishes.");

        ValidateAcceptance(errors);

        if (Language is not (null or "fr" or "en"))
            errors.Add($"'language' must be 'fr' or 'en', not '{Language}'.");

        return errors;
    }

    /// <summary>The criteria the diagnosis will judge against — the one list that refuses silence.</summary>
    private void ValidateAcceptance(List<string> errors)
    {
        if (Acceptance is not { Count: > 0 })
        {
            errors.Add("'acceptance' must hold at least one criterion — it is what the diagnosis will judge against.");
            return;
        }

        for (var i = 0; i < Acceptance.Count; i++)
            ValidateCriterion(errors, i, Acceptance[i]);
    }

    private static void ValidateCriterion(List<string> errors, int index, ForgeAcceptanceCriterion criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion.Id))
            errors.Add($"acceptance[{index}]: 'id' is required (A1, A2, …).");
        if (string.IsNullOrWhiteSpace(criterion.Statement))
            errors.Add($"acceptance[{index}]: 'statement' is required.");
        if (criterion.Kind is not ("must" or "should"))
            errors.Add($"acceptance[{index}]: 'kind' must be 'must' or 'should', not '{criterion.Kind}'.");
    }
}
