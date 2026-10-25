namespace Orkeon.Tools.Code.Sessions;

/// <summary>A command started detached: its id, its exit code once it ends, and how to kill it.</summary>
internal sealed class ShellJobHandle
{
    private readonly Action _kill;

    /// <summary>A handle over a started job.</summary>
    /// <param name="id">The job's id.</param>
    /// <param name="exit">Completes with the exit code when the job ends.</param>
    /// <param name="kill">Kills the job.</param>
    public ShellJobHandle(string id, Task<int> exit, Action kill)
    {
        Id = id;
        Exit = exit;
        _kill = kill;
    }

    /// <summary>The job's id.</summary>
    public string Id { get; }

    /// <summary>Completes with the exit code when the job ends.</summary>
    public Task<int> Exit { get; }

    /// <summary>The current in-memory output text (last 64 KiB), or null when the session keeps none.</summary>
    public Func<ShellLiveTail?>? LiveTail { get; init; }

    /// <summary>Kills the job.</summary>
    public void Kill() => _kill();
}
