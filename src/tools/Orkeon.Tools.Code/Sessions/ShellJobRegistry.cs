using Orkeon.Domain.Execution;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>A job started detached: its handle and where its output goes.</summary>
/// <param name="Handle">The job's handle: id, exit and kill.</param>
/// <param name="OutputPath">The virtual path of the log the agent reads.</param>
internal sealed record ShellJob(ShellJobHandle Handle, string OutputPath);

/// <summary>Where a job stands: running, or exited with its code.</summary>
/// <param name="Running">Whether the job has not exited.</param>
/// <param name="ExitCode">The exit code once the job ended; -1 when it ended without one.</param>
internal sealed record ShellJobStatus(bool Running, int ExitCode);

/// <summary>
/// The detached jobs of one agent run, kept in the run's <see cref="AgentRunScope"/> like the shell
/// session. At most <see cref="MaxLiveJobs"/> run at once; a finished job's entry stays readable until
/// the run ends, and disposing the registry (the end of the run) kills the jobs still running.
/// </summary>
internal sealed class ShellJobRegistry : IAsyncDisposable
{
    /// <summary>The most jobs running at once in one run.</summary>
    public const int MaxLiveJobs = 16;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, ShellJob> _jobs = new(StringComparer.Ordinal);
    private int _starting;
    private bool _disposed;

    /// <summary>The registry of the current run, or null outside a run (or after it ended).</summary>
    public static ShellJobRegistry? Current()
    {
        try
        {
            return AgentRunScope.Current?.GetOrAdd(new SlotKey(), () => new ShellJobRegistry());
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The job named <paramref name="id"/>, or null.</summary>
    public ShellJob? Find(string id)
    {
        lock (_sync)
            return _jobs.GetValueOrDefault(id);
    }

    /// <summary>Whether <see cref="MaxLiveJobs"/> jobs are running or starting.</summary>
    public bool IsFull
    {
        get
        {
            lock (_sync)
                return LiveCount() >= MaxLiveJobs;
        }
    }

    /// <summary>Where <paramref name="job"/> stands.</summary>
    public static ShellJobStatus Status(ShellJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var exit = job.Handle.Exit;
        if (!exit.IsCompleted)
            return new ShellJobStatus(true, 0);
        return new ShellJobStatus(false, exit.IsCompletedSuccessfully ? exit.Result : -1);
    }

    /// <summary>Starts a job with <paramref name="launch"/> and keeps it.</summary>
    /// <exception cref="InvalidOperationException">The registry is full or its run has ended.</exception>
    public async Task<ShellJob> StartAsync(Func<Task<ShellJobHandle>> launch, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(launch);
        lock (_sync)
        {
            if (_disposed)
                throw new InvalidOperationException("The agent run has ended.");
            if (LiveCount() >= MaxLiveJobs)
                throw new InvalidOperationException($"{MaxLiveJobs} jobs are already running: wait for one to exit.");
            _starting++;
        }

        ShellJobHandle handle;
        try
        {
            handle = await launch().ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
                _starting--;
            throw;
        }

        var job = new ShellJob(handle, outputPath);
        bool ended;
        lock (_sync)
        {
            _starting--;
            ended = _disposed;
            if (!ended)
                _jobs[handle.Id] = job;
        }

        if (ended)
        {
            handle.Kill();
            throw new InvalidOperationException("The agent run has ended.");
        }

        return job;
    }

    /// <summary>Ends the registry and kills the jobs still running.</summary>
    public ValueTask DisposeAsync()
    {
        ShellJob[] jobs;
        lock (_sync)
        {
            _disposed = true;
            jobs = [.. _jobs.Values];
        }

        foreach (var handle in jobs.Select(job => job.Handle).Where(handle => !handle.Exit.IsCompleted))
            handle.Kill();

        return ValueTask.CompletedTask;
    }

    private int LiveCount() => _starting + _jobs.Values.Count(job => !job.Handle.Exit.IsCompleted);

    /// <summary>The key of a run's job registry: private, so no other resource can hold it.</summary>
    private sealed record SlotKey;
}
