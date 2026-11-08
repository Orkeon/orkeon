using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Decision;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Refuses a value of <c>Orkeon:Guardian:Llm:Phases</c> that is not <c>Input</c> or <c>Output</c>. The
/// configuration binder drops a value it cannot convert, so the raw values are read: a typo, or a phase a
/// model does not judge (<c>ToolExecution</c>, <c>Delegation</c>), would otherwise leave the judge on a
/// different set of phases than the file says.
/// </summary>
internal sealed class GuardianLlmPhasesValidator(IConfiguration? configuration = null) : IValidateOptions<GuardianOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GuardianOptions options)
    {
        var values = configuration?.GetSection($"{GuardianLlmOptions.SectionName}:Phases").GetChildren()
            .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];
        var refused = values.Where(v => !Enum.TryParse<GuardianJudgePhase>(v, ignoreCase: true, out var phase) || !Enum.IsDefined(phase)).ToList();
        return refused.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{GuardianLlmOptions.SectionName}:Phases: '{string.Join("', '", refused)}' is not a phase a model judges. " +
                "Only `Input` and `Output` are (`ToolExecution` and `Delegation` are checked by rules); " +
                "remove the other values or leave the list empty to judge both.");
    }
}
