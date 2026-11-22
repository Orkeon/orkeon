using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Decision;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Refuses a value of <c>Orkeon:Guardian:Llm:Phases</c> that is not <c>Input</c> or <c>Output</c>. The
/// configuration binder drops a value it cannot convert, so the raw values are read: a typo, or a phase a
/// model does not judge (<c>ToolExecution</c>, <c>Delegation</c>), would otherwise leave the judge on a
/// different set of phases than the file says. A phase is written by its name, never as a number (#455),
/// and the list as a list: a single value bound an empty one, which judges both phases.
/// </summary>
internal sealed class GuardianLlmPhasesValidator(IConfiguration? configuration = null) : IValidateOptions<GuardianOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GuardianOptions options)
    {
        var section = configuration?.GetSection($"{GuardianLlmOptions.SectionName}:Phases");
        // A single value binds an empty list, which judges both phases; an empty JSON array reads as "".
        // The value is not echoed (#465): written where a list is expected, it may be anything.
        if (section is { Value.Length: > 0 } && !section.GetChildren().Any())
        {
            return ValidateOptionsResult.Fail(
                $"{section.Path} is a single value where a list is expected: write its items as a list, " +
                "[\"Input\"] (the environment variable Orkeon__Guardian__Llm__Phases__0=Input), or leave the key out to judge both phases.");
        }

        var values = section?.GetChildren()
            .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];
        var refused = values.Where(v => !EnumSetting.TryParseName<GuardianJudgePhase>(v, out _)).ToList();
        return refused.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{GuardianLlmOptions.SectionName}:Phases: '{string.Join("', '", refused)}' is not a phase a model judges. " +
                "Only `Input` and `Output` are (`ToolExecution` and `Delegation` are checked by rules); " +
                "remove the other values or leave the list empty to judge both.");
    }
}
