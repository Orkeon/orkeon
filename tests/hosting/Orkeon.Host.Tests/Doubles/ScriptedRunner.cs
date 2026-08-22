namespace Orkeon.Host.Tests.Doubles;

/// <summary>A runner that answers however the test asks it to, without a crew or a model.</summary>
internal sealed class ScriptedRunner : ICrewRunner
{
    public HostedRunResult Result { get; set; } = new(HostedRunOutcome.Completed, "run-1", "done");

    public List<string> Ran { get; } = [];

    /// <summary>
    /// Whether the scripted run reaches admission — a refusal (UnknownCrew, Busy) never
    /// invokes <c>onStarted</c>, and the gateway's acknowledgement rides on it.
    /// </summary>
    public bool Admits { get; set; } = true;

    public Func<Action<string>?, Func<string, Task>?, Task>? Behaviour { get; set; }

    public async Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Func<string, Task>? onStarted = null,
        CancellationToken cancellationToken = default)
    {
        Ran.Add($"{crewName}:{prompt}");

        if (Admits && onStarted is not null)
            await onStarted(Result.RunId ?? "run-1");

        if (Behaviour is not null)
            await Behaviour(onProgress, onStarted);

        return Result;
    }
}
