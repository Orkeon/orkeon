using Orkeon.Cli.Commands.Scripting.Shell;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>An <see cref="IOperatorShellRunner"/> that prints configured lines and ends as told, keeping what it was asked.</summary>
internal sealed class FakeOperatorShellRunner : IOperatorShellRunner
{
    /// <summary>The lines handed out, in order.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>How the run ends.</summary>
    public OperatorShellRun Result { get; set; } = new(0, TimedOut: false);

    /// <summary>The commands run, in order.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>The timeout of the last run.</summary>
    public TimeSpan? Timeout { get; private set; }

    public Task<OperatorShellRun> RunAsync(string command, Action<string> onLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Commands.Add(command);
        Timeout = timeout;
        foreach (var line in Lines)
            onLine(line);
        return Task.FromResult(Result);
    }
}
