using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>Parsed surface of <c>orkeon forge</c> (SPEC-ORKEON-FORGE §5.1).</summary>
internal sealed record ForgeCommandOptions
{
    /// <summary>The problem, typed on the command line; null opens with the interview.</summary>
    public string? Need { get; init; }

    /// <summary><c>forge list</c>.</summary>
    public bool List { get; init; }

    /// <summary><c>forge resume &lt;slug&gt;</c>.</summary>
    public string? ResumeSlug { get; init; }

    /// <summary><c>forge promote &lt;slug&gt;</c>.</summary>
    public string? PromoteSlug { get; init; }

    /// <summary>
    /// <c>forge reopen &lt;team-folder&gt;</c>: the promoted folder to reopen the atelier on
    /// (FORGE-09). Offline: finds the session rule R links it to (STUDIO-25), or rebuilds one
    /// from its <c>crew/</c> when none is — the session gone, never here, or the folder a copy.
    /// </summary>
    public string? ReopenDirectory { get; init; }

    /// <summary><c>--to</c>: destination directory of a promotion.</summary>
    public string? Destination { get; init; }

    /// <summary>
    /// <c>--name</c>: the team's name (STUDIO-26, D-01) — the title the promotion gives
    /// <c>FORGE.md</c>, <c>forge.json</c> and the session. Taken as written, a leading dash
    /// included: it is the user's words, not the next option. On one line: a line break in it
    /// would split the card's title in two.
    /// </summary>
    public string? TeamName { get; init; }

    /// <summary><c>--schedule daily@HH:mm|hourly</c>: generate the schedule artifacts.</summary>
    public ForgeSchedule? Schedule { get; init; }

    /// <summary>
    /// <c>--with-settings</c>: copy the resolved settings file into the promoted folder.
    /// Off by default — a settings file usually carries API keys, and the folder is made
    /// to be shared; without the copy the launch scripts reference the file in place.
    /// </summary>
    public bool WithSettings { get; init; }

    /// <summary><c>--format yaml|script</c>; null when not passed (yaml for a new session).</summary>
    public string? Format { get; init; }

    /// <summary><c>--events jsonl</c>: protocol on stdout, answers on stdin.</summary>
    public bool Events { get; init; }

    /// <summary><c>--auto</c>: arbitrate without a human (F4 wires the verdict side).</summary>
    public bool Auto { get; init; }

    /// <summary><c>--dry</c>: stop after Validate.</summary>
    public bool Dry { get; init; }

    /// <summary>
    /// <c>--edit</c> (resume only): amend the blueprint of a session paused before its
    /// trial. The client sends the amended blueprint over the channel; the engine
    /// validates it in full, re-renders deterministically, and the cycle continues —
    /// with <c>--dry</c>, it pauses again at the same boundary. Zero LLM tokens, same
    /// iteration: the current one's trial has not run yet.
    /// </summary>
    public bool Edit { get; init; }

    /// <summary>
    /// <c>--adopt</c> (resume only): take the team as generated, without running a trial.
    /// Offline and instantaneous — the crew is already rendered and validated at the dry
    /// pause, and promotion needs no trial artefact. It skips evidence, never checks.
    /// </summary>
    public bool Adopt { get; init; }

    /// <summary><c>--max-iterations</c>.</summary>
    public int? MaxIterations { get; init; }

    /// <summary><c>--max-tokens</c>.</summary>
    public long? MaxTokens { get; init; }

    /// <summary><c>--max-seconds</c>.</summary>
    public long? MaxSeconds { get; init; }

    /// <summary><c>--settings</c>, same semantics as <c>orkeon run</c>.</summary>
    public string? SettingsPath { get; init; }

    /// <summary><c>--pack</c>: directory overriding the embedded pack files.</summary>
    public string? PackDirectory { get; init; }

    /// <summary>
    /// <c>--read &lt;dir&gt;</c>: the folder the trial reads as <c>/workspace</c>, in place of
    /// the workspace itself. The workspace keeps every other role — the sessions live under
    /// it, the settings resolve next to it — so a client can point a trial at the documents
    /// the team is meant to read without moving its sessions there. Null: the workspace, as
    /// before.
    /// </summary>
    public string? ReadDirectory { get; init; }

    /// <summary>
    /// <c>--reference &lt;id&gt;</c> (STUDIO-40): the use case of the catalogue a new session is
    /// composed from — its crew's structure is the composer's model, and the session records it.
    /// </summary>
    public string? Reference { get; init; }

    /// <summary>What made the parse fail, when it did.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// The options that take a value, each mapped to what the parse says when the value is
    /// missing — or, for the ones it also has to interpret, when it is not usable.
    /// </summary>
    private static readonly Dictionary<string, string> ValueOptionErrors = new(StringComparer.Ordinal)
    {
        ["--to"] = "--to needs a destination directory.",
        ["--name"] = "--name needs the team's name.",
        ["--schedule"] = "--schedule needs a value: daily@HH:mm or hourly.",
        ["--format"] = "--format needs a value: yaml or script.",
        ["--max-iterations"] = "--max-iterations needs a positive integer.",
        ["--max-tokens"] = "--max-tokens needs a non-negative integer (0 = unlimited).",
        ["--max-seconds"] = "--max-seconds needs a non-negative integer (0 = unlimited).",
        ["--settings"] = "--settings needs a path.",
        ["--pack"] = "--pack needs a directory.",
        ["--read"] = "--read needs a directory.",
        ["--reference"] = "--reference needs a use case id (see `orkeon usecases list`).",
    };

    /// <summary>
    /// The options whose value is taken as written, a leading dash included: a team's name is the
    /// user's words, and « -Veille- » is one (STUDIO-26, D-01). Every other value starting with a
    /// dash is read as the next option, so a missing value is said rather than swallowed.
    /// </summary>
    private static readonly HashSet<string> VerbatimValueOptions = new(StringComparer.Ordinal) { "--name" };

    /// <summary>Parses the <c>forge</c> arguments; unknown options fail loudly, never silently.</summary>
    public static ForgeCommandOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var needWords = new List<string>();
        var options = new ForgeCommandOptions();

        for (var i = 0; i < args.Length; i++)
        {
            // A verb is positional: only the first argument can be one, anything later with
            // the same spelling is part of the need.
            options = i == 0 && IsVerb(args[0])
                ? ParseVerb(args, ref i, options)
                : ParseOption(args, ref i, options, needWords);

            // `list` takes no argument and an error stops the parse where it happened.
            if (options.Error is not null || options.List)
                return options;
        }

        return Reconcile(options, needWords);
    }

    private static bool IsVerb(string arg) => arg is "list" or "resume" or "promote" or "reopen";

    /// <summary>
    /// The four subcommands; <c>resume</c> and <c>promote</c> take the session slug,
    /// <c>reopen</c> the promoted team folder.
    /// </summary>
    private static ForgeCommandOptions ParseVerb(string[] args, ref int i, ForgeCommandOptions options)
    {
        var verb = args[i];
        if (verb == "list")
            return options with { List = true };

        if (verb == "reopen")
        {
            return i + 1 < args.Length
                ? options with { ReopenDirectory = args[++i] }
                : options with { Error = "reopen needs the team folder (the one `forge promote --to` wrote)." };
        }

        // The slug is taken as written: it is positional, so it is whatever follows the verb.
        if (i + 1 >= args.Length)
            return options with { Error = $"{verb} needs a session slug (see `orkeon forge list`)." };

        var slug = args[++i];
        return verb == "resume"
            ? options with { ResumeSlug = slug }
            : options with { PromoteSlug = slug };
    }

    /// <summary>One option, one flag — or one word of the need typed on the command line.</summary>
    private static ForgeCommandOptions ParseOption(
        string[] args, ref int i, ForgeCommandOptions options, List<string> needWords)
    {
        var arg = args[i];

        if (arg == "--events")
        {
            // The only stream format is jsonl; the value is accepted for the spec's
            // spelling (`--events jsonl`) and for forward compatibility.
            TryTakeValue(args, ref i, verbatim: false, out _);
            return options with { Events = true };
        }

        if (ApplyFlag(arg, options) is { } flagged)
            return flagged;

        if (ValueOptionErrors.TryGetValue(arg, out var missing))
        {
            return TryTakeValue(args, ref i, VerbatimValueOptions.Contains(arg), out var value)
                ? ApplyValue(options, arg, value, missing)
                : options with { Error = missing };
        }

        if (arg.StartsWith('-'))
            return options with { Error = $"Unknown option '{arg}'." };

        needWords.Add(arg);
        return options;
    }

    /// <summary>The valueless switches; <see langword="null"/> when the argument is not one.</summary>
    private static ForgeCommandOptions? ApplyFlag(string arg, ForgeCommandOptions options) => arg switch
    {
        "--with-settings" => options with { WithSettings = true },
        "--auto" => options with { Auto = true },
        "--dry" => options with { Dry = true },
        "--edit" => options with { Edit = true },
        "--adopt" => options with { Adopt = true },
        _ => null,
    };

    /// <summary>
    /// Records the value of an option that takes one; <paramref name="invalid"/> is the
    /// message of the options this parse also has to interpret.
    /// </summary>
    private static ForgeCommandOptions ApplyValue(
        ForgeCommandOptions options, string arg, string value, string invalid) => arg switch
    {
        "--to" => options with { Destination = value },
        "--name" => WithTeamName(options, value, invalid),
        "--schedule" => WithSchedule(options, value),
        "--format" => options with { Format = value },
        "--max-iterations" => WithMaxIterations(options, value, invalid),
        "--max-tokens" => WithMaxTokens(options, value, invalid),
        "--max-seconds" => WithMaxSeconds(options, value, invalid),
        "--settings" => options with { SettingsPath = value },
        "--read" => options with { ReadDirectory = value },
        "--reference" => options with { Reference = value },
        _ => options with { PackDirectory = value },
    };

    /// <summary>The team's name on one line; a blank one says nothing and is refused like a missing one.</summary>
    private static ForgeCommandOptions WithTeamName(ForgeCommandOptions options, string value, string invalid)
    {
        var name = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length > 0 ? options with { TeamName = name } : options with { Error = invalid };
    }

    private static ForgeCommandOptions WithSchedule(ForgeCommandOptions options, string value) =>
        ForgeSchedule.TryParse(value, out var schedule, out var scheduleError)
            ? options with { Schedule = schedule }
            : options with { Error = scheduleError };

    private static ForgeCommandOptions WithMaxIterations(ForgeCommandOptions options, string value, string invalid) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var maxIterations)
        && maxIterations >= 1
            ? options with { MaxIterations = maxIterations }
            : options with { Error = invalid };

    private static ForgeCommandOptions WithMaxTokens(ForgeCommandOptions options, string value, string invalid) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var maxTokens)
            ? options with { MaxTokens = maxTokens }
            : options with { Error = invalid };

    private static ForgeCommandOptions WithMaxSeconds(ForgeCommandOptions options, string value, string invalid) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var maxSeconds)
            ? options with { MaxSeconds = maxSeconds }
            : options with { Error = invalid };

    /// <summary>
    /// The rules that only make sense once every argument has been read, in the order they
    /// are checked: the first one broken is the error. <c>promote</c> and <c>list</c> mount
    /// nothing, so a read folder there would be silently ignored — and this parser never
    /// ignores an option silently; <c>reopen</c> starts no cycle, so every option that shapes
    /// one is refused there.
    /// </summary>
    private static readonly (Func<ForgeCommandOptions, int, bool> Broken, string Error)[] ReconcileRules =
    [
        ((o, _) => o.PromoteSlug is not null && o.Destination is null,
            "promote needs --to <directory>."),
        ((o, _) => o.PromoteSlug is null
                   && (o.Destination is not null || o.TeamName is not null || o.Schedule is not null || o.WithSettings),
            "--to, --name, --schedule and --with-settings only apply to `forge promote`."),
        ((o, _) => o.Edit && o.ResumeSlug is null,
            "--edit only applies to `forge resume`."),
        ((o, _) => o.Adopt && o.ResumeSlug is null,
            "--adopt only applies to `forge resume`."),
        ((o, _) => o.Adopt && o.Edit,
            "--adopt and --edit are two different answers to the same pause."),
        ((o, _) => o.ReadDirectory is not null && (o.PromoteSlug is not null || o.List || o.ReopenDirectory is not null),
            "--read only applies to a new session or to `forge resume`."),
        ((o, _) => o.Reference is not null
                   && (o.ResumeSlug is not null || o.PromoteSlug is not null || o.List || o.ReopenDirectory is not null),
            "--reference only applies to a new session: a session keeps the reference it was created with."),
        ((o, needWords) => o.ReopenDirectory is not null && (ShapesACycle(o) || needWords > 0),
            "reopen takes no option but --events: it finds or rebuilds the team's session and starts nothing."),
    ];

    /// <summary>Whether any option that shapes a cycle was passed.</summary>
    private static bool ShapesACycle(ForgeCommandOptions o) =>
        o.Format is not null || o.Auto || o.Dry || o.PackDirectory is not null
        || o.SettingsPath is not null || o.MaxIterations is not null
        || o.MaxTokens is not null || o.MaxSeconds is not null;

    /// <summary>Applies <see cref="ReconcileRules"/>, then folds the need words into the need.</summary>
    private static ForgeCommandOptions Reconcile(ForgeCommandOptions options, List<string> needWords)
    {
        foreach (var (broken, error) in ReconcileRules)
        {
            if (broken(options, needWords.Count))
                return options with { Error = error };
        }

        return options with { Need = needWords.Count > 0 ? string.Join(' ', needWords) : null };
    }

    private static bool TryTakeValue(string[] args, ref int i, bool verbatim, out string value)
    {
        if (i + 1 < args.Length && (verbatim || !args[i + 1].StartsWith('-')))
        {
            value = args[++i];
            return true;
        }

        value = "";
        return false;
    }
}

/// <summary>
/// <c>orkeon forge</c> — the Atelier (SPEC-ORKEON-FORGE): need → generated crew →
/// sandboxed test → diagnosis → verdict → promotion, the interview carried by the pack
/// crew and the cycle by the engine. <c>--dry</c> stops after Validate;
/// <c>--read &lt;dir&gt;</c> points the trial's <c>/workspace</c> at a folder of documents;
/// <c>promote &lt;slug&gt; --to &lt;dir&gt;</c> ships a Ready session as an ordinary folder;
/// <c>reopen &lt;dir&gt;</c> finds or rebuilds the session of a promoted folder (FORGE-09).
/// </summary>
internal static class ForgeCommand
{
    /// <summary>Exit code for a session/usage error, aligned on the CLI's contract.</summary>
    private const int ExitError = 1;

    /// <summary>Exit code for an unexpected runtime failure, aligned on the CLI's contract.</summary>
    private const int ExitRuntimeError = 2;

    /// <summary>Exit code of an interrupted run, aligned on the CLI's contract.</summary>
    private const int ExitCancelled = 130;

    /// <summary>Dispatches <c>forge</c> subcommands.</summary>
    public static async Task<int> DispatchAsync(string[] args, string? workingDirectoryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var workspace = workingDirectoryOverride ?? Directory.GetCurrentDirectory();
        var options = ForgeCommandOptions.Parse(args);

        if (options.Error is { } parseError)
        {
            await Console.Error.WriteLineAsync($"orkeon forge: {parseError}").ConfigureAwait(false);
            return ExitError;
        }

        if (options.List)
            return await ListAsync(workspace).ConfigureAwait(false);

        if (options.PromoteSlug is not null)
            return await PromoteAsync(workspace, options).ConfigureAwait(false);

        if (options.ReopenDirectory is not null)
        {
            // Under the same guard as adoption, for the same reason: the rebuild writes a
            // session, and a disk that refuses must come back as an exit code with a
            // closed event stream.
            try
            {
                return await ReopenAsync(workspace, options).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // the CLI boundary: anything unexpected becomes exit 2, like `orkeon run`
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"orkeon forge: {Explain(ex)}").ConfigureAwait(false);
                return ExitRuntimeError;
            }
#pragma warning restore CA1031
        }

        if (options.Format is { } requestedFormat
            && !string.Equals(requestedFormat, ForgeSession.FormatYaml, StringComparison.OrdinalIgnoreCase)
            && !ForgeSession.IsScriptFormat(requestedFormat))
        {
            await Console.Error.WriteLineAsync(
                $"orkeon forge: unknown format '{requestedFormat}' — use yaml or script.")
                .ConfigureAwait(false);
            return ExitError;
        }

        try
        {
            // Inside the guard, like the cycle: adoption writes the history and the session
            // file, and a disk that refuses either must come back as the CLI's own exit
            // code with a closed event stream — not as an unhandled exception that leaves a
            // client waiting forever for a session.finished that will never come.
            return options.Adopt
                ? await AdoptAsync(workspace, options).ConfigureAwait(false)
                : await RunCycleAsync(workspace, options).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("orkeon forge: interrupted — the session is saved and resumable.")
                .ConfigureAwait(false);
            return ExitCancelled;
        }
#pragma warning disable CA1031 // the CLI boundary: anything unexpected becomes exit 2, like `orkeon run`
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"orkeon forge: {Explain(ex)}").ConfigureAwait(false);
            return ExitRuntimeError;
        }
#pragma warning restore CA1031
    }

    private static async Task<int> RunCycleAsync(string workspace, ForgeCommandOptions options)
    {
        // A read folder that does not exist is a typo, and the cheapest refusal there is:
        // before the session is even opened, so a mistyped path leaves no stray session
        // behind and no host boots to discover an absent mount base the hard way.
        var readRoot = options.ReadDirectory is { } readDirectory ? Path.GetFullPath(readDirectory) : null;
        if (readRoot is not null && !Directory.Exists(readRoot))
        {
            await Console.Error.WriteLineAsync($"orkeon forge: --read names no directory: '{readRoot}'.")
                .ConfigureAwait(false);
            return ExitError;
        }

        // A reference that names no use case is refused the same way, and just as early
        // (STUDIO-40, D-05): before the session is opened and before any host boots, so a
        // mistyped id leaves no stray session behind and no model is ever asked.
        ForgeReference? reference = null;
        if (options.Reference is { } referenceId)
        {
            try
            {
                reference = ForgeReference.Load(referenceId);
            }
            catch (UseCases.UnknownUseCaseException ex)
            {
                return await RefuseReferenceAsync(options, ex).ConfigureAwait(false);
            }
        }

        // Open or create the session first: it is cheap, offline, and `resume` must be
        // able to say "no such session" before any host boots.
        var resumed = options.ResumeSlug is not null;
        var (session, openExitCode) = await OpenSessionAsync(workspace, options, reference).ConfigureAwait(false);
        if (session is null)
            return openExitCode;

        // A resume composes with the reference the session recorded (D-01): the parser refuses
        // another one there.
        if (resumed)
            reference = await RecordedReferenceAsync(session).ConfigureAwait(false);

        // The engine host: same settings chain as `orkeon run` (explicit --settings →
        // next to the workspace → global), the read folder readable, the session writable,
        // and the forge's own services on top.
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, workspace);
        var box = new ForgeSubmissionBox();
        var tally = new ForgeUsageTally();
        var observer = new ForgeRunObserver();

        // The sandbox's write surface: /output lands inside the session, snapshotted per
        // run by the test stage. Everything else the crew sees is read-only.
        Directory.CreateDirectory(OutputDirectoryOf(session));

        // Forge accepts no --mount: its three roots are its own CliMounts, and the host places
        // a CliMount by virtual root against the settings (STUDIO-15 D-01) — a settings entry
        // on /workspace, /forge or /output is replaced for the trial, and logged as such. So
        // they are NOT reserved here, and must not be: /output is an ordinary mount for a run,
        // and the name Studio gives a team's write folder, so a settings file naming it is the
        // normal case on every Studio machine, not a mistake. Refusing it — as the guard that
        // predated D-01 did — stopped the wizard's first trial on the very folder the user had
        // just associated. The one root the forge does not mount itself is /sandbox: the file
        // system registers it internally in every host, a settings entry there still reaches
        // the registry's duplicate check, and only this guard turns that crash into one line.
        if (!RunnerExecution.EnsureReservedRootsAreFree([], settingsPath, RunnerVirtualRoots.Sandbox))
        {
            return 1;
        }

        // The forge's three CliMounts replace every settings entry of /workspace, /forge and
        // /output — a machine with two /output entries (VFS-90) forges unchanged. Any other
        // root declared twice with nothing to pick one is refused here, in one line.
        var mountPlan = BuildMountPlan(workspace, readRoot, session);
        if (!RunnerExecution.EnsureMountSelectionIsResolvable(mountPlan.CliMounts, [], CrewMountDeclarations.None, settingsPath, out _))
        {
            return 1;
        }

        using var host = RunnerHost.Build(
            settingsPath,
            mountPlan,
            // stdout carries the --events jsonl protocol. The default preset writes
            // warnings there, so one line like «Access denied by registry for virtual path
            // '.'» lands in the middle of the event stream and every consumer has to guess
            // which lines are events. Warnings still matter — they go to stderr.
            configureLogging: (_, logging) =>
            {
                logging.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss.fff ";
                });
                logging.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(
                    o => o.LogToStandardErrorThreshold = LogLevel.Trace);
                logging.SetMinimumLevel(LogLevel.Warning);
                // The host's own decisions stay visible above that floor: the LLM it resolved,
                // and a settings entry one of the forge's mounts replaced (D-01) — the trial
                // writes into the session's bench, not into the folder the settings name as
                // /output, and the log is where that is said.
                logging.AddFilter("Orkeon.Hosting.RunnerHost", LogLevel.Information);
            },
            configureServices: (_, services) =>
            {
                services.AddSingleton(box);
                services.AddSingleton(tally);
                services.AddSingleton<ILlmUsageSink>(tally);
                // Registering a delta sink is what puts the assistant on the provider's
                // streaming path, and that is the point: the meter then moves with each
                // chunk instead of once a whole response has been written. The
                // OpenAI-compatible base reassembles tool_calls from stream fragments for
                // exactly this loop, so brief_submit / blueprint_submit are unaffected.
                services.AddSingleton<ILlmDeltaSink>(tally);
                services.AddSingleton<Orkeon.Application.Crew.ICrewExecutionHook>(observer);
                services.AddSingleton<IBaseTool>(new BriefSubmitTool(box));
                services.AddSingleton<IBaseTool>(new BlueprintSubmitTool(box));
            });

        // No Llm section = no interview. Refuse with the remedy, before spending a turn —
        // the silent echo degrade that is acceptable for `orkeon run` would make the
        // assistant babble its own prompts back at the user.
        if (!host.Services.GetRequiredService<IConfiguration>().GetSection(ConfigurationKeys.LlmSection).Exists())
        {
            await Console.Error.WriteLineAsync(
                "orkeon forge: no LLM is configured (FORGE-LLM-UNAVAILABLE) — run `orkeon init`, or pass --settings.")
                .ConfigureAwait(false);
            return ExitError;
        }

        var packPath = ForgePack.Ensure(session.Directory, options.PackDirectory);
        var assistant = new ForgeCrewAssistant(host.Services, session, packPath, reference: reference);

        // The sandbox catalogue: what the blueprint prompt shows is exactly what the
        // validation enforces (SPEC §9.1) — one list, two consumers.
        var knownTools = ForgeSandbox.SelectCrewTools(host.Services.GetServices<IBaseTool>())
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Disposing the renderer never touches Console.Out — it only wraps it.
        using var renderer = options.Events ? null : new ForgeTerminalRenderer(Console.Out);
        var events = new ForgeEventWriter(renderer ?? Console.Out);
        observer.Attach(events);
        IForgeUserChannel channel = options.Events
            ? new JsonLinesUserChannel(Console.In)
            : new TerminalUserChannel(Console.In, Console.Out);

        // The dry-pause edit (v3 W-10): the wizard's Composer step shows the proposed
        // agents while the engine is off — amending one is a resume that carries the
        // blueprint over the channel, validated in full, then a deterministic re-render.
        // The arbitration keeps its own edit decision; this path only exists BEFORE the
        // first trial of the current iteration, so it charges none.
        var announce = true;
        if (options.Edit)
        {
            if (await ApplyEditedBlueprintAsync(session, channel, events, knownTools).ConfigureAwait(false)
                is { } editExitCode)
            {
                return editExitCode;
            }

            // The amendment announced the session itself, before the exchange.
            announce = false;
        }

        var engine = new ForgeEngine(
            session,
            events,
            [
                new BriefStage(assistant, channel, resumed ? null : options.Need),
                new BlueprintStage(assistant),
                new RenderStage(),
                new ValidateStage(knownTools),
                new TestStage(new ForgeCrewTestBench(host.Services)),
                new DiagnoseStage(new LlmForgeJudge(host.Services.GetService<Orkeon.Domain.SharedKernel.ILlmProvider>())),
                // A resume that lands AT the arbitration (a reopen, or an interruption
                // there) re-announces the stored verdict before asking again.
                new VerdictStage(options.Auto, channel, knownTools,
                    recallVerdict: resumed && session.State == ForgeState.Verdict),
            ]);

        var result = await engine
            .RunAsync(resumed, stopBefore: options.Dry ? ForgeState.Test : null, announce: announce)
            .ConfigureAwait(false);

        await AnnounceDryPauseAsync(session, options, result).ConfigureAwait(false);
        return result.ExitCode;
    }

    /// <summary>
    /// The VFS surface of a cycle: the read folder as <c>/workspace</c> (read-only), the
    /// session as <c>/forge</c> and the session's output folder as <c>/output</c> (both
    /// writable). <paramref name="readRoot"/> is <c>--read</c>, resolved; null reads the
    /// workspace itself, the default. The workspace keeps every other role whatever is read:
    /// the session stays under <see cref="ForgeSession.RootFor"/> of the workspace and the
    /// settings still resolve next to it — <c>--read</c> moves the documents, not the atelier.
    /// <para>
    /// A read folder outside the process working directory is whitelisted the way
    /// <c>orkeon run</c> whitelists its script directory and <c>orkeon rag</c> its corpus:
    /// the mounts register either way, but the path validator behind every file tool refuses
    /// a physical path outside the cwd unless the plan allows it — and the plan is the forge's
    /// own three roots, nothing a user typed, so allowing it grants exactly those.
    /// </para>
    /// <para>
    /// Built here, apart from the host, so it can be asserted without an LLM.
    /// </para>
    /// </summary>
    internal static RunnerMountPlan BuildMountPlan(string workspace, string? readRoot, ForgeSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(session);

        // Containment, not spelling, like the run and rag verbs: ~/proj-old is not inside ~/proj.
        var readsOutsideCwd = readRoot is not null
            && !PhysicalPathContainment.IsUnder(readRoot, Directory.GetCurrentDirectory());

        return new RunnerMountPlan
        {
            CliMounts =
            [
                // Quoted, like every other spec the framework builds: a session, workspace or
                // read-folder path carrying a ':' or ';' would otherwise split into the wrong
                // segments and the forge would die at host build with a grammar error about
                // a path the user never typed.
                $"{FileSystemMount.Quote(readRoot ?? workspace)}:{RunnerVirtualRoots.Workspace}:ro",
                $"{FileSystemMount.Quote(session.Directory)}:{RunnerVirtualRoots.Forge}:rw",
                $"{FileSystemMount.Quote(OutputDirectoryOf(session))}:{RunnerVirtualRoots.Output}:rw",
            ],
            AllowExternalMounts = readsOutsideCwd,
        };
    }

    /// <summary>Where the trial bench writes <c>/output</c>: inside the session, snapshotted per run.</summary>
    private static string OutputDirectoryOf(ForgeSession session) =>
        Path.Combine(session.Directory, TestStage.OutputDirectoryName);

    /// <summary>
    /// An unknown <c>--reference</c> (STUDIO-40, D-05): the catalogue's own typed error,
    /// <c>USECASES-UNKNOWN-ID</c>. No session exists, so the stream carries the error and closes on
    /// «failed», like a reopen that could produce none; the terminal gets the one line.
    /// </summary>
    private static async Task<int> RefuseReferenceAsync(ForgeCommandOptions options, UseCases.UnknownUseCaseException error)
    {
        if (!options.Events)
        {
            await Console.Error.WriteLineAsync($"orkeon forge: {error.Message}").ConfigureAwait(false);
            return ExitError;
        }

        var events = new ForgeEventWriter(Console.Out);
        events.Error(UseCases.UnknownUseCaseException.Code, error.Message, recoverable: false);
        events.SessionFinished("failed", ExitError);
        return ExitError;
    }

    /// <summary>
    /// The reference a resumed session composes with: the one it recorded (D-01). A use case this
    /// build no longer carries is said on stderr and composed without — the session still resumes,
    /// and keeps its provenance as recorded.
    /// </summary>
    private static async Task<ForgeReference?> RecordedReferenceAsync(ForgeSession session)
    {
        if (session.Document.Reference is not { } recorded)
            return null;

        var reference = ForgeReference.Recorded(recorded);
        if (reference is null)
        {
            await Console.Error.WriteLineAsync(
                $"orkeon forge: the use case '{recorded.Id}' this session was composed from is no longer in the"
                + " catalogue — the team is composed without it.")
                .ConfigureAwait(false);
        }

        return reference;
    }

    /// <summary>
    /// Opens the session <c>resume</c> names, or creates a new one — composed from
    /// <paramref name="reference"/>, when there is one. A refusal is reported on stderr and comes
    /// back as a null session carrying the exit code.
    /// </summary>
    private static async Task<(ForgeSession? Session, int ExitCode)> OpenSessionAsync(
        string workspace, ForgeCommandOptions options, ForgeReference? reference)
    {
        if (options.ResumeSlug is not { } slug)
            return (await CreateSessionAsync(workspace, options, reference).ConfigureAwait(false), 0);

        if (!ForgeSession.TryLoadBySlug(workspace, slug, out var loaded, out var loadError))
        {
            await Console.Error.WriteLineAsync($"orkeon forge: {loadError}").ConfigureAwait(false);
            return (null, ExitError);
        }

        var session = loaded!;
        if (session.Status is ForgeSessionStatus.Failed)
        {
            await Console.Error.WriteLineAsync(
                $"orkeon forge: session '{slug}' is {session.Document.Status} and cannot be resumed.")
                .ConfigureAwait(false);
            return (null, ExitError);
        }

        // The reopen (W-09): anything that reached a verdict re-enters at the
        // arbitration — modify / re-try / re-adopt. No-op on an ordinary resume.
        session.TryReopen(DateTimeOffset.UtcNow);

        // The session's format is a fact of its artifacts: resuming cannot change it.
        if (options.Format is { } requestedFormat
            && !string.Equals(requestedFormat, session.Document.Format, StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                $"orkeon forge: session '{slug}' was created with --format {session.Document.Format}"
                + " — the format of a session cannot change on resume.")
                .ConfigureAwait(false);
            return (null, ExitError);
        }

        if (ForgeSession.IsScriptFormat(session.Document.Format) && !EsbuildAvailable())
        {
            await Console.Error.WriteLineAsync(
                $"orkeon forge: session '{slug}' renders a script crew and esbuild was not found"
                + " (FORGE-ESBUILD-MISSING) — install the toolchain (see `orkeon doctor`) and resume again.")
                .ConfigureAwait(false);
            return (null, ExitError);
        }

        RaiseBudget(session, options);
        return (session, 0);
    }

    /// <summary>
    /// A brand-new session, with the budget the command line asked for and the reference it is
    /// composed from, recorded in English until the promotion knows the brief's language (D-04).
    /// </summary>
    private static async Task<ForgeSession> CreateSessionAsync(
        string workspace, ForgeCommandOptions options, ForgeReference? reference)
    {
        var format = options.Format ?? ForgeSession.FormatYaml;
        if (ForgeSession.IsScriptFormat(format) && !EsbuildAvailable())
        {
            // The clean degradation of SPEC §8.2: fall back to YAML with the remedy,
            // never a transpilation error a beginner cannot read.
            await Console.Error.WriteLineAsync(
                "orkeon forge: esbuild was not found (FORGE-ESBUILD-MISSING) — rendering YAML instead;"
                + " install the script toolchain (see `orkeon doctor`) to get a .ork.ts crew.")
                .ConfigureAwait(false);
            format = ForgeSession.FormatYaml;
        }

        return ForgeSession.Create(
            workspace,
            options.Need,
            format: format,
            budget: new ForgeBudget
            {
                MaxIterations = options.MaxIterations ?? ForgeBudget.DefaultMaxIterations,
                MaxTokens = options.MaxTokens ?? 0,
                MaxWallSeconds = options.MaxSeconds ?? 0,
            },
            reference: reference?.RecordIn(language: null));
    }

    /// <summary>
    /// The dry-pause edit (v3 W-10): the client sends the amended blueprint over the channel,
    /// it is validated in full and the session moves back to a deterministic re-render.
    /// Returns the exit code when the amendment is refused, null when it was applied.
    /// </summary>
    private static async Task<int?> ApplyEditedBlueprintAsync(
        ForgeSession session,
        IForgeUserChannel channel,
        ForgeEventWriter events,
        IReadOnlyCollection<string> knownTools)
    {
        if (session.State != ForgeState.Test)
        {
            await Console.Error.WriteLineAsync(
                $"orkeon forge: --edit amends a session paused before its trial; session '{session.Document.Slug}'"
                + $" is at '{ForgeEventWriter.Spell(session.State)}' — at the arbitration, use the edit decision instead.")
                .ConfigureAwait(false);
            return ExitError;
        }

        events.SessionStarted(session, resumed: true);

        var json = await channel.ReadBlueprintAsync(CancellationToken.None).ConfigureAwait(false)
            ?? throw new OperationCanceledException("The user channel closed while sending the edited blueprint.");
        if (VerdictStage.ValidateEditedBlueprint(json, knownTools, events) is not { } edited)
        {
            // The session has not moved: it waits at the same pause, resumable again.
            events.SessionFinished("paused", ExitError);
            return ExitError;
        }

        session.SaveArtifact(ForgeSession.BlueprintFileName, edited);
        events.Emit("blueprint.ready", new { blueprint = edited, iteration = session.Document.Iteration });
        var now = DateTimeOffset.UtcNow;
        session.AppendHistory(ForgeState.Test, ForgeTrigger.BlueprintEdited, ForgeState.Render, now);
        session.SetState(ForgeState.Render);
        session.Save(now);
        return null;
    }

    /// <summary>Where a <c>--dry</c> run stopped and how to take it from there — terminal only.</summary>
    private static async Task AnnounceDryPauseAsync(
        ForgeSession session, ForgeCommandOptions options, ForgeEngineResult result)
    {
        if (options.Events || !options.Dry || result.Outcome != ForgeEngineOutcome.Paused)
            return;

        await Console.Out.WriteLineAsync(
            $"Generated and validated under {Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName)}."
            + $" Try it: orkeon forge resume {session.Document.Slug}"
            + $" — or keep it as it is, without a trial: orkeon forge resume {session.Document.Slug} --adopt")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// <c>forge resume &lt;slug&gt; --adopt</c>: take the team as generated, without running
    /// a trial. Fully offline — no host, no LLM, no run directory.
    /// <para>
    /// The pause this answers is the one <c>--dry</c> leaves behind: the crew is rendered
    /// and it passed validation, and the only thing the trial would add is evidence about
    /// how it behaves. Promotion never needed that evidence — <c>verdict.json</c> is
    /// optional and the generated card already says «no verdict recorded» when there is
    /// none — so the trial was a toll, not a check. Refusing to pay it is the user's call;
    /// the session records that it was skipped rather than pretending a verdict was earned.
    /// </para>
    /// </summary>
    private static async Task<int> AdoptAsync(string workspace, ForgeCommandOptions options)
    {
        if (!ForgeSession.TryLoadBySlug(workspace, options.ResumeSlug!, out var session, out var loadError))
        {
            await Console.Error.WriteLineAsync($"orkeon forge: {loadError}").ConfigureAwait(false);
            return ExitError;
        }

        using var renderer = options.Events ? null : new ForgeTerminalRenderer(Console.Out);
        var events = new ForgeEventWriter(renderer ?? Console.Out);
        events.SessionStarted(session!, resumed: true);

        if (!session!.TryAdoptWithoutTrial(DateTimeOffset.UtcNow))
        {
            var detail =
                $"session '{options.ResumeSlug}' is {session.Document.Status} at "
                + $"'{ForgeEventWriter.Spell(session.State)}' — adoption without a trial answers the "
                + "pause left by --dry, where the crew is generated and validated and nothing has run.";
            // Recoverable, and finished on the status the session ACTUALLY holds: nothing
            // moved on disk, so calling the session failed would strand a client on a
            // verdict about the session rather than about the command it just refused.
            events.Error(ForgeErrorCodes.InvalidState, detail, recoverable: true);
            events.SessionFinished(StatusWord(session), ExitError);
            if (!options.Events)
                await Console.Error.WriteLineAsync($"orkeon forge: {detail}").ConfigureAwait(false);
            return ExitError;
        }

        events.SessionFinished("ready", 0);
        if (!options.Events)
        {
            await Console.Out.WriteLineAsync(
                $"Adopted without a trial. Ship it with: orkeon forge promote {session.Document.Slug} --to <directory>")
                .ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// <c>forge reopen &lt;team-folder&gt;</c> (FORGE-09): makes sure a session exists for a
    /// promoted team, so the atelier can be reopened on it — « Modify » in Studio, or
    /// <c>forge resume</c> here. Fully offline — no host, no LLM.
    /// <para>
    /// Rule R (<see cref="ForgeTeamLink"/>, STUDIO-25) decides which session the folder is
    /// linked to: the one whose id its <c>forge.json</c> carries, unless that session's
    /// <c>promotedTo</c> names another folder still carrying the same id — then this one is a
    /// copy. A linked session is reported as it stands; nothing is moved — the resume that
    /// follows does the reopen, as it always did — except its <c>promotedTo</c>, pointed at the
    /// folder when the folder was moved or renamed since. When no session is linked (deleted,
    /// imported, forged on another machine, a copy, a folder without an id), one is
    /// <b>rebuilt</b> from the folder itself: the plan read back from <c>crew/</c>, the brief
    /// from <c>forge.json</c> when the promotion left one — derived from the plan otherwise, and
    /// said so — and the crew copied verbatim. It lands at the dry pause with <c>promotedTo</c>
    /// set and its new id written into the folder's <c>forge.json</c>, so an amendment, a trial
    /// or an adoption follow exactly as after <c>--dry</c>, the re-adoption updates the same
    /// folder in place, and the next reopen finds this session instead of rebuilding another.
    /// </para>
    /// </summary>
    private static async Task<int> ReopenAsync(string workspace, ForgeCommandOptions options)
    {
        var teamDirectory = Path.GetFullPath(options.ReopenDirectory!);
        using var renderer = options.Events ? null : new ForgeTerminalRenderer(Console.Out);
        var events = new ForgeEventWriter(renderer ?? Console.Out);

        if (!Directory.Exists(teamDirectory))
        {
            return await RefuseReopenAsync(events, options, $"reopen names no directory: '{teamDirectory}'.")
                .ConfigureAwait(false);
        }

        var link = ForgeTeamLink.Resolve(workspace, teamDirectory);
        if (link is { IsLinked: true, Session: { } existing })
        {
            if (link.Kind == TeamSessionLinkKind.Moved)
            {
                // Case 3: the original, moved or renamed. The session follows it, so its next
                // promotion updates the folder where it now is — and a copy made from here on
                // is told apart from it by this path.
                existing.Document.PromotedTo = teamDirectory;
                existing.Save(DateTimeOffset.UtcNow);
            }

            events.SessionStarted(existing, resumed: true);
            events.Emit("team.reopened", new
            {
                slug = existing.Document.Slug,
                dir = existing.Directory,
                path = teamDirectory,
                state = ForgeEventWriter.Spell(existing.State),
                rebuilt = false,
            });
            events.SessionFinished(StatusWord(existing), 0);
            if (!options.Events)
            {
                var moved = link.Kind == TeamSessionLinkKind.Moved ? ", since moved here — it now points here" : "";
                await Console.Out.WriteLineAsync(
                    $"Session '{existing.Document.Slug}' promoted this folder{moved}. Reopen it: orkeon forge resume {existing.Document.Slug}")
                    .ConfigureAwait(false);
            }

            return 0;
        }

        // A copy gets a session of its own (D-05): the rebuild rewrites the id the copy carried,
        // so it can never reopen, nor update, its original's.
        var isCopy = link.Kind == TeamSessionLinkKind.Copy;
        ForgeRebuildResult rebuilt;
        try
        {
            rebuilt = ForgeSessionRebuilder.Rebuild(workspace, teamDirectory, DateTimeOffset.UtcNow, isCopy);
        }
        catch (InvalidOperationException ex)
        {
            return await RefuseReopenAsync(events, options, ex.Message).ConfigureAwait(false);
        }

        var session = rebuilt.Session;
        var briefWord = rebuilt.BriefSource == ForgeBriefSource.Recorded ? "recorded" : "derived";
        events.SessionStarted(session, resumed: false);
        events.Emit("team.reopened", new
        {
            slug = session.Document.Slug,
            dir = session.Directory,
            path = teamDirectory,
            state = ForgeEventWriter.Spell(session.State),
            rebuilt = true,
            brief = briefWord,
        });
        events.SessionFinished("paused", 0);

        if (!options.Events)
        {
            var slug = session.Document.Slug;
            var copyOf = isCopy
                ? $"This folder is a copy of '{link.Session!.Document.PromotedTo}': it gets a session of its own. "
                : "";
            await Console.Out.WriteLineAsync(
                copyOf
                + $"Session '{slug}' rebuilt from {Path.Combine(teamDirectory, ForgeYamlRenderer.CrewDirectoryName)}"
                + (rebuilt.BriefSource == ForgeBriefSource.Recorded
                    ? $" with the brief {ForgeTeamRecord.FileName} recorded."
                    : $" — no brief recorded here ({ForgeTeamRecord.FileName}), so the brief was derived from the plan.")
                + $" Amend it: orkeon forge resume {slug} --edit --dry · try it: orkeon forge resume {slug}"
                + $" · keep it as it is: orkeon forge resume {slug} --adopt")
                .ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// A reopen that could not produce a session: no session started, so the stream carries the
    /// error and closes on «failed» — the verdict is on the command, there is no session to judge.
    /// </summary>
    private static async Task<int> RefuseReopenAsync(ForgeEventWriter events, ForgeCommandOptions options, string detail)
    {
        events.Error(ForgeErrorCodes.TeamUnreadable, detail, recoverable: false);
        events.SessionFinished("failed", ExitError);
        if (!options.Events)
            await Console.Error.WriteLineAsync($"orkeon forge: {detail}").ConfigureAwait(false);
        return ExitError;
    }

    /// <summary>
    /// The protocol's word for where a session stands, for a command that refused to move it.
    /// «failed» is reserved for a session that actually broke; a session that is merely not
    /// where the command applies is still exactly as resumable as it was a moment ago.
    /// </summary>
    private static string StatusWord(ForgeSession session) => session.Status switch
    {
        ForgeSessionStatus.Ready => "ready",
        ForgeSessionStatus.Abandoned => "abandoned",
        ForgeSessionStatus.Promoted => "promoted",
        ForgeSessionStatus.Failed => "failed",
        _ => "paused",
    };

    /// <summary>
    /// <c>forge promote &lt;slug&gt; --to &lt;dir&gt; [--name &lt;team&gt;]</c> (SPEC §11): ships a
    /// Ready session as an ordinary folder. Fully offline — no host, no LLM. A failed write
    /// leaves the session Ready and retryable; only success moves it to Promoted, with the
    /// transition recorded in the history like any other.
    /// <para>
    /// The team's name, when given, titles the session, <c>FORGE.md</c> and <c>forge.json</c>;
    /// once the promotion is written the session folder follows the team folder's name
    /// (STUDIO-26, <see cref="ForgeSessionFolder"/>) — a move the disk refuses is a warning, and
    /// the command still succeeds: the promotion stands, the id links the two.
    /// </para>
    /// </summary>
    private static async Task<int> PromoteAsync(string workspace, ForgeCommandOptions options)
    {
        if (!ForgeSession.TryLoadBySlug(workspace, options.PromoteSlug!, out var session, out var loadError))
        {
            await Console.Error.WriteLineAsync($"orkeon forge: {loadError}").ConfigureAwait(false);
            return ExitError;
        }

        if (session!.Status != ForgeSessionStatus.Ready)
        {
            await Console.Error.WriteLineAsync(
                $"orkeon forge: session '{options.PromoteSlug}' is {session.Document.Status} — only a ready session can be promoted.")
                .ConfigureAwait(false);
            return ExitError;
        }

        using var renderer = options.Events ? null : new ForgeTerminalRenderer(Console.Out);
        var events = new ForgeEventWriter(renderer ?? Console.Out);
        events.SessionStarted(session, resumed: true);

        // The team's name titles the session before the folder is written, so FORGE.md and
        // forge.json carry it from their first line (D-02). In memory only until the promotion
        // succeeds: a refused one saves nothing, and the session keeps the title it had.
        if (options.TeamName is { } teamName)
            session.Document.Title = teamName;

        // Quietly: promotion is offline by design and needs the path only to reference it
        // from the launch scripts. Warning that no model is configured would be a false
        // alarm on a command that never talks to one — a missing settings file simply means
        // the generated launcher carries no --settings line.
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, workspace, quiet: true);
        ForgePromotionResult result;
        var now = DateTimeOffset.UtcNow;
        try
        {
            result = ForgePromoter.Promote(
                session,
                Path.GetFullPath(options.Destination!),
                options.Schedule,
                settingsPath,
                options.WithSettings,
                ForgePromoter.DetectPlatform(),
                now);

            session.AppendHistory(ForgeState.Ready, ForgeTrigger.Promote, ForgeState.Promoted, now);
            session.SetState(ForgeState.Promoted);
            session.SetStatus(ForgeSessionStatus.Promoted);
            session.Document.PromotedTo = result.Destination;
            session.Save(now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            events.Error(ForgeErrorCodes.PromoteFailed, ex.Message, recoverable: true);
            events.SessionFinished("ready", ExitError);
            return ExitError;
        }

        events.Emit("promoted", result.ScheduleDirectory is null
            ? new { path = result.Destination, launcher = result.Launcher, updated = result.Updated }
            : (object)new
            {
                path = result.Destination,
                launcher = result.Launcher,
                schedule = result.ScheduleDirectory,
                install = result.InstallCommand,
                updated = result.Updated,
            });

        // The promotion is written: the session folder follows the team's (D-02). Outside the
        // guard above on purpose — nothing that happens from here may read as a failed
        // promotion: a move the disk refuses is a warning on the stream, and the command succeeds.
        ForgeSessionFolder.FollowTeam(session, result.Destination, events, now);
        events.SessionFinished("ready", 0);
        return 0;
    }

    /// <summary>
    /// A resume may raise the budget — that is the whole point of resuming a
    /// budget-exhausted session. Consumption always carries over: raising the ceiling
    /// never refunds what was spent.
    /// </summary>
    private static void RaiseBudget(ForgeSession session, ForgeCommandOptions options)
    {
        if (options.MaxIterations is null && options.MaxTokens is null && options.MaxSeconds is null)
            return;

        var current = session.Document.Budget;
        session.Document.Budget = new ForgeBudget
        {
            MaxIterations = options.MaxIterations ?? current.MaxIterations,
            MaxTokens = options.MaxTokens ?? current.MaxTokens,
            MaxWallSeconds = options.MaxSeconds ?? current.MaxWallSeconds,
            ConsumedIterations = current.ConsumedIterations,
            ConsumedTokens = current.ConsumedTokens,
            ConsumedPromptTokens = current.ConsumedPromptTokens,
            ConsumedCompletionTokens = current.ConsumedCompletionTokens,
            ConsumedEstimatedTokens = current.ConsumedEstimatedTokens,
            ConsumedWallSeconds = current.ConsumedWallSeconds,
        };

        if (session.Status == ForgeSessionStatus.BudgetExhausted)
            session.SetStatus(ForgeSessionStatus.Active);
    }

    /// <summary>
    /// Whether the esbuild binary resolves through the transpiler's own chain (config →
    /// env → bundled → repo-local → PATH) — the exact resolution `orkeon run` will use,
    /// not a parallel probe that could drift.
    /// </summary>
    private static bool EsbuildAvailable()
    {
        try
        {
            using var transpiler = new Orkeon.Scripting.Toolchain.EsbuildTranspiler();
            transpiler.ResolveBinary();
            return true;
        }
        catch (Orkeon.Scripting.Toolchain.EsbuildNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Prints the workspace's sessions, most recently touched first.</summary>
    private static async Task<int> ListAsync(string workspace)
    {
        var sessions = ForgeSession.List(workspace);
        if (sessions.Count == 0)
        {
            await Console.Out.WriteLineAsync($"No forge session under {ForgeSession.RootFor(workspace)}.")
                .ConfigureAwait(false);
            return 0;
        }

        await Console.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{"SLUG",-34} {"STATE",-10} {"STATUS",-16} {"FORMAT",-7} UPDATED")).ConfigureAwait(false);

        foreach (var session in sessions)
        {
            await Console.Out.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"{session.Slug,-34} {SpellState(session.State),-10} {session.Status,-16} {session.Format,-7} {session.UpdatedAt}"))
                .ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>States print in the protocol's lowercase spelling; unknown strings pass through.</summary>
    private static string SpellState(string state) =>
        Enum.TryParse<ForgeState>(state, out var parsed) ? ForgeEventWriter.Spell(parsed) : state;

    /// <summary>
    /// Turns an exception into a line the reader can act on.
    /// <para>
    /// A bare <see cref="TimeoutException"/> carries the framework's default sentence — «The
    /// operation has timed out.» — which names nothing and suggests nothing. On this path it
    /// means the scripting engine's wall-clock ceiling ended an assistant turn, LLM latency
    /// included, so say which knob raises it.
    /// </para>
    /// </summary>
    private static string Explain(Exception exception) => exception switch
    {
        TimeoutException => "the assistant's turn ran past the scripting engine's time limit. "
            + "The session is saved — resume it, or raise "
            + "Orkeon:Scripting:Limits:ExecutionTimeout if the model is simply slow.",
        _ => exception.Message,
    };

}
