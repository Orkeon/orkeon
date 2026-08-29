using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary><c>--to</c>: destination directory of a promotion.</summary>
    public string? Destination { get; init; }

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

    /// <summary>What made the parse fail, when it did.</summary>
    public string? Error { get; init; }

    /// <summary>Parses the <c>forge</c> arguments; unknown options fail loudly, never silently.</summary>
    public static ForgeCommandOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var needWords = new List<string>();
        var options = new ForgeCommandOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "list" when i == 0:
                    return options with { List = true };

                case "resume" when i == 0:
                    if (i + 1 >= args.Length)
                        return options with { Error = "resume needs a session slug (see `orkeon forge list`)." };
                    options = options with { ResumeSlug = args[++i] };
                    continue;

                case "promote" when i == 0:
                    if (i + 1 >= args.Length)
                        return options with { Error = "promote needs a session slug (see `orkeon forge list`)." };
                    options = options with { PromoteSlug = args[++i] };
                    continue;

                case "--to":
                    if (!TryTakeValue(args, ref i, out var destination))
                        return options with { Error = "--to needs a destination directory." };
                    options = options with { Destination = destination };
                    continue;

                case "--schedule":
                    if (!TryTakeValue(args, ref i, out var scheduleText))
                        return options with { Error = "--schedule needs a value: daily@HH:mm or hourly." };
                    if (!ForgeSchedule.TryParse(scheduleText, out var schedule, out var scheduleError))
                        return options with { Error = scheduleError };
                    options = options with { Schedule = schedule };
                    continue;

                case "--with-settings":
                    options = options with { WithSettings = true };
                    continue;

                case "--format":
                    if (!TryTakeValue(args, ref i, out var format))
                        return options with { Error = "--format needs a value: yaml or script." };
                    options = options with { Format = format };
                    continue;

                case "--events":
                    // The only stream format is jsonl; the value is accepted for the spec's
                    // spelling (`--events jsonl`) and for forward compatibility.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        i++;
                    options = options with { Events = true };
                    continue;

                case "--auto":
                    options = options with { Auto = true };
                    continue;

                case "--dry":
                    options = options with { Dry = true };
                    continue;

                case "--edit":
                    options = options with { Edit = true };
                    continue;

                case "--max-iterations":
                    if (!TryTakeValue(args, ref i, out var iterations)
                        || !int.TryParse(iterations, NumberStyles.None, CultureInfo.InvariantCulture, out var maxIterations)
                        || maxIterations < 1)
                        return options with { Error = "--max-iterations needs a positive integer." };
                    options = options with { MaxIterations = maxIterations };
                    continue;

                case "--max-tokens":
                    if (!TryTakeValue(args, ref i, out var tokens)
                        || !long.TryParse(tokens, NumberStyles.None, CultureInfo.InvariantCulture, out var maxTokens))
                        return options with { Error = "--max-tokens needs a non-negative integer (0 = unlimited)." };
                    options = options with { MaxTokens = maxTokens };
                    continue;

                case "--max-seconds":
                    if (!TryTakeValue(args, ref i, out var seconds)
                        || !long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var maxSeconds))
                        return options with { Error = "--max-seconds needs a non-negative integer (0 = unlimited)." };
                    options = options with { MaxSeconds = maxSeconds };
                    continue;

                case "--settings":
                    if (!TryTakeValue(args, ref i, out var settings))
                        return options with { Error = "--settings needs a path." };
                    options = options with { SettingsPath = settings };
                    continue;

                case "--pack":
                    if (!TryTakeValue(args, ref i, out var pack))
                        return options with { Error = "--pack needs a directory." };
                    options = options with { PackDirectory = pack };
                    continue;

                default:
                    if (arg.StartsWith('-'))
                        return options with { Error = $"Unknown option '{arg}'." };
                    needWords.Add(arg);
                    continue;
            }
        }

        if (options.PromoteSlug is not null && options.Destination is null)
            return options with { Error = "promote needs --to <directory>." };
        if (options.PromoteSlug is null && (options.Destination is not null || options.Schedule is not null || options.WithSettings))
            return options with { Error = "--to, --schedule and --with-settings only apply to `forge promote`." };
        if (options.Edit && options.ResumeSlug is null)
            return options with { Error = "--edit only applies to `forge resume`." };

        return options with { Need = needWords.Count > 0 ? string.Join(' ', needWords) : null };
    }

    private static bool TryTakeValue(string[] args, ref int i, out string value)
    {
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
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
/// <c>promote &lt;slug&gt; --to &lt;dir&gt;</c> ships a Ready session as an ordinary folder.
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
            return await RunCycleAsync(workspace, options).ConfigureAwait(false);
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
            await Console.Error.WriteLineAsync($"orkeon forge: {ex.Message}").ConfigureAwait(false);
            return ExitRuntimeError;
        }
#pragma warning restore CA1031
    }

    private static async Task<int> RunCycleAsync(string workspace, ForgeCommandOptions options)
    {
        // Open or create the session first: it is cheap, offline, and `resume` must be
        // able to say "no such session" before any host boots.
        ForgeSession session;
        var resumed = options.ResumeSlug is not null;
        if (options.ResumeSlug is { } slug)
        {
            if (!ForgeSession.TryLoadBySlug(workspace, slug, out var loaded, out var loadError))
            {
                await Console.Error.WriteLineAsync($"orkeon forge: {loadError}").ConfigureAwait(false);
                return ExitError;
            }

            session = loaded!;
            if (session.Status is ForgeSessionStatus.Failed)
            {
                await Console.Error.WriteLineAsync(
                    $"orkeon forge: session '{slug}' is {session.Document.Status} and cannot be resumed.")
                    .ConfigureAwait(false);
                return ExitError;
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
                return ExitError;
            }

            if (ForgeSession.IsScriptFormat(session.Document.Format) && !EsbuildAvailable())
            {
                await Console.Error.WriteLineAsync(
                    $"orkeon forge: session '{slug}' renders a script crew and esbuild was not found"
                    + " (FORGE-ESBUILD-MISSING) — install the toolchain (see `orkeon doctor`) and resume again.")
                    .ConfigureAwait(false);
                return ExitError;
            }

            RaiseBudget(session, options);
        }
        else
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

            session = ForgeSession.Create(
                workspace,
                options.Need,
                format: format,
                budget: new ForgeBudget
                {
                    MaxIterations = options.MaxIterations ?? ForgeBudget.DefaultMaxIterations,
                    MaxTokens = options.MaxTokens ?? 0,
                    MaxWallSeconds = options.MaxSeconds ?? 0,
                });
        }

        // The engine host: same settings chain as `orkeon run` (explicit --settings →
        // next to the workspace → global), the workspace readable, the session writable,
        // and the forge's own services on top.
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, workspace);
        var box = new ForgeSubmissionBox();
        var tally = new ForgeUsageTally();
        var observer = new ForgeRunObserver();

        // The sandbox's write surface: /output lands inside the session, snapshotted per
        // run by the test stage. Everything else the crew sees is read-only.
        var outputDirectory = Path.Combine(session.Directory, TestStage.OutputDirectoryName);
        Directory.CreateDirectory(outputDirectory);

        // Forge accepts no --mount, so its settings file is the only place these three roots
        // can be claimed from — and that is precisely the input the reserved-root guard never
        // saw. Every other entry point calls it, on its command-line mounts; this one has to
        // call it on what the settings declare, or the collision comes back from a DI factory
        // as "Duplicate virtual paths" and reads as a crash instead of the mistake it is.
        if (!RunnerExecution.EnsureReservedRootsAreFree(
                [], settingsPath, [.. RunnerVirtualRoots.ForgeReserved]))
        {
            return 1;
        }

        using var host = RunnerHost.Build(
            settingsPath,
            cliMounts:
            [
                // Quoted, like every other spec the framework builds: a session or
                // workspace path carrying a ':' or ';' would otherwise split into the wrong
                // segments and the forge would die at host build with a grammar error about
                // a path the user never typed.
                $"{FileSystemMount.Quote(workspace)}:{RunnerVirtualRoots.Workspace}:ro",
                $"{FileSystemMount.Quote(session.Directory)}:{RunnerVirtualRoots.Forge}:rw",
                $"{FileSystemMount.Quote(outputDirectory)}:{RunnerVirtualRoots.Output}:rw",
            ],
            configureServices: (_, services) =>
            {
                services.AddSingleton(box);
                services.AddSingleton(tally);
                services.AddSingleton<ILlmUsageSink>(tally);
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
        var assistant = new ForgeCrewAssistant(host.Services, session, packPath);

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
            if (session.State != ForgeState.Test)
            {
                await Console.Error.WriteLineAsync(
                    $"orkeon forge: --edit amends a session paused before its trial; session '{session.Document.Slug}'"
                    + $" is at '{ForgeEventWriter.Spell(session.State)}' — at the arbitration, use the edit decision instead.")
                    .ConfigureAwait(false);
                return ExitError;
            }

            events.SessionStarted(session, resumed: true);
            announce = false;

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

        if (!options.Events && options.Dry && result.Outcome == ForgeEngineOutcome.Paused)
        {
            await Console.Out.WriteLineAsync(
                $"Generated and validated under {Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName)}."
                + $" Resume without --dry to test it: orkeon forge resume {session.Document.Slug}")
                .ConfigureAwait(false);
        }

        return result.ExitCode;
    }

    /// <summary>
    /// <c>forge promote &lt;slug&gt; --to &lt;dir&gt;</c> (SPEC §11): ships a Ready session
    /// as an ordinary folder. Fully offline — no host, no LLM. A failed write leaves the
    /// session Ready and retryable; only success moves it to Promoted, with the transition
    /// recorded in the history like any other.
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

        // Quietly: promotion is offline by design and needs the path only to reference it
        // from the launch scripts. Warning that no model is configured would be a false
        // alarm on a command that never talks to one — a missing settings file simply means
        // the generated launcher carries no --settings line.
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, workspace, quiet: true);
        try
        {
            var result = ForgePromoter.Promote(
                session,
                Path.GetFullPath(options.Destination!),
                options.Schedule,
                settingsPath,
                options.WithSettings,
                ForgePromoter.DetectPlatform(),
                DateTimeOffset.UtcNow);

            var now = DateTimeOffset.UtcNow;
            session.AppendHistory(ForgeState.Ready, ForgeTrigger.Promote, ForgeState.Promoted, now);
            session.SetState(ForgeState.Promoted);
            session.SetStatus(ForgeSessionStatus.Promoted);
            session.Document.PromotedTo = result.Destination;
            session.Save(now);

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
            events.SessionFinished("ready", 0);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            events.Error(ForgeErrorCodes.PromoteFailed, ex.Message, recoverable: true);
            events.SessionFinished("ready", ExitError);
            return ExitError;
        }
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
}
