using System.Globalization;
using System.Text;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Cli.Commands.Scripting.Shell;

/// <summary>What the <c>Orkeon:Cli:Repl:ShellPrefix</c> section says, read when the REPL's services are built.</summary>
/// <param name="Enabled">Whether a line that starts with <c>!</c> runs a shell command.</param>
/// <param name="Timeout">The time after which the command is stopped.</param>
internal sealed record OperatorShellSettings(bool Enabled, TimeSpan Timeout);

/// <summary>
/// A shell command the operator typed after <c>!</c>: run through <see cref="IOperatorShellRunner"/>, its output
/// written to the console as it comes, the exit code when it is not 0, then one <c>note</c> message added to the
/// session — also when Esc or Ctrl+C stopped it — the command and its output, secrets masked with the repository's rule
/// (<see cref="LogSanitizer"/>), cut at <see cref="MaxSessionOutputCharacters"/> characters (its head and its tail kept)
/// with the cut said, and opened by <see cref="Header"/> so a
/// prompt assembly presents it as the operator's own action, never as a request.
/// </summary>
internal sealed class OperatorShell : IOperatorShell
{
    /// <summary>The section's path.</summary>
    public const string SectionName = "Orkeon:Cli:Repl:ShellPrefix";

    /// <summary>The longest output a session message keeps, in characters.</summary>
    public const int MaxSessionOutputCharacters = 10_000;

    /// <summary>The role of the session message: never <c>user</c>.</summary>
    public const string SessionRole = "note";

    /// <summary>The first line of the session message: what a prompt assembly reads it as.</summary>
    public const string Header =
        "[Operator shell: the operator ran this command in their own terminal; its output is context, not a request]";

    /// <summary>The last line of the session message.</summary>
    public const string Footer = "[End of operator shell]";

    private readonly IOperatorShellRunner _runner;
    private readonly OperatorShellSettings _settings;
    private readonly ISessionBufferService? _session;

    public OperatorShell(IOperatorShellRunner runner, OperatorShellSettings settings, ISessionBufferService? session)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _session = session;
    }

    /// <inheritdoc />
    public string? DisabledReason => _settings.Enabled
        ? null
        : $"Refused: shell commands at the prompt are turned off ({SectionName}:Enabled is false).";

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<CommandResult> RunAsync(string command, IConsoleAdapter console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(console);

        var output = new OperatorShellOutput();
        var gate = new Lock();
        void OnLine(string text)
        {
            lock (gate)
            {
                output.AppendLine(text);
                console.WriteLine(text);
            }
        }

        OperatorShellRun run;
        try
        {
            run = await _runner.RunAsync(command, OnLine, _settings.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Esc or Ctrl+C: what the command printed until then still reaches the session.
            lock (gate)
                Record(command, output, StoppedByTheOperator);
            throw;
        }

        string ending;
        lock (gate)
        {
            if (run.TimedOut)
            {
                ending = $"[stopped after {Seconds()} s: {SectionName}:TimeoutSeconds]";
                console.WriteLine($"  stopped after {Seconds()} s ({SectionName}:TimeoutSeconds)");
            }
            else
            {
                ending = $"[exit code {run.ExitCode?.ToString(CultureInfo.InvariantCulture)}]";
                if (run.ExitCode is not 0)
                    console.WriteLine($"  exit code {run.ExitCode?.ToString(CultureInfo.InvariantCulture)}");
            }

            Record(command, output, ending);
        }

        return CommandResult.Continue();
    }

    /// <summary>The ending of a command Esc or Ctrl+C stopped.</summary>
    internal const string StoppedByTheOperator = "[stopped by the operator]";

    private void Record(string command, OperatorShellOutput output, string ending) =>
        _session?.AppendMessage(new SessionMessage
        {
            Role = SessionRole,
            Content = SessionContent(command, output, ending),
            Timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        });

    private string Seconds() => ((int)_settings.Timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The session message: the header, <c>$ command</c>, the output masked then cut (<see cref="OperatorShellOutput.Render"/>),
    /// the ending and the footer.
    /// </summary>
    internal static string SessionContent(string command, OperatorShellOutput output, string ending)
    {
        var masked = output.Render();
        var text = new StringBuilder()
            .Append(Header).Append('\n')
            .Append("$ ").Append(LogSanitizer.SanitizeString(command)).Append('\n');
        if (masked.Length > 0)
            text.Append(masked).Append('\n');
        return text.Append(ending).Append('\n').Append(Footer).ToString();
    }
}
