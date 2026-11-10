using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Runs;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// <c>orkeon runs list [--crew &lt;key&gt;] [--limit &lt;n&gt;] | show &lt;id&gt; | tail &lt;id&gt; [--follow] [--from &lt;seq&gt;]</c>:
/// reads back the runs the history recorded (<c>Orkeon:RunHistory</c>, FWK-08). <c>show</c> and <c>tail</c> write the very
/// lines <c>orkeon run --events jsonl</c> wrote, one JSON document per line, so one reader serves both; <c>list</c> writes
/// one JSON summary per run, newest first.
/// </summary>
internal static class RunsCommand
{
    private const string Usage =
        """
        Usage:
          orkeon runs list [--crew <key>] [--limit <n>] [--settings <path>]
          orkeon runs show <run-id> [--settings <path>]
          orkeon runs tail <run-id> [--follow] [--from <seq>] [--settings <path>]

        Reads the history Orkeon:RunHistory records (set Orkeon:RunHistory:Enabled to true).
        show and tail write the lines of the --events stream; list writes one JSON summary per run.

        """;

    private static readonly JsonSerializerOptions SummaryOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>How long <c>tail --follow</c> waits between two reads of a run still in flight.</summary>
    internal static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Dispatches <c>orkeon runs …</c> (arguments already stripped of the verb).</summary>
    public static Task<int> DispatchAsync(IRunnerConsole console, string[] args) =>
        DispatchAsync(console, args, Directory.GetCurrentDirectory());

    /// <summary>Test seam: the same dispatch, anchored at <paramref name="workingDirectory"/> (where <c>.orkeon/runs</c> is).</summary>
    internal static async Task<int> DispatchAsync(IRunnerConsole console, string[] args, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            await console.Output.WriteAsync(Usage).ConfigureAwait(false);
            return args.Length == 0 ? Program.ExitScriptError : Program.ExitOk;
        }

        var parsed = Parse(args);
        if (parsed.Error is { } error)
            return await RefuseAsync(console, error).ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        using var interrupt = console.OnInterrupt(() =>
        {
            cts.Cancel();
            return true;
        });

        IRunHistoryReader reader;
        Microsoft.Extensions.Hosting.IHost host;
        try
        {
            host = RunnerHost.Build(
                parsed.SettingsPath,
                new RunnerMountPlan { WorkingDirectory = workingDirectory },
                configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.Error));
        }
        catch (RunnerSettingsException ex)
        {
            return await RefuseAsync(console, ex.Message).ConfigureAwait(false);
        }

        using (host)
        {
            if (host.Services.GetService<IRunHistoryReader>() is not { } found)
            {
                return await RefuseAsync(
                    console,
                    "the run history is not enabled: set Orkeon:RunHistory:Enabled to true (orkeon-host records it by default).").ConfigureAwait(false);
            }

            reader = found;
            try
            {
                return parsed.Action switch
                {
                    "list" => await ListAsync(console, reader, parsed, cts.Token).ConfigureAwait(false),
                    "show" => await TailAsync(console, reader, parsed with { From = 1, Follow = false }, cts.Token).ConfigureAwait(false),
                    _ => await TailAsync(console, reader, parsed, cts.Token).ConfigureAwait(false),
                };
            }
            catch (OperationCanceledException)
            {
                return Program.ExitCancelled;
            }
        }
    }

    private static async Task<int> ListAsync(IRunnerConsole console, IRunHistoryReader reader, RunsArguments parsed, CancellationToken ct)
    {
        var runs = await reader.ListAsync(new RunQuery { CrewKey = parsed.Crew, Limit = parsed.Limit }, ct).ConfigureAwait(false);
        foreach (var run in runs)
            await console.Output.WriteLineAsync(JsonSerializer.Serialize(run, SummaryOptions)).ConfigureAwait(false);
        return Program.ExitOk;
    }

    private static async Task<int> TailAsync(IRunnerConsole console, IRunHistoryReader reader, RunsArguments parsed, CancellationToken ct)
    {
        var runId = parsed.RunId!;
        if (await reader.GetAsync(runId, ct).ConfigureAwait(false) is null)
            return await RefuseAsync(console, $"no run '{runId}' in the history.").ConfigureAwait(false);

        var next = parsed.From;
        while (true)
        {
            var events = await reader.ReadEventsAsync(runId, next, ct).ConfigureAwait(false);
            foreach (var runEvent in events)
            {
                await console.Output.WriteLineAsync(RunEventWire.Serialize(runEvent)).ConfigureAwait(false);
                next = runEvent.Seq + 1;
            }

            // A run that ended has nothing more to say; --follow waits only for one still in flight.
            if (!parsed.Follow
                || (await reader.GetAsync(runId, ct).ConfigureAwait(false))?.Status != RunStatus.Running)
            {
                return Program.ExitOk;
            }

            await Task.Delay(FollowInterval, ct).ConfigureAwait(false);
        }
    }

    private static async Task<int> RefuseAsync(IRunnerConsole console, string message)
    {
        await console.ErrorOutput.WriteLineAsync("orkeon runs: " + message).ConfigureAwait(false);
        return Program.ExitScriptError;
    }

    private sealed record RunsArguments(
        string Action = "",
        string? RunId = null,
        string? Crew = null,
        int Limit = 50,
        int From = 1,
        bool Follow = false,
        string? SettingsPath = null,
        string? Error = null);

    /// <summary>Applies the argument at <paramref name="i"/> (and its value, advancing <paramref name="i"/>) to <paramref name="parsed"/>; sets <c>Error</c> when it is refused.</summary>
    private static RunsArguments ParseArgument(RunsArguments parsed, string[] args, ref int i)
    {
        var arg = args[i];
        var next = i + 1 < args.Length ? args[i + 1] : null;
        if (TakesValue(arg, parsed.Action) && next is not null)
            i++;

        return ApplyOption(parsed, arg, next);
    }

    private static bool TakesValue(string arg, string action) => arg switch
    {
        "--crew" or "--limit" => action == "list",
        "--from" => action == "tail",
        "--settings" or "-s" => true,
        _ => false,
    };

    private static RunsArguments ApplyLimit(RunsArguments parsed, string? next) => ParsePositive(next) switch
    {
        { } limit => parsed with { Limit = limit },
        null => parsed with { Error = "--limit needs a positive number." },
    };

    private static RunsArguments ApplyFrom(RunsArguments parsed, string? next) => ParsePositive(next) switch
    {
        { } from => parsed with { From = from },
        null => parsed with { Error = "--from needs a sequence number (1 or more)." },
    };

    private static RunsArguments ApplyOption(RunsArguments parsed, string arg, string? next) => arg switch
    {
        "--crew" when parsed.Action == "list" =>
            next is null ? parsed with { Error = "--crew needs a crew key." } : parsed with { Crew = next },
        "--limit" when parsed.Action == "list" => ApplyLimit(parsed, next),
        "--from" when parsed.Action == "tail" => ApplyFrom(parsed, next),
        "--follow" when parsed.Action == "tail" => parsed with { Follow = true },
        "--settings" or "-s" =>
            next is null ? parsed with { Error = "--settings needs a path." } : parsed with { SettingsPath = next },
        _ when arg.StartsWith('-') || parsed.Action == "list" || parsed.RunId is not null =>
            parsed with { Error = $"unexpected argument '{arg}'." },
        _ => parsed with { RunId = arg },
    };

    private static int? ParsePositive(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 1 ? value : null;

    private static RunsArguments Parse(string[] args)
    {
        var parsed = new RunsArguments { Action = args[0] };
        if (parsed.Action is not ("list" or "show" or "tail"))
            return parsed with { Error = $"unknown subcommand '{parsed.Action}' (list, show, tail)." };

        for (var i = 1; i < args.Length; i++)
        {
            parsed = ParseArgument(parsed, args, ref i);
            if (parsed.Error is not null)
                return parsed;
        }

        return parsed.Action != "list" && parsed.RunId is null
            ? parsed with { Error = $"'{parsed.Action}' needs a run id." }
            : parsed;
    }
}
