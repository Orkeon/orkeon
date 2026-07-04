namespace Orkeon.Cli.Scripting.Dispatch;

#pragma warning disable IDE1006 // camelCase: reflected to JS as the `progress` field of a command view
/// <summary>
/// Optional progress snapshot an agent can publish while a command is in flight (design
/// §6, §10 open point). Surfaced by <c>inspect</c> and <c>ps</c>.
/// </summary>
public sealed class CommandProgress
{
    public CommandProgress(int? step, double? percent, string? message)
    {
        this.step = step;
        this.percent = percent;
        this.message = message;
    }

    /// <summary>Current step index (1-based), when the agent reports discrete steps.</summary>
    public int? step { get; }

    /// <summary>Completion ratio in the range [0, 100], when the agent reports a percentage.</summary>
    public double? percent { get; }

    /// <summary>Free-form status line (e.g. "Pre-flight checks").</summary>
    public string? message { get; }
}
#pragma warning restore IDE1006
