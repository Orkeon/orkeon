using Orkeon.Constants.FileSystem;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Compliance.Vfs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Scripting;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Hosting;

/// <summary>
/// Shared execution glue for Orkeon runners: graceful shutdown (SIGTERM/SIGINT),
/// AutoSummaryWriter wiring on <c>/output:rw</c> mounts, verbosity presets, and the
/// one-shot kickoff and interactive-loop flows used by all runners.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: validates user-supplied config/log paths and injects the corresponding VFS mounts before the host (and thus IFileSystemService) is built.")]
public static partial class RunnerExecution
{
    /// <summary>
    /// Bootstrap result shared by <see cref="RunOneShotAsync"/> and
    /// <see cref="RunInteractiveLoopAsync"/>: a fully built host plus the
    /// runner-level metadata both flows need.
    /// </summary>
    /// <param name="Host">The built host.</param>
    /// <param name="Logger">The runner-level logger.</param>
    /// <param name="ConfigPath">
    /// The crew target as the operator typed it, resolved to an absolute physical path.
    /// Diagnostics and esbuild's import resolution only — never handed to the VFS.
    /// </param>
    /// <param name="VirtualConfigPath">
    /// The same target spelled for the VFS: <c>/crew</c> for a multi-file crew directory,
    /// <c>/crew/&lt;file&gt;</c> otherwise. This is what the loader is given.
    /// </param>
    /// <param name="IsCrewDirectory">
    /// Whether the target is a multi-file crew directory. Decided once, by
    /// <c>CrewDirectoryLayout.Inspect</c>, and carried rather than re-derived: asking the VFS
    /// again cannot see through a symlinked crew directory.
    /// </param>
    /// <param name="CliMounts">The mount strings the host was built with.</param>
    internal sealed record HostBootstrap(
        IHost Host,
        ILogger Logger,
        string ConfigPath,
        string VirtualConfigPath,
        bool IsCrewDirectory,
        IReadOnlyList<string> CliMounts);

    /// <summary>
    /// Resolves config/settings paths, validates the external-mounts guard,
    /// auto-injects the configDir and (if any) llmLogPath mounts, builds the
    /// host with verbosity-driven logging, and wires AutoSummaryWriter when an
    /// <c>/output:rw</c> mount is present. Returns an error exit code on
    /// invalid input; the caller must check <paramref name="errorCode"/>.
    /// <paramref name="configureLogging"/>, when given, replaces the verbosity-driven logging:
    /// an embedding application (<see cref="CrewScriptRunner"/>) owns its own log output.
    /// </summary>
    internal static bool TryBuildHost(
        RunnerOptionsBase opts,
        string loggerCategory,
        Action<HostBuilderContext, IServiceCollection>? configureServices,
        out HostBootstrap? bootstrap,
        out int errorCode,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging = null)
    {
        ArgumentNullException.ThrowIfNull(opts);
        bootstrap = null;
        errorCode = 0;

        if (!TryResolveCrewTarget(opts, out var target))
        {
            errorCode = 1;
            return false;
        }

        var cliMounts = opts.Mounts.ToList();
        var llmLogPath = opts.ResolvedLlmLogPath;

        // The crew's own mounts: block, read raw before any host: it is one of the inputs that
        // decide which settings entries the host mounts (VFS-90), and the loader that reads it
        // properly runs over the VFS those very mounts build.
        var crewMounts = target.IsScript
            ? CrewMountDeclarations.None
            : CrewMountDeclarations.Read(target.ConfigPath, target.IsCrewDirectory);

        // The layers the host will read its mounts from, read once for every guard (GAP-49,
        // decision 2): the environment without a prefix, the settings file, ORKEON_. A script has
        // no mounts: block (VFS-90, D-11), so its selection line names --mount-id and --mount only.
        var layers = RunnerSettings.ReadMountConfiguration(target.SettingsPath);
        var offered = target.IsScript
            ? OfferedMountSelectors.MountIdOption | OfferedMountSelectors.MountOption
            : OfferedMountSelectors.All;
        var error = opts.RunnerConsole.ErrorOutput;

        if (!EnsureVirtualRootsAreUnique(cliMounts, layers, target.SettingsPath, error)
            || !EnsureExternalMountsAllowed(opts, target.IsScript ? null : target.ConfigDir, llmLogPath, error)
            || !EnsureReservedRootsAreFree(
                cliMounts, layers, error,
                target.VirtualRoot, RunnerVirtualRoots.LlmLogs, RunnerVirtualRoots.Sandbox)
            || !EnsureMountSelectionIsResolvable(
                cliMounts, opts.MountIds, crewMounts, new MountLayers(layers, target.SettingsPath), offered, error, out var selection)
            || !EnsureMountSourcesExist(cliMounts, layers, selection, error)
            || !EnsureLlmProfileIsKnown(opts.LlmProfile, target.SettingsPath, error))
        {
            errorCode = 1;
            return false;
        }

        // Announced only once every guard has passed: "Using settings" reads as the run going
        // ahead with that file, and a refusal that arrived after it read as a run that had
        // started and crashed — the one-line diagnostics above are the whole answer.
        if (target.SettingsPath != null)
            error.WriteLine($"Using settings: {target.SettingsPath}");

        cliMounts.Insert(0, $"{FileSystemMount.Quote(target.ConfigDir)}:{target.VirtualRoot}:ro");

        var internalMounts = RunnerLlmLog.CreateMounts(llmLogPath);
        var verbosity = Math.Clamp(opts.Verbose, 0, 2);
        var outputMountPath = DetectOutputMountPath(cliMounts);

        IHost host;
        try
        {
            host = RunnerHost.Build(
                target.SettingsPath,
                new RunnerMountPlan
                {
                    CliMounts = cliMounts,
                    InternalMounts = internalMounts,
                    InputDirectories = target.IsScript ? [target.ConfigDir] : [],
                    SelectedMountIds = selection.SelectedMountIds,
                    CrewMountReferences = selection.CrewMountReferences,
                    AllowExternalMounts = opts.EffectiveAllowExternalMounts,
                    LlmLogVirtualPath = llmLogPath != null ? RunnerVirtualRoots.LlmLogs : null,
                },
                configureLogging: configureLogging ?? (verbosity > 0
                    ? (ctx, b) => ConfigureVerboseLogging(b, verbosity, ctx.Configuration)
                    : null),
                configureServices: (ctx, services) =>
                {
                    if (outputMountPath != null)
                    {
                        services.AddScoped<ICrewExecutionHook>(sp =>
                            new AutoSummaryWriter(
                                sp.GetRequiredService<IFileSystemService>(),
                                outputMountPath,
                                sp.GetRequiredService<ILogger<AutoSummaryWriter>>()));
                    }
                    // Default human-input wiring: registers the `human_input` tool +
                    // AutoApprove provider as a fallback. Interactive runners override
                    // the provider (e.g. TerminalGuiHumanInputProvider) via
                    // configureServices below.
                    services.AddOrkeonHumanInput();
                    // The runner's console, before the caller's services: the operator channel
                    // writes on it (RunnerOperatorLog), a caller may still replace it.
                    services.AddSingleton(opts.RunnerConsole);
                    configureServices?.Invoke(ctx, services);
                    // After the caller's own provider is in, so the deadline decorator wraps it.
                    services.DecorateHumanInputWithDeadline();
                },
                llmProfile: opts.LlmProfile);
        }
        catch (RunnerSettingsException ex)
        {
            // A setting the host refuses — a retired key, an unconvertible value, an unreadable
            // file, an address that is none — is the operator's to fix: the sentence as the last
            // stderr line, exit 1 (GAP-35). It used to escape as an unhandled exception: a stack,
            // a core dump, and on an --events run a stream that never closed.
            ReportRefusedSettings(ex, error);
            errorCode = 1;
            return false;
        }

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(loggerCategory);

        if (llmLogPath != null)
            LogLlmExchangeLoggingEnabled(logger, llmLogPath);

        bootstrap = new HostBootstrap(
            host, logger, target.ConfigPath, target.VirtualConfigPath, target.IsCrewDirectory, cliMounts);
        return true;
    }

    /// <summary>
    /// Writes a setting the host refused as one line, <c>ERROR: &lt;reason&gt;</c> — the last on
    /// stderr, the one the exit code points at. <c>ORKEON_DEBUG=1</c> puts the exception, the
    /// failure it explains and its stack, before it.
    /// </summary>
    /// <param name="ex">The refusal.</param>
    /// <param name="error">The runner's standard error.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    internal static void ReportRefusedSettings(RunnerSettingsException ex, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(ex);
        ArgumentNullException.ThrowIfNull(error);
        if (RunnerEnvironment.DebugDiagnostics)
            error.WriteLine(ex.ToString());
        error.WriteLine($"ERROR: {ex.Message}");
    }

    /// <summary>
    /// The crew target, decided once: the physical path the operator named, the directory that
    /// anchors settings and the config mount, the virtual root and spelling the loader is
    /// given, and the settings file that goes with them.
    /// </summary>
    /// <param name="ConfigPath">The target resolved to an absolute physical path.</param>
    /// <param name="ConfigDir">Directory mounted read-only under <paramref name="VirtualRoot"/>.</param>
    /// <param name="VirtualRoot"><c>/script</c> for a scripting entry point, <c>/crew</c> otherwise.</param>
    /// <param name="VirtualConfigPath">The target spelled for the VFS.</param>
    /// <param name="IsCrewDirectory">Whether the target is a multi-file crew directory.</param>
    /// <param name="IsScript">Whether the target is a scripting entry point.</param>
    /// <param name="SettingsPath">Resolved appsettings.json, or <see langword="null"/>.</param>
    private sealed record CrewTarget(
        string ConfigPath,
        string ConfigDir,
        string VirtualRoot,
        string VirtualConfigPath,
        bool IsCrewDirectory,
        bool IsScript,
        string? SettingsPath);

    /// <summary>
    /// Resolves what the operator pointed <c>--config</c> at: presence, single file versus
    /// multi-file crew directory, the anchor directory, the settings file, and the virtual
    /// spelling the loader is handed. Prints its own diagnostic and returns
    /// <see langword="false"/> on invalid input.
    /// </summary>
    private static bool TryResolveCrewTarget(
        RunnerOptionsBase opts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CrewTarget? target)
    {
        target = null;
        var error = opts.RunnerConsole.ErrorOutput;

        if (string.IsNullOrWhiteSpace(opts.ConfigPath))
        {
            // --config is optional at the parser level so --list-tools can run without it,
            // but every crew-loading mode still needs it — enforce presence here.
            error.WriteLine("ERROR: --config is required (path to the crew .yaml or .ork.ts).");
            return false;
        }

        var configPath = Path.GetFullPath(opts.ConfigPath);

        // A crew target is either a single file (.yaml / .ork.ts) or a directory holding a
        // multi-file crew (config.yaml + agents/ + tasks/, or the flat legacy triplet) — at its
        // root, or under its crew/ sub-folder, which then wins over whatever the root holds
        // (STUDIO-59). The directory form is classified here so an ambiguous or empty directory
        // reports its own diagnostic instead of the generic "config file not found".
        var inspection = CrewDirectoryLayout.Inspect(configPath);
        if (inspection.Error is not null)
        {
            error.WriteLine($"ERROR: {inspection.Error}");
            return false;
        }

        if (!inspection.IsCrewDirectory && !File.Exists(configPath))
        {
            error.WriteLine($"ERROR: config file not found: {configPath}");
            return false;
        }

        // For a crew directory the definition's directory IS the target — the root, or the
        // crew/ sub-folder the inspection designated: mounting it (rather than the folder the
        // operator named, or its parent) keeps the VFS surface as narrow as it is for a
        // single-file crew, and anchors appsettings resolution inside the crew: for a promoted
        // team the chain starts at crew/, so a settings file kept at the team folder's root is not
        // found — only crew/appsettings.json and an appsettings/ (or _shared/) folder above it —
        // and the workshop's launchers pass --settings.
        if (inspection.IsCrewDirectory)
            configPath = Path.TrimEndingDirectorySeparator(inspection.CrewRoot!);
        var configDir = inspection.IsCrewDirectory ? configPath : Path.GetDirectoryName(configPath)!;
        // Resolved here, announced by TryBuildHost once the mount guards have passed.
        var settingsPath = RunnerSettings.ResolveSettingsPath(opts.SettingsPath, configDir, error);

        // The crew-config loader (YamlCrewDefinitionLoader) reads the YAML through
        // IFileSystemService, so configDir must be visible to the VFS registry — under a
        // NAME (ADR-008), never identity-mapped. An agent asking `list_mounts`, or reading
        // an access-denied message, must never be handed an absolute disk path.
        // A scripting entry point gets /script, a YAML crew gets /crew — the two roots exist
        // because a script's relative imports resolve against its own directory. And a
        // script's directory is the CLI's primary input rather than a user-declared mount, so
        // it is not gated behind --allow-external-mounts, exactly as `orkeon run` argues on
        // its own script path. Both rules used to live only there: `--validate` on a .ork.ts
        // came through here instead, took a /crew topology the real run never uses, and was
        // refused for a script outside the working directory that runs perfectly well. A
        // validation that answers about a different arrangement than the run is worse than no
        // validation.
        var isScript = !inspection.IsCrewDirectory && IsScriptedCrewDefinition(configPath);
        var virtualRoot = isScript ? RunnerVirtualRoots.Script : RunnerVirtualRoots.Crew;
        var virtualConfigPath = inspection.IsCrewDirectory
            ? virtualRoot
            : $"{virtualRoot}/{Path.GetFileName(configPath)}";

        target = new CrewTarget(
            configPath, configDir, virtualRoot, virtualConfigPath,
            inspection.IsCrewDirectory, isScript, settingsPath);
        return true;
    }

    /// <summary>
    /// Validates the external-mounts guard: paths outside the current working directory
    /// require an explicit opt-in (<c>--allow-external-mounts</c>). Conservative by design —
    /// avoids silently widening the VFS surface for users who expect workspace-relative
    /// execution. Returns <see langword="false"/> (after printing the diagnostic) when blocked.
    /// <para>
    /// "Outside" is asked of <see cref="PhysicalPathContainment"/>, the same predicate
    /// <c>PathValidator</c> enforces the rule with. A bare <c>StartsWith</c> answered a
    /// different question here and the two answers only ever differed in the direction that
    /// hurts: a crew in a sibling directory whose name extends the cwd's
    /// (<c>~/proj</c> vs <c>~/proj-old</c>) read as <i>inside</i>, so the opt-in was never
    /// demanded, its base path never whitelisted, and the boundary-safe validator then
    /// refused every file the crew touched — with no mention of the flag that would have
    /// fixed it.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static bool EnsureExternalMountsAllowed(RunnerOptionsBase opts, string? configDir, string? llmLogPath, TextWriter error)
    {
        var cwd = Directory.GetCurrentDirectory();
        var configOutsideCwd = configDir is not null && !PhysicalPathContainment.IsUnder(configDir, cwd);
        var llmLogOutsideCwd = llmLogPath != null && !PhysicalPathContainment.IsUnder(llmLogPath, cwd);
        if (!(configOutsideCwd || llmLogOutsideCwd) || opts.EffectiveAllowExternalMounts)
            return true;

        error.WriteLine(
            "ERROR: --allow-external-mounts is required when reading the crew config "
            + "or writing LLM logs outside the current working directory. Add "
            + "--allow-external-mounts (or set ORKEON_ALLOW_EXTERNAL_MOUNTS=1) to proceed.");
        if (configOutsideCwd)
            error.WriteLine($"       configDir   : {configDir}");
        if (llmLogOutsideCwd)
            error.WriteLine($"       llmLogPath  : {llmLogPath}");
        error.WriteLine($"       cwd         : {cwd}");
        return false;
    }

    /// <summary>
    /// Refuses a mount that claims a virtual root the runner needs for itself, whether it was
    /// written as a <c>--mount</c> argument or declared in the settings — the file or the
    /// environment, prefixed or not. Without this the collision surfaces as a raw
    /// <see cref="InvalidOperationException"/> ("Duplicate virtual paths") thrown out of a DI
    /// factory, which reads as a crash rather than as the configuration mistake it is.
    /// <para>
    /// The settings are read HERE, from <paramref name="settingsPath"/>, rather than concatenated
    /// by each caller. It was a caller's job briefly, and three of the six mount-building entry
    /// points did not do it - including <c>orkeon-host</c>, the most settings-driven of them all.
    /// A guard whose completeness depends on every future caller remembering an argument is a
    /// guard that will be incomplete again; this signature cannot be called wrongly. What is read
    /// is the layers the host composes (<see cref="RunnerSettings.ReadDeclaredMounts(string?)"/>,
    /// GAP-49, decision 2).
    /// </para>
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="settingsPath">Resolved settings file whose declared mounts also count, or
    /// <see langword="null"/> when the command resolves none.</param>
    /// <param name="error">Where the refusal is written: the runner's standard error, or the caller's buffer.</param>
    /// <param name="reserved">The virtual roots this runner keeps for itself; <c>/credentials</c>,
    /// which the host keeps for every command, is always added.</param>
    public static bool EnsureReservedRootsAreFree(
        IEnumerable<string> userMounts,
        string? settingsPath,
        TextWriter error,
        params string[] reserved) =>
        EnsureReservedRootsAreFree(userMounts, RunnerSettings.ReadMountConfiguration(settingsPath), error, reserved);

    /// <summary>
    /// <see cref="EnsureReservedRootsAreFree(IEnumerable{string}, string?, TextWriter, string[])"/> over the
    /// layers a caller composed — the REPL's, its command line included —, the refusal written to
    /// <paramref name="error"/>.
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="configuration">The configuration the host is built over, before its own mounts.</param>
    /// <param name="error">Where the refusal is written: stderr, or the caller's buffer.</param>
    /// <param name="reserved">The virtual roots this command keeps for itself; <c>/credentials</c> is always added.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    internal static bool EnsureReservedRootsAreFree(
        IEnumerable<string> userMounts,
        IConfiguration configuration,
        TextWriter error,
        params string[] reserved)
    {
        ArgumentNullException.ThrowIfNull(userMounts);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(reserved);

        // /credentials belongs to the runner host whatever the command: the OAuth tokens of the
        // e-mail accounts live there (ADR-012), so every entry point refuses it.
        reserved = reserved.Contains(RunnerVirtualRoots.Credentials, StringComparer.Ordinal)
            ? reserved
            : [.. reserved, RunnerVirtualRoots.Credentials];

        foreach (var mountString in userMounts.Concat(RunnerSettings.ReadDeclaredMounts(configuration)))
        {
            string virtualPath;
            try
            {
                virtualPath = FileSystemMount.Parse(mountString).VirtualPath;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // Malformed strings — including a blank one, which Parse rejects with an
                // ArgumentException rather than a FormatException — are reported by the mount
                // parser at host build time, with its own precise message. Not this guard's.
                continue;
            }

            var clash = reserved.FirstOrDefault(r =>
                string.Equals(virtualPath.TrimEnd('/'), r, StringComparison.Ordinal));
            if (clash is null)
                continue;

            // The roots differ per entry point — the YAML runner reserves /crew, the forge
            // /workspace, /forge and /output — so the line names what THIS command reserves
            // rather than a fixed list that was false for whoever was not the YAML runner.
            error.WriteLine(
                $"ERROR: '{clash}' is a virtual root reserved by the runner: this command mounts "
                + $"it for itself (reserved here: {string.Join(", ", reserved)}). Give this mount "
                + "another virtual name.");
            error.WriteLine($"       mount       : {mountString}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Refuses two mounts claiming one virtual root from the same source: two <c>--mount</c>
    /// arguments, or two entries of the settings file that nothing can tell apart. Without this
    /// the collision surfaces as a raw <see cref="InvalidOperationException"/> ("Duplicate
    /// virtual paths") thrown out of a DI factory at kickoff — after "Using settings", under a
    /// stack trace — where one line and an exit code 1 before any host boots say what to fix
    /// (STUDIO-15 D-02).
    /// <para>
    /// A <c>--mount</c> on a root the settings declare is NOT a duplicate: the host places a
    /// command-line mount by root and it replaces that settings entry for the run
    /// (<c>RunnerHost</c>, D-01). Nor are two settings entries of one root that both carry an
    /// id (VFS-90, D-03): a <c>--mount-id</c> or the crew's <c>mounts:</c> picks one per run —
    /// <see cref="EnsureMountSelectionIsResolvable(IEnumerable{string}, IEnumerable{string}, CrewMountDeclarations, string?, TextWriter, out MountSelectionPlan?)"/> checks that something does. What has no
    /// answer but "keep one" is a root claimed twice on the command line, an id declared twice,
    /// a root declared twice with an entry that has no id, or an Internal mount on a root the
    /// agent-facing array already holds (the registry's duplicate check spans both sections).
    /// </para>
    /// <para>
    /// Same completeness stance as <see cref="EnsureReservedRootsAreFree(IEnumerable{string}, string?, TextWriter, string[])"/>:
    /// the settings are read HERE from <paramref name="settingsPath"/>, so no caller can forget them.
    /// </para>
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="settingsPath">Resolved settings file whose declared mounts are checked
    /// against each other, or <see langword="null"/> when the command resolves none.</param>
    /// <param name="error">Where the refusal is written: the runner's standard error, or the caller's buffer.</param>
    public static bool EnsureVirtualRootsAreUnique(
        IEnumerable<string> userMounts,
        string? settingsPath,
        TextWriter error) =>
        EnsureVirtualRootsAreUnique(userMounts, RunnerSettings.ReadMountConfiguration(settingsPath), settingsPath, error);

    /// <summary>
    /// <see cref="EnsureVirtualRootsAreUnique(IEnumerable{string}, string?, TextWriter)"/> over the layers a
    /// caller composed, the refusal written to <paramref name="error"/>.
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="configuration">The configuration the host is built over, before its own mounts.</param>
    /// <param name="settingsLabel">What the refusal names the declarations by: the file, or the files separated by a comma; null for the configuration alone.</param>
    /// <param name="error">Where the refusal is written: stderr, or the caller's buffer.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    internal static bool EnsureVirtualRootsAreUnique(
        IEnumerable<string> userMounts,
        IConfiguration configuration,
        string? settingsLabel,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(userMounts);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(error);

        if (!RootsAreClaimedOnce(userMounts, "mounted twice on the command line", error))
            return false;

        var declared = RunnerSettings.ReadDeclaredAgentFacingMounts(configuration);
        var errors = MountSelection.ValidateDeclared(declared, settingsLabel);
        if (errors.Count > 0)
        {
            foreach (var refusal in errors)
                error.WriteLine("ERROR: " + refusal);
            return false;
        }

        // The Internal section must not claim a root the agent-facing one holds, whichever of
        // its entries the run ends up keeping.
        var agentFacingRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var spec in declared.Select(entry => entry.Spec))
        {
            if (MountSelection.TryGetVirtualRoot(spec) is { } root)
                agentFacingRoots.TryAdd(root, spec);
        }

        return RootsAreClaimedOnce(
            RunnerSettings.ReadDeclaredInternalMounts(configuration),
            $"declared twice in {(string.IsNullOrEmpty(settingsLabel) ? "the configuration" : settingsLabel)}",
            error,
            agentFacingRoots);
    }

    /// <summary>
    /// Refuses a run whose mount selection cannot be resolved (VFS-90): a malformed
    /// <c>--mount-id</c>, a malformed item in the crew's <c>mounts:</c> block, an id no entry
    /// carries, a root the crew requires that nothing provides, and — D-04 — a root declared
    /// several times with nothing selecting one. The plan it returns is what the host applies,
    /// so the guard and the host cannot disagree.
    /// <para>
    /// Same completeness stance as <see cref="EnsureReservedRootsAreFree(IEnumerable{string}, string?, TextWriter, string[])"/>:
    /// the settings are read HERE from <paramref name="settingsPath"/>. Warnings are printed and
    /// never refuse.
    /// </para>
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="mountIds">The <c>--mount-id</c> values, unparsed.</param>
    /// <param name="crewMounts">The crew's <c>mounts:</c> block, pre-read; <see cref="CrewMountDeclarations.None"/>
    /// for a script or when the command has no crew.</param>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    /// <param name="error">Where the refusal and the warnings are written: the runner's standard error, or the caller's buffer.</param>
    /// <param name="plan">The resolved plan when the guard passes.</param>
    public static bool EnsureMountSelectionIsResolvable(
        IEnumerable<string> userMounts,
        IEnumerable<string> mountIds,
        CrewMountDeclarations crewMounts,
        string? settingsPath,
        TextWriter error,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MountSelectionPlan? plan) =>
        EnsureMountSelectionIsResolvable(userMounts, mountIds, crewMounts, settingsPath, OfferedMountSelectors.All, error, out plan);

    /// <summary>
    /// <see cref="EnsureMountSelectionIsResolvable(IEnumerable{string}, IEnumerable{string}, CrewMountDeclarations, string?, TextWriter, out MountSelectionPlan?)"/>
    /// for a command that offers <paramref name="offered"/> to choose between the entries of a root:
    /// the line of a root declared several times names those ways only (GAP-49, decision 3).
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="mountIds">The <c>--mount-id</c> values, unparsed.</param>
    /// <param name="crewMounts">The crew's <c>mounts:</c> block, pre-read, or <see cref="CrewMountDeclarations.None"/>.</param>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    /// <param name="offered">What the command offers to choose with.</param>
    /// <param name="error">Where the refusal and the warnings are written.</param>
    /// <param name="plan">The resolved plan when the guard passes.</param>
    internal static bool EnsureMountSelectionIsResolvable(
        IEnumerable<string> userMounts,
        IEnumerable<string> mountIds,
        CrewMountDeclarations crewMounts,
        string? settingsPath,
        OfferedMountSelectors offered,
        TextWriter error,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MountSelectionPlan? plan) =>
        EnsureMountSelectionIsResolvable(
            userMounts, mountIds, crewMounts, new MountLayers(RunnerSettings.ReadMountConfiguration(settingsPath), settingsPath), offered, error, out plan);

    /// <summary>
    /// The selection guard over the layers a caller composed — the REPL's, its command line
    /// included —, its lines written to <paramref name="error"/>.
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="mountIds">The <c>--mount-id</c> values, unparsed.</param>
    /// <param name="crewMounts">The crew's <c>mounts:</c> block, pre-read, or <see cref="CrewMountDeclarations.None"/>.</param>
    /// <param name="layers">The configuration the host is built over, before its own mounts, and what the lines name its declarations by.</param>
    /// <param name="offered">What the command offers to choose with.</param>
    /// <param name="error">Where the refusal and the warnings are written: stderr, or the caller's buffer.</param>
    /// <param name="plan">The resolved plan when the guard passes.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    internal static bool EnsureMountSelectionIsResolvable(
        IEnumerable<string> userMounts,
        IEnumerable<string> mountIds,
        CrewMountDeclarations crewMounts,
        MountLayers layers,
        OfferedMountSelectors offered,
        TextWriter error,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MountSelectionPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(userMounts);
        ArgumentNullException.ThrowIfNull(mountIds);
        ArgumentNullException.ThrowIfNull(crewMounts);
        ArgumentNullException.ThrowIfNull(layers);
        var (configuration, settingsLabel) = layers;
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(error);
        plan = null;

        var ids = new List<MountId>();
        foreach (var raw in mountIds)
        {
            if (!MountId.TryParse(raw, out var id))
            {
                error.WriteLine(
                    $"ERROR: --mount-id '{raw}' is not a mount id: expected the 26 Crockford base32 characters "
                    + "a settings entry carries before its '|' (0-9, A-Z without I, L, O, U).");
                return false;
            }

            if (!ids.Contains(id))
                ids.Add(id);
        }

        var references = new List<MountReference>();
        foreach (var item in crewMounts.Items)
        {
            if (!MountReference.TryParse(item, out var reference))
            {
                error.WriteLine(
                    $"ERROR: crew mounts: entry '{item}' in {crewMounts.SourceFile} is neither '/root' nor '<ulid>|/root' "
                    + "(a virtual root starts with '/', a mount id is the 26-character ULID a settings entry carries before its '|').");
                return false;
            }

            if (!references.Contains(reference))
                references.Add(reference);
        }

        var resolved = MountSelection.Resolve(
            RunnerSettings.ReadDeclaredAgentFacingMounts(configuration),
            userMounts.ToList(),
            ids,
            references,
            settingsLabel,
            offered,
            withdrawsAmbiguousRoots: offered == OfferedMountSelectors.None);

        foreach (var warning in resolved.Warnings)
            error.WriteLine("WARNING: " + warning);
        if (resolved.Errors.Count > 0)
        {
            foreach (var refusal in resolved.Errors)
                error.WriteLine("ERROR: " + refusal);
            return false;
        }

        plan = resolved;
        return true;
    }

    /// <summary>
    /// Refuses an <c>--llm-profile</c> the configuration does not define (STUDIO-50) in one line —
    /// the sentence a crew naming an unknown profile fails its load with, listing the profiles the
    /// configuration a run reads defines (<see cref="RunnerSettings.ReadConfiguration"/>) — before
    /// any host is built. Null,
    /// blank and <c>default</c> elect nothing and always pass. A settings file that cannot be read
    /// passes too: the host build reports it, with its path and the parse position.
    /// <para>
    /// The host refuses the same id the same way (<see cref="RunnerHost.Build"/>): this is the
    /// runners' one line, that the closed net for a host built without the guard.
    /// </para>
    /// </summary>
    /// <param name="llmProfile">The <c>--llm-profile</c> value, or null.</param>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    /// <param name="error">Where the refusal is written: the runner's standard error, or the caller's buffer.</param>
    /// <returns>True when the run may go on.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static bool EnsureLlmProfileIsKnown(string? llmProfile, string? settingsPath, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (llmProfile is null || Application.Interfaces.Ports.LlmProfiles.IsDefault(llmProfile))
            return true;

        IReadOnlyList<string> known;
        try
        {
            known = Infrastructure.LLMs.Profiles.LlmSettings.ProfileNames(RunnerSettings.ReadConfiguration(settingsPath));
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException)
        {
            return true;
        }

        var name = llmProfile.Trim();
        if (known.Contains(name, StringComparer.OrdinalIgnoreCase))
            return true;

        error.WriteLine("ERROR: " + RunnerHost.UnknownElectedProfileMessage(name, known));
        return false;
    }

    /// <summary>
    /// One source's mounts, each root claimed at most once. Malformed strings are left to the
    /// mount parser, which reports them at host build time with its own precise message.
    /// </summary>
    /// <param name="mountStrings">The mounts of the source being checked.</param>
    /// <param name="howItWasClaimed">The words of the refusal.</param>
    /// <param name="error">Where the refusal is written.</param>
    /// <param name="alreadyClaimed">Roots another source already holds, by root, with the
    /// mount string that holds them; a claim on one of those is refused too.</param>
    private static bool RootsAreClaimedOnce(
        IEnumerable<string> mountStrings,
        string howItWasClaimed,
        TextWriter error,
        Dictionary<string, string>? alreadyClaimed = null)
    {
        var firstClaim = alreadyClaimed ?? new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mountString in mountStrings)
        {
            string virtualPath;
            try
            {
                virtualPath = FileSystemMount.Parse(mountString).VirtualPath;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                continue;
            }

            // Ordinal, like the registry — with the trailing slash dropped, so '/output/' and
            // '/output' read as the one root they are.
            var root = virtualPath.Length > 1 ? virtualPath.TrimEnd('/') : virtualPath;
            if (firstClaim.TryAdd(root, mountString))
                continue;

            error.WriteLine(
                $"ERROR: '{root}' is {howItWasClaimed}: {firstClaim[root]} and {mountString}. Keep one.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Refuses a mount whose host-side base path does not exist, whether it was written as a
    /// <c>--mount</c> argument or declared in the settings file. Without this the missing
    /// directory surfaces as a raw <see cref="DirectoryNotFoundException"/> thrown out of the
    /// <c>FileSystemRegistry</c> DI factory — a stack trace, where the operator made a simple
    /// configuration mistake that one actionable line (and a <c>mkdir</c>) fixes.
    /// <para>
    /// Same completeness stance as <see cref="EnsureReservedRootsAreFree(IEnumerable{string}, string?, TextWriter, string[])"/>:
    /// the settings are read HERE from <paramref name="settingsPath"/>, so no caller can forget
    /// them. Probing the physical path is bootstrap work by definition — the check exists precisely
    /// because the VFS registry (and thus <c>IFileSystemService</c>) cannot be built over it yet.
    /// </para>
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="settingsPath">Resolved settings file whose declared mounts also count, or
    /// <see langword="null"/> when the command resolves none.</param>
    /// <param name="error">Where the refusal is written: the runner's standard error, or the caller's buffer.</param>
    public static bool EnsureMountSourcesExist(
        IEnumerable<string> userMounts,
        string? settingsPath,
        TextWriter error) =>
        EnsureMountSourcesExist(userMounts, settingsPath, selection: null, error);

    /// <summary>
    /// <see cref="EnsureMountSourcesExist(IEnumerable{string}, string?, TextWriter)"/>, minus the settings
    /// entries the selection withdrew for this run (VFS-90, D-10): an entry the run does not
    /// mount is not probed, so a machine whose second <c>/output</c> points at a folder that no
    /// longer exists still runs the crew that selects the first.
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="settingsPath">Resolved settings file whose declared mounts also count, or
    /// <see langword="null"/> when the command resolves none.</param>
    /// <param name="selection">The resolved plan, or <see langword="null"/> to probe every entry.</param>
    /// <param name="error">Where the refusal is written: the runner's standard error, or the caller's buffer.</param>
    public static bool EnsureMountSourcesExist(
        IEnumerable<string> userMounts,
        string? settingsPath,
        MountSelectionPlan? selection,
        TextWriter error) =>
        EnsureMountSourcesExist(userMounts, RunnerSettings.ReadMountConfiguration(settingsPath), selection, error);

    /// <summary>
    /// <see cref="EnsureMountSourcesExist(IEnumerable{string}, string?, MountSelectionPlan?, TextWriter)"/> over
    /// the layers a caller composed, the refusal written to <paramref name="error"/>.
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings — <c>--mount</c>, and the folders a command mounts itself.</param>
    /// <param name="configuration">The configuration the host is built over, before its own mounts.</param>
    /// <param name="selection">The resolved plan, or <see langword="null"/> to probe every entry.</param>
    /// <param name="error">Where the refusal is written: stderr, or the caller's buffer.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    internal static bool EnsureMountSourcesExist(
        IEnumerable<string> userMounts,
        IConfiguration configuration,
        MountSelectionPlan? selection,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(userMounts);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(error);

        var withdrawn = new HashSet<int>(selection?.WithdrawnIndices() ?? []);
        var declared = RunnerSettings.ReadDeclaredAgentFacingMounts(configuration)
            .Where(entry => !withdrawn.Contains(entry.Index))
            .Select(entry => entry.Spec)
            .Concat(RunnerSettings.ReadDeclaredInternalMounts(configuration));

        foreach (var mountString in userMounts.Concat(declared))
        {
            FileSystemMount mount;
            try
            {
                mount = FileSystemMount.Parse(mountString);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // Malformed strings are reported by the mount parser at host build time,
                // with its own precise message. Not this guard's.
                continue;
            }

            if (Directory.Exists(mount.BasePath))
                continue;

            error.WriteLine(
                $"ERROR: mount source directory does not exist: {mount.BasePath}. "
                + "Create it first (mkdir -p) or point the mount at an existing directory.");
            error.WriteLine($"       mount       : {mountString}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Runs a one-shot crew kickoff end-to-end. Handles settings resolution,
    /// AutoSummaryWriter registration (when an <c>/output:rw</c> mount is declared),
    /// SIGTERM/SIGINT graceful cancellation, and standard exit codes.
    /// </summary>
    /// <param name="opts">Parsed runner options.</param>
    /// <param name="loggerCategory">Logger category used for runner-level log messages.</param>
    /// <param name="configureServices">Optional hook to register runner-specific services.</param>
    /// <param name="externalCt">
    /// Optional external cancellation token. When the caller is itself running inside
    /// another runner (e.g. <c>VerifyCommand</c> inside the TUI's REPL loop), pass the
    /// command's <see cref="CancellationToken"/> here so the inner CTS is linked to it.
    /// Without this, Ctrl+C inside the TUI cannot reach the inner crew (TUI mode disables
    /// the runtime SIGINT path that <c>RegisterGracefulShutdown</c> relies on).
    /// </param>
    /// <returns>Exit code: 0 = success, 1 = config error, 2 = crew failure, 130 = canceled.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static Task<int> RunOneShotAsync(
        RunnerOptionsBase opts,
        string loggerCategory,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null,
        CancellationToken externalCt = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return RunOneShotCoreAsync();

        async Task<int> RunOneShotCoreAsync()
        {
        // Diagnostic modes short-circuit the kickoff: --list-tools dumps the runtime tool
        // registry (no crew), --validate loads the crew strictly but never probes the LLM.
        if (opts.ListTools)
            return await RunListToolsAsync(opts, loggerCategory, configureServices).ConfigureAwait(false);
        if (opts.Validate)
            return await RunValidateAsync(opts, loggerCategory, configureServices, externalCt).ConfigureAwait(false);

        if (!TryBuildHost(opts, loggerCategory, configureServices, out var bootstrap, out var errorCode))
            return errorCode;

        // The failure line is written AFTER the host is disposed: disposing flushes the
        // console logger's queue, so "ERROR: <message>" is the last thing on stderr — the
        // sentence exit code 2 promises ("the last stderr lines say why") — rather than a
        // line the logger's own output then buries (STUDIO-15 D-03).
        var (exitCode, failure) = await RunWithHostAsync(bootstrap!, opts, externalCt).ConfigureAwait(false);
        if (failure is not null)
            await opts.RunnerConsole.ErrorOutput.WriteLineAsync($"ERROR: {failure}").ConfigureAwait(false);
        return exitCode;
        }
    }

    /// <summary>
    /// The exit-code fault barrier around one crew run: cancellation is 130, an unreachable
    /// endpoint and any other failure are 2, with the reason handed back for the caller to
    /// write once the host is disposed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level fault barrier: any crew execution failure is logged and converted to exit code 2 so the runner exits cleanly instead of crashing. Cancellation maps to exit 130.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static async Task<(int ExitCode, string? Failure)> RunWithHostAsync(
        HostBootstrap bootstrap, RunnerOptionsBase opts, CancellationToken externalCt)
    {
        // `using`, because the sandbox session directory is deleted by SandboxSession.Dispose,
        // which the container runs on host disposal. Without it every `orkeon run` left a
        // directory under the ephemeral root for a later process's janitor to collect — and
        // the diagnostics flows next door already dispose theirs.
        using var host = bootstrap.Host;
        var logger = bootstrap.Logger;

        // Internal CTS linked to the external one (if any). Cancelling either path stops
        // the crew: SIGINT/SIGTERM via RegisterGracefulShutdown, OR caller's externalCt.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var error = opts.RunnerConsole.ErrorOutput;
        using var shutdown = RegisterGracefulShutdown(cts, error);

        try
        {
            return await KickoffLoadedCrewAsync(host, bootstrap, opts, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogCrewExecutionCanceled(logger);
            return (130, null);
        }
        catch (Exception ex) when (IsConnectionRefused(ex))
        {
            // Safety net for the case the pre-kickoff probe let through (e.g. the endpoint died
            // between the probe and kickoff): the raw failure is a "Connection refused"
            // SocketException buried under HTTP retries — surface the actionable one-liner
            // instead of the cryptic stack trace.
            var endpoint = ResolveConfiguredLlmEndpoint(host);
            await error.WriteLineAsync(BuildUnreachableLlmMessage(endpoint)).ConfigureAwait(false);
            LogLlmEndpointUnreachable(logger, endpoint ?? "(default)");
            return (2, null);
        }
        catch (Exception ex)
        {
            // The message is the diagnostic; the type and the stack are debugging noise that
            // used to be the whole Error line — one line of ~3000 characters whose only
            // actionable words were the first ten. They stay one opt-in away: --verbose 2
            // (the Debug entry below) or ORKEON_DEBUG=1, the switch the crew-load path and
            // the scripting CLI's fault barrier already honour.
            LogCrewExecutionFailed(logger, ex.Message);
            LogCrewExecutionFailureDetail(logger, ex);
            if (RunnerEnvironment.DebugDiagnostics)
                await error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return (2, ex.Message);
        }
    }

    /// <summary>
    /// The kickoff sequence itself: log the mounts, load the crew, parse the CLI input, probe
    /// the LLM endpoint, run the crew and print its output. Split out of
    /// <see cref="RunOneShotAsync"/> so that method reads as the exit-code fault barrier it is
    /// and this one as the ordered sequence it runs; every failure it does not answer for
    /// itself propagates to that barrier.
    /// </summary>
    /// <returns>
    /// Exit code — 0 = success, 1 = malformed <c>--var</c>, 2 = crew load, LLM or crew failure —
    /// and, for a crew that ran and did not succeed, the reason the caller writes as the last
    /// stderr line once the host is disposed.
    /// </returns>
    private static async Task<(int ExitCode, string? Failure)> KickoffLoadedCrewAsync(
        IHost host,
        HostBootstrap bootstrap,
        RunnerOptionsBase opts,
        CancellationToken ct)
    {
        var logger = bootstrap.Logger;
        var console = opts.RunnerConsole;
        RunnerLogging.LogMounts(bootstrap.CliMounts, logger);

        LogLoadingCrew(logger, bootstrap.ConfigPath);

        // The MCP tools must be in the registry before the crew resolves its tools (STUDIO-21):
        // a server that fails costs one error line and the run goes on, so a crew naming one
        // of its tools fails at load, under StrictTools, with the usual "unknown tool" line.
        await McpStartup.ConnectConfiguredServersAsync(host, ct).ConfigureAwait(false);

        // One scope for the load and the kickoff, as orkeon-host's CrewRunner opens per run
        // (GAP-25): the crew factory, the orchestrator and the repositories the crew is loaded
        // into are scoped, and resolving them from the root kept them for the life of the
        // process — or failed outright on a host validating scopes.
        var scope = host.Services.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();

        Domain.Crew.Crew crew;
        try
        {
            crew = await LoadCrewAsync(
                host, factory, scope.ServiceProvider, bootstrap.VirtualConfigPath, logger, ct, bootstrap.IsCrewDirectory).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !IsConnectionRefused(ex))
        {
            // A crew that does not load is a configuration mistake — report it the way
            // --validate does (one actionable line, stack behind -v / ORKEON_DEBUG=1)
            // rather than letting the generic fault barrier above dump the raw exception.
            // A scripted crew can reach the LLM while loading, though: that failure is not
            // a configuration mistake, so it is left to the unreachable-endpoint handler.
            await console.ErrorOutput.WriteLineAsync($"ERROR: {ex.Message}").ConfigureAwait(false);
            await ReportCrewConfigurationErrorAsync(logger, ex, opts.Verbose, console.ErrorOutput).ConfigureAwait(false);
            return (2, null);
        }

        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();

        var input = await TryParseCrewInputAsync(opts).ConfigureAwait(false);
        if (input is null)
            return (1, null);

        if (!await EnsureLlmEndpointReachableAsync(host, logger, console.ErrorOutput, ct).ConfigureAwait(false))
            return (2, null);

        LogKickingOffCrew(logger, crew.Goal);
        var output = await orchestrator.KickoffAsync(crew.Id, input, ct).ConfigureAwait(false);

        PrintCrewOutput(output, opts.MachineReadableStdout ? console.ErrorOutput : console.Output);
        if (!output.Succeeded)
        {
            // The crew ran and failed: exit 2, the reason handed back so the caller writes it
            // as the last stderr line once the host — and its console logger queue — is
            // disposed. KickoffAsync never throws, so this exit code was 0 for every crew
            // failure: an empty deliverable went green (STUDIO-12 C5a).
            return (2, output.Error ?? output.FinalOutput);
        }

        return (0, null);
    }

    /// <summary>
    /// Builds the <see cref="CrewInput"/> for a one-shot kickoff from the parsed CLI variables
    /// and initial context. Returns <see langword="null"/> (after printing the diagnostic) when
    /// a <c>--var</c> value is malformed.
    /// </summary>
    private static async Task<CrewInput?> TryParseCrewInputAsync(RunnerOptionsBase opts)
    {
        try
        {
            var vars = opts.ParseVariables();
            return vars.Count > 0 || !string.IsNullOrEmpty(opts.InitialContext)
                ? CrewInput.WithStringVariables(opts.InitialContext, vars)
                : CrewInput.Empty();
        }
        catch (FormatException ex)
        {
            await opts.RunnerConsole.ErrorOutput.WriteLineAsync($"ERROR: {ex.Message}").ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Pre-kickoff reachability probe. LLM providers wrap connection failures in sanitized
    /// exceptions behind Polly retries, so a dead endpoint otherwise yields an empty crew
    /// output at exit 0 with no hint — the connection-refused catch in the caller never sees it.
    /// A short TCP probe turns "nothing is listening" into a fast, explicit failure. Only an
    /// active refusal returns <see langword="false"/>; ambiguous results (no URL, DNS, TLS,
    /// slow-link timeout) pass so a legitimate run is never blocked by the probe itself.
    /// </summary>
    private static async Task<bool> EnsureLlmEndpointReachableAsync(
        IHost host, ILogger logger, TextWriter error, CancellationToken ct)
    {
        var probeEndpoint = ResolveConfiguredLlmEndpoint(host);
        if (probeEndpoint is null
            || await IsLlmEndpointReachableAsync(probeEndpoint, TimeSpan.FromSeconds(2), ct)
                .ConfigureAwait(false))
        {
            return true;
        }

        await error.WriteLineAsync(BuildUnreachableLlmMessage(probeEndpoint)).ConfigureAwait(false);
        LogLlmEndpointUnreachable(logger, probeEndpoint);
        return false;
    }

    /// <summary>
    /// Renders the one-shot crew result (final output, duration, token usage) to the console.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static void PrintCrewOutput(CrewOutput output, TextWriter destination)
    {
        // On an --events run the destination is stderr: stdout is the protocol, and this
        // banner between two JSONL documents was the one non-envelope thing it still carried.
        destination.WriteLine();
        destination.WriteLine("=== Crew Output ===");
        destination.WriteLine(output.FinalOutput);
        destination.WriteLine();
        destination.WriteLine($"Duration: {output.Duration}");
        destination.WriteLine(output.TokensUsed is { } usage
            ? $"Tokens used: {usage.TotalTokens}"
            : "Tokens used: (not measured)");
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ex"/> or any inner exception is a
    /// "connection refused" socket failure — the signature of an LLM endpoint that is configured
    /// but not listening (e.g. Docker Model Runner is not running).
    /// </summary>
    private static bool IsConnectionRefused(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.Net.Sockets.SocketException
                { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the actionable one-line diagnostic shown when the configured LLM endpoint cannot be
    /// reached, pointing at the example profiles, <c>--settings</c>, and the getting-started doc.
    /// </summary>
    private static string BuildUnreachableLlmMessage(string? endpoint)
    {
        var target = endpoint is null ? "the configured LLM endpoint" : $"endpoint {endpoint}";
        return $"ERROR: No reachable LLM endpoint ({target} refused the connection). " +
            "Copy an example profile (examples/appsettings/*.example) and pass it with " +
            "--settings, or start Docker Model Runner. " +
            "See docs/getting-started/run-your-first-example.md.";
    }

    /// <summary>
    /// Probes whether the configured LLM endpoint accepts a TCP connection. Returns
    /// <see langword="false"/> ONLY when the host actively refuses the connection (a fast, reliable
    /// "nothing is listening" signal). Every ambiguous outcome — no/blank URL, unparseable URL, DNS
    /// failure, TLS/other socket error, or a probe timeout on a slow link — returns
    /// <see langword="true"/> so a legitimate run is never blocked by the probe itself.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Probe is intentionally best-effort: any non-refusal outcome (timeout, DNS, TLS, unexpected) is treated as inconclusive and must never block a legitimate run.")]
    internal static async System.Threading.Tasks.Task<bool> IsLlmEndpointReachableAsync(
        string? baseUrl, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return true;

        var defaultPort = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? 443
            : 80;
        var port = uri.Port > 0 ? uri.Port : defaultPort;

        try
        {
            using var probe = new System.Net.Sockets.TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            await probe.ConnectAsync(uri.Host, port, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Only an active refusal is conclusive; other socket errors are inconclusive.
            return ex.SocketErrorCode != System.Net.Sockets.SocketError.ConnectionRefused;
        }
        catch (Exception)
        {
            // Timeout (OperationCanceledException), DNS, etc. — inconclusive; do not block.
            return true;
        }
    }

    /// <summary>
    /// Reads the configured LLM base URL (<c>Llm:BaseUrl</c>) for use in diagnostics, or
    /// <see langword="null"/> when no configuration is available.
    /// </summary>
    private static string? ResolveConfiguredLlmEndpoint(IHost host)
    {
        var url = host.Services.GetService<IConfiguration>()?["Llm:BaseUrl"];
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    /// <summary>
    /// Runs an interactive question/answer loop. Reuses the same settings/host/mount
    /// auto-injection logic as <see cref="RunOneShotAsync"/>: SIGTERM aborts the loop
    /// (exit 130), per-question SIGINT cancels only the current kickoff, and a stop
    /// word terminates the loop with exit 0. The kickoff body is delegated to
    /// <paramref name="kickoffPerInputAsync"/> so callers control crew lookup and
    /// CrewInput shape.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static Task<int> RunInteractiveLoopAsync(
        RunnerOptionsBase opts,
        string loggerCategory,
        IReadOnlySet<string> stopWords,
        Func<IServiceProvider, string, CancellationToken, Task<CrewOutput>> kickoffPerInputAsync,
        Action<int> onSessionStart,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(stopWords);
        ArgumentNullException.ThrowIfNull(kickoffPerInputAsync);
        ArgumentNullException.ThrowIfNull(onSessionStart);
        return RunInteractiveLoopCoreAsync();

        async Task<int> RunInteractiveLoopCoreAsync()
        {
            // Diagnostic modes are shared with the one-shot flow so every runner honours them.
            if (opts.ListTools)
                return await RunListToolsAsync(opts, loggerCategory, configureServices).ConfigureAwait(false);
            if (opts.Validate)
                return await RunValidateAsync(opts, loggerCategory, configureServices, CancellationToken.None).ConfigureAwait(false);

            if (!TryBuildHost(opts, loggerCategory, configureServices, out var bootstrap, out var errorCode))
                return errorCode;

            using var host = bootstrap!.Host;
            var (logger, cliMounts) = (bootstrap.Logger, bootstrap.CliMounts);

            var console = opts.RunnerConsole;
            using var sessionCts = new CancellationTokenSource();
            using var shutdown = RegisterGracefulShutdown(sessionCts, console.ErrorOutput);

            RunnerLogging.LogMounts(cliMounts, logger);

            // Shared handle to the in-flight kickoff's CTS, read by the SIGINT handler and
            // written by the per-question runner. A StrongBox lets both the handler closure
            // and the extracted runner method target the same volatile slot.
            var currentKickoff = new StrongBox<CancellationTokenSource?>(null);
            using var perQuestion = console.OnInterrupt(CreatePerQuestionCancelHandler(currentKickoff, console.ErrorOutput));

            onSessionStart(0);
            return await RunQuestionLoopAsync(
                host, console, sessionCts, currentKickoff, stopWords, kickoffPerInputAsync, logger).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the SIGINT handler for the interactive loop: a per-kickoff cancel that cancels only
    /// the in-flight question (via <paramref name="currentKickoff"/>) and leaves the loop running.
    /// SIGINT outside a kickoff is handled by <see cref="RegisterGracefulShutdown"/> instead.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static Func<bool> CreatePerQuestionCancelHandler(
        StrongBox<CancellationTokenSource?> currentKickoff, TextWriter error)
    {
        return () =>
        {
            var cts = Volatile.Read(ref currentKickoff.Value);
            if (cts is null || cts.IsCancellationRequested) return false;
            cts.Cancel();
            error.WriteLine(
                "\n[runner] Canceling current question — type a new question or a stop word to exit.");
            return true;
        };
    }

    /// <summary>
    /// Reads and dispatches interactive questions until end-of-input, a stop word, or session
    /// cancellation. Returns the loop exit code: 0 on stop word / EOF, 130 when the session was
    /// canceled, or a non-null code propagated from a question run.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static async Task<int> RunQuestionLoopAsync(
        IHost host,
        IRunnerConsole console,
        CancellationTokenSource sessionCts,
        StrongBox<CancellationTokenSource?> currentKickoff,
        IReadOnlySet<string> stopWords,
        Func<IServiceProvider, string, CancellationToken, Task<CrewOutput>> kickoffPerInputAsync,
        ILogger logger)
    {
        var questionNumber = 0;
        var session = new QuestionSession(sessionCts, kickoffPerInputAsync, logger);
        while (!sessionCts.IsCancellationRequested)
        {
            var input = ReadQuestion(console);
            if (input is null) break;
            if (string.IsNullOrWhiteSpace(input))
            {
                await console.Output.WriteLineAsync("  (empty input — type a question or a stop word to exit)").ConfigureAwait(false);
                continue;
            }
            if (IsStopWord(stopWords, input))
            {
                await console.Output.WriteLineAsync().ConfigureAwait(false);
                await console.Output.WriteLineAsync($"Session ended. {questionNumber} question(s) answered.").ConfigureAwait(false);
                return 0;
            }

            questionNumber++;
            LogQuestion(logger, questionNumber, input);

            var earlyExit = await RunScopedQuestionAsync(
                host, console.Output, input, questionNumber, session, currentKickoff)
                .ConfigureAwait(false);
            if (earlyExit is { } code)
                return code;
        }

        return sessionCts.IsCancellationRequested ? 130 : 0;
    }

    /// <summary>
    /// Runs a single interactive question inside its own DI scope and linked kickoff CTS,
    /// publishing that CTS to <paramref name="currentKickoff"/> for the duration so the SIGINT
    /// handler can cancel just this question. Returns the same <c>int?</c> contract as
    /// <see cref="RunSingleQuestionAsync"/>: non-null only when the loop must terminate.
    /// </summary>
    private static async Task<int?> RunScopedQuestionAsync(
        IHost host,
        TextWriter output,
        string input,
        int questionNumber,
        QuestionSession session,
        StrongBox<CancellationTokenSource?> currentKickoff)
    {
        using var scope = host.Services.CreateScope();
        using var kickoffCts = CancellationTokenSource.CreateLinkedTokenSource(session.SessionCts.Token);
        Volatile.Write(ref currentKickoff.Value, kickoffCts);

        try
        {
            return await RunSingleQuestionAsync(
                scope.ServiceProvider, output, input, questionNumber, session, kickoffCts.Token).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref currentKickoff.Value, null);
        }
    }

    /// <summary>
    /// Prompts for and reads a single interactive question line, returning the trimmed input
    /// or <c>null</c> when end-of-input is reached (Ctrl+D / closed stdin).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static string? ReadQuestion(IRunnerConsole console)
    {
        console.Output.Write("\n[Question] > ");
        console.Output.Flush();
        return console.Input.ReadLine()?.Trim();
    }

    private static bool IsStopWord(IReadOnlySet<string> stopWords, string input)
    {
#pragma warning disable CA1308 // matched against a caller-provided set whose comparer/casing is unknown here; lowercasing preserves the existing lookup semantics
        return stopWords.Contains(input.ToLowerInvariant());
#pragma warning restore CA1308
    }

    /// <summary>
    /// Runs one kickoff for an interactive question and renders the answer. Returns a non-null
    /// exit code only when the loop must terminate (session canceled → 130); returns <c>null</c>
    /// to continue with the next question (success, per-question cancel, or per-question error).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-question fault barrier: any kickoff failure is logged and reported, then the loop continues so one failed question cannot terminate the interactive session. Cancellation is handled by dedicated catches.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static async Task<int?> RunSingleQuestionAsync(
        IServiceProvider services,
        TextWriter console,
        string input,
        int questionNumber,
        QuestionSession session,
        CancellationToken kickoffToken)
    {
        var (sessionCts, kickoffPerInputAsync, logger) = session;
        try
        {
            await console.WriteLineAsync("  Processing...").ConfigureAwait(false);

            var output = await kickoffPerInputAsync(services, input, kickoffToken).ConfigureAwait(false);

            await console.WriteLineAsync("\n[Answer]").ConfigureAwait(false);
            await console.WriteLineAsync(output.FinalOutput).ConfigureAwait(false);

            await console.WriteLineAsync(output.TokensUsed is { } usage
                ? $"  ({output.Duration.TotalSeconds:F1}s — {usage.TotalTokens} tokens)"
                : $"  ({output.Duration.TotalSeconds:F1}s — tokens not measured)").ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
        {
            LogSessionCanceled(logger, questionNumber);
            return 130;
        }
        catch (OperationCanceledException)
        {
            await console.WriteLineAsync("  Question canceled.").ConfigureAwait(false);
            LogQuestionCanceled(logger, questionNumber);
            return null;
        }
        catch (Exception ex)
        {
            await console.WriteLineAsync($"\n  Error: {ex.Message}").ConfigureAwait(false);
            LogQuestionFailed(logger, ex, questionNumber);
            return null;
        }
    }

    /// <summary>What every question of an interactive session shares.</summary>
    /// <param name="SessionCts">The session's source: its cancellation ends the loop.</param>
    /// <param name="KickoffPerInputAsync">Runs one kickoff for a question.</param>
    /// <param name="Logger">The runner's logger.</param>
    private sealed record QuestionSession(
        CancellationTokenSource SessionCts,
        Func<IServiceProvider, string, CancellationToken, Task<CrewOutput>> KickoffPerInputAsync,
        ILogger Logger);

    /// <summary>
    /// Registers SIGTERM + SIGINT handlers that cancel the provided token source. Returns
    /// a disposable that unregisters both handlers when disposed.
    /// </summary>
    /// <param name="cts">The source a signal cancels.</param>
    /// <param name="error">Where the signal is announced: the runner's standard error.</param>
    public static IDisposable RegisterGracefulShutdown(CancellationTokenSource cts, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(cts);
        ArgumentNullException.ThrowIfNull(error);
        var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            error.WriteLine("[runner] SIGTERM received — canceling crew for graceful shutdown...");
            ctx.Cancel = true;
            cts.Cancel();
        });
        var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
        {
            error.WriteLine("[runner] SIGINT received — canceling crew for graceful shutdown...");
            ctx.Cancel = true;
            cts.Cancel();
        });
        return new CompositeDisposable(sigterm, sigint);
    }

    /// <summary>
    /// Inspects CLI mount strings and returns the virtual path of the first writable mount
    /// whose virtual root starts with <c>/output</c>, or <c>null</c> if none is found.
    /// </summary>
    public static string? DetectOutputMountPath(IEnumerable<string> cliMounts)
    {
        ArgumentNullException.ThrowIfNull(cliMounts);
        foreach (var raw in cliMounts)
        {
            try
            {
                var mount = FileSystemMount.Parse(raw);
                if (mount.VirtualPath.StartsWith("/output", StringComparison.OrdinalIgnoreCase)
                    && (mount.DefaultRights & FileAccessRights.Write) != 0)
                {
                    return mount.VirtualPath.TrimEnd('/');
                }
            }
            catch (FormatException)
            {
                // Ignore malformed mount strings.
            }
        }
        return null;
    }

    /// <summary>
    /// Applies a verbosity preset shared by all runners.
    /// 1 = warnings only except Orkeon Hosting/Application/Tools/Infrastructure at Information.
    /// 2 = Debug everywhere, Infrastructure capped at Information.
    /// </summary>
    public static void ConfigureVerboseLogging(ILoggingBuilder b, int verbosity)
        => ConfigureVerboseLogging(b, verbosity, null);

    /// <summary>
    /// As <see cref="ConfigureVerboseLogging(ILoggingBuilder, int)"/>, leaving a <c>Logging:LogLevel</c>
    /// rule of <paramref name="configuration"/> that names <c>Orkeon.Verbose</c> or <c>Default</c> to win.
    /// </summary>
    public static void ConfigureVerboseLogging(ILoggingBuilder b, int verbosity, IConfiguration? configuration)
    {
        // If an outer host (typically a TUI) advertised an AmbientLoggerProvider, route
        // logs there INSTEAD of the standard Console provider — otherwise SimpleConsole
        // would write to System.Console.Out (which an outer TUI has commandeered) and
        // pollute the alt-screen / leak as a dump on shutdown.
        // Resolved by reflection so this shared helper doesn't take a hard dependency
        // on Orkeon.Cli.Abstractions (avoid coupling examples runners to CLI projects).
        var ambient = TryResolveAmbientLoggerProvider();
        if (ambient is not null)
        {
            b.ClearProviders();
            b.AddProvider(ambient);
        }
        else
        {
            b.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss.fff ";
                options.SingleLine = true;
            });
        }

        switch (verbosity)
        {
            case 1:
                b.SetMinimumLevel(LogLevel.Warning);
                // The host's own decisions — which LLM endpoint was resolved, which settings
                // mount a --mount replaced — are what an operator reading a -v 1 log needs
                // first; Studio launches at this level.
                b.AddFilter("Orkeon.Hosting", LogLevel.Information);
                b.AddFilter("Orkeon.Application.Crew.ExecutionOrchestrator", LogLevel.Information);
                b.AddFilter("Orkeon.Application.Agent", LogLevel.Information);
                b.AddFilter("Orkeon.Tools", LogLevel.Information);
                b.AddFilter("Orkeon.Infrastructure.Crew", LogLevel.Information);
                b.AddFilter("Orkeon.Infrastructure.Orchestration", LogLevel.Information);
                b.AddFilter("Orkeon.Infrastructure.Logging", LogLevel.Information);
                AddVerboseCategoryRule(b, configuration);
                break;
            default:
                b.SetMinimumLevel(LogLevel.Debug);
                b.AddFilter("Orkeon.Infrastructure", LogLevel.Information);
                break;
        }
    }

    /// <summary>
    /// Shows the agents' turn lines (category <c>Orkeon.Verbose</c>) at Information. The rule is added
    /// only when the configuration's <c>Logging:LogLevel</c> names neither that category nor
    /// <c>Default</c>: the most specific rule wins, so the user's is kept by the runner's absence.
    /// </summary>
    internal static void AddVerboseCategoryRule(ILoggingBuilder b, IConfiguration? configuration)
    {
        var levels = configuration?.GetSection("Logging:LogLevel");
        if (levels is not null
            && (levels[ExecutionLogCategories.Verbose] is not null || levels["Default"] is not null))
        {
            return;
        }

        b.AddFilter(ExecutionLogCategories.Verbose, LogLevel.Information);
    }

    /// <summary>
    /// True when <paramref name="configPath"/> looks like a <c>.ork.ts</c> Orkeon Scripting
    /// DSL source rather than a YAML crew definition. The runner dispatches on this so the
    /// same <c>-c/--config</c> CLI flag accepts both formats.
    /// </summary>
    public static bool IsScriptedCrewDefinition(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        // Accept both ".ork.ts" (the conventional extension shipped in examples) and the
        // shorter ".ork.js" for already-transpiled fixtures used in tests.
        return configPath.EndsWith(".ork.ts", StringComparison.OrdinalIgnoreCase)
            || configPath.EndsWith(".ork.js", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads a <c>.ork.ts</c> crew definition through <see cref="ScriptHost"/>, then runs
    /// it through <see cref="JsCrewConfigurationAdapter"/> + <see cref="ICrewFactory"/>
    /// so it joins the same orchestration pipeline as YAML crews (deliverable resolvers,
    /// AutoSummaryWriter, telemetry).
    /// </summary>
    private static async Task<Domain.Crew.Crew> LoadCrewFromScriptAsync(
        IHost host,
        ICrewFactory factory,
        string configPath,
        ILogger logger,
        CancellationToken ct)
    {
        var sp = host.Services;
        var fileSystem = sp.GetRequiredService<IFileSystemService>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        // The registry holds the MCP servers' tools, connected before the load: a .ork.ts crew
        // reaches them in .tools([...]), its body and act, as a declarative crew does.
        var tools = new ScriptToolCatalogue(sp.GetServices<IBaseTool>(), sp.GetService<IToolRegistry>()).ToList();
        var llmProvider = sp.GetService<ILlmProvider>();

        // Bind the configured limits rather than letting the all-optional overload apply
        // the 30 s untrusted-script default: omitting this discarded
        // Orkeon:Scripting:Limits entirely, so a .ork.ts crew was silently capped at 30 s
        // of WALL CLOCK — LLM latency included — no matter what the operator configured.
        var scriptLimits = configuration.GetSection(Orkeon.Scripting.Configuration.ScriptingLimitsOptions.SectionName)
            .Get<Orkeon.Scripting.Configuration.ScriptingLimitsOptions>();

        var engineFactory = new JsEngineFactory(
            limits: scriptLimits,
            loggerFactory: loggerFactory,
            builtInTools: tools,
            llmProvider: llmProvider,
            hostPorts: new ScriptingHostPorts
            {
                PermissionGate = sp.GetService<Orkeon.Application.Interfaces.Security.IPermissionGate>(),
                DeltaSink = sp.GetService<Orkeon.Application.Interfaces.Ports.ILlmDeltaSink>(),
                ToolInvocation = sp.GetService<Orkeon.Application.Interfaces.Security.IToolInvocationPipeline>(),
                // GAP-17: llm.profile(name) picks among the host's Llm:Profiles.
                LlmProfiles = sp.GetService<Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry>(),
                AgentMemoryStore = sp.GetService<Orkeon.Application.Interfaces.Ports.IScriptAgentMemoryStore>(),
                ToolLimits = sp.GetService<Microsoft.Extensions.Options.IOptions<Orkeon.Domain.Tools.ToolResultLimitsOptions>>()?.Value,
                AgentInstances = configuration.GetSection(Orkeon.Scripting.Configuration.ScriptAgentInstanceOptions.SectionName).Get<Orkeon.Scripting.Configuration.ScriptAgentInstanceOptions>(),
                // RT-06: a script agent's .workingRoot(...) is checked against these mounts.
                FileSystem = sp.GetService<IFileSystemService>(),
            },
            ragBackend: Orkeon.Scripting.Bindings.RagScriptingBackendFactory.Build(sp));

        // Always bundle through esbuild so relative imports + TS-only syntax in the
        // script resolve consistently across .ork.ts authors. EsbuildNotFoundException
        // surfaces upstream and is caught by RunOneShotAsync's exception handler with
        // the standard exit code 2 (crew failure); operators can pre-install the
        // toolchain via the MSBuild bootstrap target on src/scripting/Orkeon.Scripting/, or
        // point Orkeon:Scripting:Toolchain:EsbuildPath at it.
        using var transpiler = EsbuildTranspiler.Create(configuration);
        var scriptHost = new ScriptHost(
            fileSystem,
            transpiler,
            engineFactory,
            loggerFactory.CreateLogger<ScriptHost>());

        // The script is addressed virtually like everything else; esbuild is the one consumer
        // that genuinely needs a disk path (it resolves the script's relative imports itself,
        // outside the VFS). Asking the VFS to resolve it is the sanctioned way to obtain one —
        // same stance as SqliteStateStore's Data Source. A denial leaves it null, and
        // ScriptHost documents that as "imports will not resolve".
        var virtualPath = configPath;
        var resolved = fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
        var physicalPath = resolved.IsAllowed ? resolved.ResolvedPath : null;

        LogScriptedCrewDetected(logger);
        var jsCrew = await scriptHost.LoadCrewFromFileAsync(physicalPath, virtualPath, ct).ConfigureAwait(false);
        var crewConfig = JsCrewConfigurationAdapter.ToConfiguration(jsCrew);

        // The two script shapes honour opposite halves of the DSL, and this is the half
        // this one drops. Dropping it quietly is how a reader concludes .body() ran: the
        // run succeeds, the output is plausible, and nothing says the body was skipped.
        foreach (var ignored in JsCrewConfigurationAdapter.CollectIgnoredFeatures(jsCrew))
            LogDeclarativeShapeIgnores(logger, ignored);

        // Script-defined tools become first-class (EX-01): register the instances with
        // the runtime registry BEFORE the factory's strict resolution runs — the same
        // late-registration path MCP tools use. The registry refuses a name another tool
        // already holds (IToolRegistry.RegisterToolAsync returns false); for a script tool
        // that refusal ends the run loudly: silently replacing a built-in is exactly the
        // kind of surprise strict resolution exists to prevent.
        var scriptTools = JsCrewConfigurationAdapter.CollectScriptTools(jsCrew);
        if (scriptTools.Count > 0)
        {
            var registry = sp.GetRequiredService<Orkeon.Domain.Tools.IToolRegistry>();
            foreach (var tool in scriptTools)
            {
                if (!await registry.RegisterToolAsync(tool).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        $"Script tool '{tool.Name}' collides with an already-registered tool of the same name. "
                        + "Rename the script tool — shadowing a registered tool is refused.");
                }
            }
            LogRegisteredScriptTools(logger, scriptTools.Count, jsCrew.name);
        }

        LogAdaptedJsCrew(logger, jsCrew.name, crewConfig.Agents.Count, crewConfig.Tasks.Count);
        return await factory.CreateFromConfigAsync(crewConfig, ct).ConfigureAwait(false);
    }

    private static ILoggerProvider? TryResolveAmbientLoggerProvider()
    {
        // Late-bound lookup of Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider.Current.
        // Returns null if the assembly isn't loaded (no TUI active) or the property is null.
        var t = Type.GetType("Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider, Orkeon.Cli.Abstractions");
        var prop = t?.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return prop?.GetValue(null) as ILoggerProvider;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "LLM exchange logging enabled → {LogDir}/llm-exchanges-*.jsonl")]
    private static partial void LogLlmExchangeLoggingEnabled(ILogger logger, string logDir);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Loading crew from {ConfigPath}...")]
    private static partial void LogLoadingCrew(ILogger logger, string configPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Kicking off crew '{CrewGoal}'...")]
    private static partial void LogKickingOffCrew(ILogger logger, string crewGoal);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Crew execution was canceled (SIGTERM/SIGINT); hook should have written AUTO_SUMMARY.md")]
    private static partial void LogCrewExecutionCanceled(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Crew execution failed: {Reason}")]
    private static partial void LogCrewExecutionFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 15, Level = LogLevel.Debug, Message = "Crew execution failure detail")]
    private static partial void LogCrewExecutionFailureDetail(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "LLM endpoint unreachable (connection refused): {Endpoint}")]
    private static partial void LogLlmEndpointUnreachable(ILogger logger, string endpoint);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Question #{Number}: {Question}")]
    private static partial void LogQuestion(ILogger logger, int number, string question);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Session canceled (SIGTERM/SIGINT) during question #{Number}")]
    private static partial void LogSessionCanceled(ILogger logger, int number);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Question #{Number} was canceled")]
    private static partial void LogQuestionCanceled(ILogger logger, int number);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "Failed to process question #{Number}")]
    private static partial void LogQuestionFailed(ILogger logger, Exception ex, int number);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Crew definition detected as Orkeon Scripting DSL — loading via ScriptHost.")]
    private static partial void LogScriptedCrewDetected(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Adapted JsCrew '{CrewName}' to CrewConfiguration ({AgentCount} agent(s), {TaskCount} task(s)).")]
    private static partial void LogAdaptedJsCrew(ILogger logger, string crewName, int agentCount, int taskCount);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "Declarative crew script: {Detail}")]
    private static partial void LogDeclarativeShapeIgnores(ILogger logger, string detail);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Registered {Count} script-defined tool(s) from crew '{CrewName}' with the runtime tool registry.")]
    private static partial void LogRegisteredScriptTools(ILogger logger, int count, string crewName);

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;
        public CompositeDisposable(params IDisposable[] disposables) => _disposables = disposables;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a failed Dispose must not mask sibling disposals or the primary result.")]
        public void Dispose()
        {
            foreach (var d in _disposables)
            {
                try { d.Dispose(); } catch { /* best-effort */ }
            }
        }
    }
}
