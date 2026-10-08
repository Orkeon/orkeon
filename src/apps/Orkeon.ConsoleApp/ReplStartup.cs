using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Cli.Commands.Scripting.Configuration;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Hosting;

namespace Orkeon.ConsoleApp;

/// <summary>
/// What the REPL's start produced: the host to run and the options it was built with, or — the
/// refusal already reported — the exit code to leave with.
/// </summary>
/// <param name="Host">The host, built and not started; null when a setting was refused.</param>
/// <param name="ExitCode">0 with a host; 1 after a refusal.</param>
/// <param name="ScriptedOptions">The <c>--commands-dir</c>, <c>--crews-dir</c>, <c>--mount</c>, <c>--settings</c> … options.</param>
internal sealed record ReplLaunch(IHost? Host, int ExitCode, ScriptedCommandsCliOptions ScriptedOptions);

/// <summary>
/// Everything the REPL does before its console opens, under one barrier (GAP-40, decision 7), as
/// <c>orkeon run</c> and <c>orkeon-host</c> do: its registrations, a setting they refuse becoming the
/// one type every entry point translates; <c>Logging</c>, judged before the logger is built; then the
/// start validation of the runner host — values, keys, names, <c>Orkeon:Rag:LlmProfile</c>. A
/// <c>--settings</c> file it cannot read is named. Each refusal is one line, <c>orkeon-repl: …</c>,
/// and the exit code 1, before the console opens: the REPL used to open on a file <c>orkeon run</c>
/// refuses, and its first command failed on it. A section the settings write that no shipped binary
/// reads is one line too, <c>orkeon-repl: warning: …</c>, and the console opens.
/// </summary>
internal static class ReplStartup
{
    /// <summary>The exit code of a refused setting.</summary>
    internal const int RefusedExitCode = 1;

    /// <summary>The REPL's start sequence, testable without a process.</summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="effectiveUi">The UI mode the flags resolved.</param>
    /// <param name="replWordWrap">The <c>--repl-wrap</c> choice.</param>
    /// <param name="report">Where a refusal or a notice is written: stderr, or a test's sink.</param>
    public static ReplLaunch Prepare(string[] args, UiMode effectiveUi, bool replWordWrap, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(report);

        var scriptedOpts = ScriptedCommandsCliOptions.Parse(args);
        var globalSettingsPath = Program.GlobalSettingsPathOrNull();

        IHost host;
        try
        {
            host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, builder) => Program.ConfigureAppConfiguration(builder, scriptedOpts, globalSettingsPath))
                .ConfigureServices((context, services) =>
                    ConfigureServicesUnderBarrier(context, services, effectiveUi, scriptedOpts, replWordWrap))
                .Build();
        }
        catch (RunnerSettingsException ex)
        {
            return Refused(report, ex.Message, scriptedOpts);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
        {
            // A --settings file that is not JSON, or that is not there: named, with the line and the
            // position of what its JSON gets wrong — as the runners name it (GAP-35).
            return Refused(report, RunnerSettings.DescribeUnreadableSettings(ex), scriptedOpts);
        }

        if (RunnerHost.ValidateSettings(host.Services) is [var refusal, ..])
        {
            host.Dispose();
            return Refused(report, refusal, scriptedOpts);
        }

        // What the settings write and no component of the REPL reads: said before the console
        // opens, as a runner says it, and the REPL starts.
        RunnerHost.AnnounceSettingsNotices(host.Services, notice => report($"orkeon-repl: warning: {notice}"));

        return new ReplLaunch(host, 0, scriptedOpts);
    }

    /// <summary>
    /// The REPL's registrations, under the barrier of the runner host (GAP-35): a setting they refuse
    /// — <c>Logging</c> judged first, before the host builds its logger from it — is a
    /// <see cref="RunnerSettingsException"/>; anything else keeps its type and its stack.
    /// </summary>
    private static void ConfigureServicesUnderBarrier(
        HostBuilderContext context,
        IServiceCollection services,
        UiMode effectiveUi,
        ScriptedCommandsCliOptions scriptedOpts,
        bool replWordWrap)
    {
        try
        {
            SettingsValidation.CheckLogging(context.Configuration);
            Program.ConfigureServices(context, services, effectiveUi, scriptedOpts, replWordWrap);
        }
        catch (InvalidOperationException ex) when (ex is not RunnerSettingsException)
        {
            throw new RunnerSettingsException(ex.Message, ex);
        }
    }

    private static ReplLaunch Refused(Action<string> report, string refusal, ScriptedCommandsCliOptions scriptedOpts)
    {
        report($"orkeon-repl: {refusal}");
        return new ReplLaunch(null, RefusedExitCode, scriptedOpts);
    }
}
