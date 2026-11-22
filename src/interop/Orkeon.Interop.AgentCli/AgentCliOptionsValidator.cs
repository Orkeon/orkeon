using Microsoft.Extensions.Options;

namespace Orkeon.Interop.AgentCli;

/// <summary>
/// Judges <c>Llm:AgentCli</c> when the host starts: an engine the provider does not have, a command it cannot
/// run, a limit that bounds nothing are refused there, not at the first call of a run. Each refusal names its
/// key and what it takes, never the configured value.
/// </summary>
internal sealed class AgentCliOptionsValidator : IValidateOptions<AgentCliOptions>
{
    /// <summary>The least <see cref="AgentCliOptions.MaxToolResultCharacters"/> takes: room for a result and the note that cuts it.</summary>
    internal const int MinToolResultCharacters = 1_000;

    public ValidateOptionsResult Validate(string? name, AgentCliOptions options) =>
        Refusals(options) is { Count: > 0 } refusals ? ValidateOptionsResult.Fail(refusals) : ValidateOptionsResult.Success;

    /// <summary>Every reason the section is refused, in the order of its keys; empty when it is accepted.</summary>
    internal static IReadOnlyList<string> Refusals(AgentCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var refusals = new List<string>();
        if (EngineRefusal(options) is { } engine)
            refusals.Add(engine);
        if (CommandRefusal(options) is { } command)
            refusals.Add(command);
        if (options.MaxProcesses <= 0)
            refusals.Add($"{AgentCliOptions.SectionName}:MaxProcesses takes a whole number of 1 or more: the CLI processes that may live at once.");
        if (options.MaxToolResultCharacters < MinToolResultCharacters)
            refusals.Add($"{AgentCliOptions.SectionName}:MaxToolResultCharacters takes a whole number of {MinToolResultCharacters} or more: the longest tool result handed to the CLI, in characters.");
        if (options.AutocompactTokens <= 0)
            refusals.Add($"{AgentCliOptions.SectionName}:AutocompactTokens takes a whole number of 1 or more: the context size at which the CLI would compact the conversation itself.");
        return refusals;
    }

    /// <summary>Why <see cref="AgentCliOptions.Engine"/> is refused, or null when the provider has that engine.</summary>
    internal static string? EngineRefusal(AgentCliOptions options) =>
        string.Equals(options.Engine?.Trim(), AgentCliEngines.Claude, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{AgentCliOptions.SectionName}:Engine is not an engine of the agent-cli provider: write {AgentCliEngines.Claude}, the only one.";

    /// <summary>
    /// Why <see cref="AgentCliOptions.Command"/> is refused, or null. A <c>.cmd</c> or <c>.bat</c> file runs
    /// through <c>cmd.exe</c> on Windows, which reads the JSON arguments of the command line as its own syntax.
    /// </summary>
    internal static string? CommandRefusal(AgentCliOptions options)
    {
        var command = options.Command?.Trim();
        if (string.IsNullOrEmpty(command))
            return $"{AgentCliOptions.SectionName}:Command takes the CLI's executable: a name looked up on the PATH, or a full path.";

        var extension = Path.GetExtension(command);
        return string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
            ? $"{AgentCliOptions.SectionName}:Command takes an executable, not a .cmd or .bat script, which Windows runs through cmd.exe and its parsing of the arguments: name the CLI's .exe (the native installer's claude.exe)."
            : null;
    }
}
