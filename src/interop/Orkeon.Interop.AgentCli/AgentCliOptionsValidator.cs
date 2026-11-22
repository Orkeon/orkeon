using Microsoft.Extensions.Options;

namespace Orkeon.Interop.AgentCli;

/// <summary>
/// Judges <c>Llm:AgentCli</c> when the host starts: an engine the provider does not have is refused there,
/// not at the first call of a run.
/// </summary>
internal sealed class AgentCliOptionsValidator : IValidateOptions<AgentCliOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentCliOptions options) =>
        EngineRefusal(options) is { } refusal ? ValidateOptionsResult.Fail(refusal) : ValidateOptionsResult.Success;

    /// <summary>Why <see cref="AgentCliOptions.Engine"/> is refused, or null when the provider has that engine.</summary>
    internal static string? EngineRefusal(AgentCliOptions options) =>
        string.Equals(options.Engine?.Trim(), AgentCliEngines.Claude, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{AgentCliOptions.SectionName}:Engine is not an engine of the agent-cli provider: write {AgentCliEngines.Claude}, the only one.";
}
