using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;

namespace Orkeon.Host;

/// <summary>
/// The command line the daemon was started with, read once into the shape the startup
/// sequence needs. Parsing is deliberately separate from validating: every check below can
/// then be read — and tested — as a question about the same parsed shape.
/// </summary>
/// <param name="SettingsPath">The <c>--settings</c> file the operator named, or null.</param>
/// <param name="Mounts">The operator's own <c>--mount</c> specs, in the order given.</param>
/// <param name="WorkingDirectory">The <c>--working-dir</c> the process must move to, or null.</param>
internal sealed record HostCommandLine(
    string? SettingsPath,
    IReadOnlyList<string> Mounts,
    string? WorkingDirectory);

/// <summary>
/// The outcome of the reserved-root guard: a refusal to report, warnings to re-emit, or
/// neither. <c>RunnerExecution.EnsureReservedRootsAreFree</c> writes its own diagnostics to
/// <see cref="Console.Error"/> (it is shared with three CLI commands), so the daemon captures
/// them — the message has to reach the Windows event log too, where stderr goes nowhere.
/// </summary>
/// <param name="Error">The refusal to report before exiting 78, or null when the roots are free.</param>
/// <param name="Warnings">What the guard wrote to stderr while accepting, verbatim, or null.</param>
internal sealed record ReservedRootsOutcome(string? Error, string? Warnings);

/// <summary>
/// What the startup sequence produced: the host to run, or — its refusal already reported — the
/// exit code to leave with.
/// </summary>
/// <param name="Host">The host, built and not started; null when the configuration was refused.</param>
/// <param name="ExitCode">0 with a host; <see cref="HostConfigurationException.ExitCode"/> after a refusal.</param>
internal sealed record HostLaunch(IHost? Host, int ExitCode);

/// <summary>
/// Everything the daemon does before its host starts: what the command line says, the host it
/// builds, and every reason to refuse a configuration rather than start on it (GATE-01, GAP-35).
/// <para>
/// Each check answers with the operator-facing message instead of writing it, and
/// <see cref="Prepare"/> hands every refusal to one reporter, so <c>Program.cs</c> stays one
/// <c>Report</c> + exit 78 — and so a test can drive the whole sequence, and hold the wording,
/// without a process or a captured console.
/// </para>
/// </summary>
internal static class HostStartup
{
    private const string HelpText = """
    orkeon-host — the Orkeon service host: hosts crews as a daemon and answers chat channels
    and, when enabled, A2A peers.

    Usage:
      orkeon-host [--settings <file>] [--working-dir <dir>] [--mount <physical>:<virtual>:<ro|rw|rwnd>]... [--allow-external-mounts]

    Options:
      -s, --settings <file>     Configuration file (JSON). Defaults to ./appsettings.json,
                                resolved against the working directory; a file named here
                                replaces it.
      -m, --mount <spec>        Additional VFS mount, '<physical>:<virtual>:<rights>'. All three
                                segments are required; rights are ro, rw or rwnd. The virtual
                                path starts with '/' — a physical path is never a virtual path.
                                Quote a segment containing ':' or ';': "C:\src":/workspace:ro.
                                Hosted crew directories are mounted automatically, read-only.
          --working-dir <dir>   Directory to move to before anything is read. Relative settings
                                and crew paths resolve against it. A Windows service starts in
                                System32 — this flag is how it leaves it.
          --allow-external-mounts
                                Allow mounts outside the working directory.
      -h, --help                Show this help and exit.
          --version             Show the version and exit.

    Configuration lives under Orkeon:Host (crews, RunTimeout, ShutdownGracePeriod),
    Orkeon:Host:Discord and Orkeon:Host:A2A. Secrets are named by environment variable,
    never written in files.
    Documentation: docs/architecture/service-host.md
    """;

    /// <summary>
    /// What <c>--help</c> or <c>--version</c> asks for, or null when the daemon must run.
    /// <para>
    /// Before this existed, <c>orkeon-host --help</c> silently ignored the flag and started the
    /// daemon — the least helpful possible reading of a question, from a binary whose whole
    /// documentation says "run it in a terminal first".
    /// </para>
    /// </summary>
    public static string? InformationalText(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
            return HelpText;

        if (!args.Contains("--version", StringComparer.Ordinal))
            return null;

        var version = typeof(OrkeonHostOptions).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            is [System.Reflection.AssemblyInformationalVersionAttribute info, ..] ? info.InformationalVersion : "unknown";

        return $"orkeon-host {version}";
    }

    /// <summary>
    /// The whole sequence that precedes the start, under one barrier (GAP-35): the command line,
    /// the configuration the daemon boots from, the mounts, the runner host and the daemon's own
    /// sections — <c>Orkeon:Host</c>, its <c>A2A</c> and <c>Discord</c> —, read once, here. Every
    /// refusal is reported, and answered with exit 78, which the systemd unit excludes from its
    /// restarts. The runner host used to be built outside the barrier: a typo crashed the daemon,
    /// restarted every ten seconds; and a <c>Discord</c> value the binder could not convert crashed
    /// it later still, when the host built its services.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="report">Where a refusal is reported: the startup failure reporter, or a test's sink.</param>
    /// <param name="configureBuilder">What the binary adds to the host builder: the supervisors' lifetimes.</param>
    /// <returns>The host, built and not started, or the exit code of a refusal already reported.</returns>
    public static HostLaunch Prepare(string[] args, Action<string> report, Action<IHostBuilder>? configureBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(report);

        var command = Read(args);

        // Flags that need a value have one, --working-dir is applied (before anything else touches the
        // disk) and the settings file the operator named really exists.
        if (ValidateArguments(args, command) is { } argumentError)
            return Refused(report, argumentError);

        // The one settings file the daemon reads — the --settings file, else ./appsettings.json —,
        // resolved once --working-dir has moved the process: the boot configuration, the reserved-root
        // guard and the host read that file and no other (GAP-36). The default .NET host laid
        // ./appsettings.json under the file --settings named, and the guard saw neither.
        var settingsPath = StartupProbes.ResolveSettingsPath(command.SettingsPath);

        // Each hosted crew's directory is mounted read-only under a NAME — /crews, /crews-1, …
        // (ADR-008), never identity-mapped: the loader reads through the VFS, and a crew path that
        // only exists on the physical disk would pass the startup probe and then fail on every
        // message. The crew definitions are the host's primary input — declared by the operator in
        // the configuration — so their mounts do not require the external-mounts opt-in any more
        // than the CLI's own config directory does.
        HostCrewMountPlan crewPlan;
        try
        {
            var bootConfiguration = StartupProbes.BuildBootConfiguration(settingsPath);
            crewPlan = HostCrewMounts.For(ReadSection<OrkeonHostOptions>(bootConfiguration, OrkeonHostOptions.SectionName).Crews);
        }
        catch (HostConfigurationException ex)
        {
            return Refused(report, $"orkeon-host: {ex.Message}");
        }

        // A malformed operator --mount is a configuration error, refused here rather than at the
        // first message.
        if (ValidateMounts(command.Mounts) is { } mountError)
            return Refused(report, mountError);

        // An operator --mount claiming a root the daemon needs for its own crews would otherwise
        // surface as a raw "Duplicate virtual paths" exception thrown out of a DI factory (ADR-008,
        // decision 5). /sandbox is in this list because AddOrkeonFileSystem mounts it unconditionally,
        // in every host — so the guard has to run even when the daemon hosts no crew of its own.
        // settingsPath, not just --mount: the daemon is the most settings-driven entry point in the
        // repo and was the one the guard could not see, so a /crews claimed in appsettings.json met the
        // host's own crew mount and came back as "Duplicate virtual paths" out of a DI factory.
        var reservedRoots = CheckReservedRoots(
            command.Mounts, settingsPath, [.. crewPlan.Roots, RunnerVirtualRoots.Sandbox]);
        if (reservedRoots.Error is { } reservedRootsError)
            return Refused(report, reservedRootsError);

        // Accepted, but the guard still had something to say: re-emitted so the terminal and journald
        // contracts stay byte-identical to the CLI's.
        if (reservedRoots.Warnings is { } reservedRootsWarnings)
            Console.Error.Write(reservedRootsWarnings);

        var mounts = new List<string>(command.Mounts);
        mounts.AddRange(crewPlan.Mounts);

        try
        {
            var host = RunnerHost.Build(
                settingsPath,
                new RunnerMountPlan
                {
                    CliMounts = mounts,
                    AllowExternalMounts = crewPlan.Mounts.Count > 0 || args.Contains("--allow-external-mounts", StringComparer.Ordinal),
                },
                configureLogging: null,
                // Everything the daemon adds to the runner host lives in HostServiceRegistration, so a
                // test builds the very host this binary runs — its sections read there, once.
                configureServices: (context, services) => services.AddHostServices(context.Configuration, crewPlan),
                configureBuilder: configureBuilder);
            return new HostLaunch(host, 0);
        }
        catch (RunnerSettingsException ex)
        {
            return Refused(report, $"orkeon-host: {ex.Message}");
        }
        catch (HostConfigurationException ex)
        {
            return Refused(report, $"orkeon-host: {ex.Message}");
        }
    }

    /// <summary>
    /// Binds one of the daemon's sections. A value the binder cannot convert — a
    /// <c>RunTimeout</c> of <c>"abc"</c> — is a refused configuration naming its key (exit 78),
    /// not an exception escaping the start (GAP-35). Only binding happens here, so only the
    /// binder's refusal is converted.
    /// </summary>
    /// <exception cref="HostConfigurationException">A value of the section cannot be converted.</exception>
    internal static T ReadSection<T>(IConfiguration configuration, string sectionName)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            return configuration.GetSection(sectionName).Get<T>() ?? new T();
        }
        catch (InvalidOperationException ex)
        {
            throw new HostConfigurationException(ex.Message, ex);
        }
    }

    private static HostLaunch Refused(Action<string> report, string refusal)
    {
        report(refusal);
        return new HostLaunch(null, HostConfigurationException.ExitCode);
    }

    /// <summary>Reads the flags the daemon understands out of the raw argument array.</summary>
    public static HostCommandLine Read(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new HostCommandLine(
            SettingsPath: ArgumentValue(args, "--settings") ?? ArgumentValue(args, "-s"),
            Mounts: [.. ArgumentValues(args, "--mount"), .. ArgumentValues(args, "-m")],
            WorkingDirectory: ArgumentValue(args, "--working-dir"));
    }

    /// <summary>
    /// Everything that must be true before a byte of configuration is read, in the order it
    /// matters: <c>--working-dir</c> is applied FIRST because the settings probe, the boot
    /// configuration and the host itself all resolve relative paths against the current
    /// directory — and a Windows service starts in System32. Applied unconditionally, not only
    /// under the SCM: a terminal <c>--working-dir</c> must mean the same thing, and that is
    /// also what makes the behavior testable off Windows.
    /// <para>
    /// A <c>--settings</c> with no value used to become a silent null, and a typo'd path was
    /// silently ignored by the host builder — the daemon then started with zero crews and the
    /// operator got "nothing configured" instead of "your file is not where you said". Both
    /// are configuration errors, refused before anything starts.
    /// </para>
    /// <para>
    /// OUT-OF-SCOPE: probing the operator-supplied settings path happens on the physical disk;
    /// this bootstrap runs before the VFS exists (see <see cref="StartupProbes"/>).
    /// </para>
    /// </summary>
    /// <returns>The operator-facing refusal, or null when the daemon may carry on.</returns>
    public static string? ValidateArguments(string[] args, HostCommandLine command)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(command);

        if (IsLastArgument(args, "--working-dir"))
            return "orkeon-host: --working-dir requires a path.";

        if (command.WorkingDirectory is not null
            && StartupProbes.TryApplyWorkingDirectory(command.WorkingDirectory) is { } workingDirError)
        {
            return $"orkeon-host: {workingDirError}";
        }

        if (IsLastArgument(args, "--settings") || IsLastArgument(args, "-s"))
            return "orkeon-host: --settings requires a path.";

        if (command.SettingsPath is not null && !StartupProbes.SettingsFileExists(command.SettingsPath))
            return $"orkeon-host: settings file not found: {command.SettingsPath}";

        return null;
    }

    /// <summary>
    /// Refuses a malformed operator <c>--mount</c> here rather than at the first message. The
    /// mount registry is built lazily inside DI, so an unparseable spec used to let the daemon
    /// log READY, pass its health check, and then fail every crew run it was started for — the
    /// one failure mode a service is least able to report.
    /// </summary>
    /// <returns>The operator-facing refusal, or null when every spec parses.</returns>
    public static string? ValidateMounts(IEnumerable<string> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        foreach (var mount in mounts)
        {
            try
            {
                _ = FileSystemMount.Parse(mount);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return $"orkeon-host: invalid --mount '{mount}': {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// Refuses an operator mount claiming a root the daemon needs for its own crews, which
    /// would otherwise surface as a raw "Duplicate virtual paths" exception thrown out of a DI
    /// factory — a crash, rather than the configuration mistake it is (ADR-008, decision 5).
    /// <para>
    /// The guard writes its own refusal to <see cref="Console.Error"/>, so stderr is captured
    /// here: under the Windows SCM that message would otherwise go nowhere, and the caller
    /// re-emits whatever the guard wrote so the terminal and journald contracts stay
    /// byte-identical.
    /// </para>
    /// </summary>
    public static ReservedRootsOutcome CheckReservedRoots(
        IEnumerable<string> mounts,
        string? settingsPath,
        string[] reservedRoots)
    {
        ArgumentNullException.ThrowIfNull(reservedRoots);

        var realError = Console.Error;
        using var capture = new StringWriter();
        bool free;
        Console.SetError(capture);
        try
        {
            free = RunnerExecution.EnsureReservedRootsAreFree(mounts, settingsPath, reservedRoots);
        }
        finally
        {
            Console.SetError(realError);
        }

        var written = capture.ToString();
        if (!free)
        {
            var captured = written.TrimEnd();
            return new ReservedRootsOutcome(
                captured.Length > 0 ? captured : "orkeon-host: a reserved virtual root is already claimed.",
                null);
        }

        return new ReservedRootsOutcome(null, written.Length > 0 ? written : null);
    }

    /// <summary>
    /// Whether <paramref name="name"/> is present AND is the last argument — a flag that needs
    /// a value and was given none. Only when it is actually present: <c>Array.LastIndexOf</c>
    /// answers -1 for an absent flag, and with no arguments at all <c>-1 == args.Length - 1</c>
    /// was true, so a bare <c>orkeon-host</c> exited 78 complaining about a flag nobody typed —
    /// and the unit's RestartPreventExitStatus then made sure it was never retried.
    /// </summary>
    private static bool IsLastArgument(string[] args, string name)
    {
        var last = Array.LastIndexOf(args, name);
        return last >= 0 && last == args.Length - 1;
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IEnumerable<string> ArgumentValues(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
                yield return args[index + 1];
        }
    }
}

/// <summary>Bootstrap-time disk probes, before the host and its VFS exist.</summary>
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: probes the operator-supplied settings path before the host (and thus IFileSystemService) is built.")]
internal static class StartupProbes
{
    /// <summary>Whether the operator-supplied settings file exists on the physical disk.</summary>
    public static bool SettingsFileExists(string path) => File.Exists(path);

    /// <summary>
    /// Applies <c>--working-dir</c>: moves the process to <paramref name="path"/> so every
    /// later relative resolution — the settings probe, <see cref="BuildBootConfiguration"/>,
    /// the host built afterwards — reads from where the operator said, not from wherever the
    /// process happened to start (System32, under the Windows SCM).
    /// </summary>
    /// <returns>An error message when the directory does not exist; null when applied.</returns>
    public static string? TryApplyWorkingDirectory(string path)
    {
        if (!Directory.Exists(path))
            return $"working directory not found: {path}";

        Directory.SetCurrentDirectory(path);
        return null;
    }

    /// <summary>
    /// The settings file the daemon reads: the one <c>--settings</c> names, else
    /// <c>./appsettings.json</c> when there is one — what <c>--help</c> promises —, else none. Both
    /// are read from the working directory, made absolute here once: the boot configuration, the
    /// reserved-root guard and the runner host then read the same file.
    /// </summary>
    /// <param name="settingsPath">The <c>--settings</c> value, or null.</param>
    /// <returns>The absolute path of the file to read, or null when there is none.</returns>
    public static string? ResolveSettingsPath(string? settingsPath)
    {
        if (settingsPath is not null)
            return Path.GetFullPath(settingsPath);

        var conventional = Path.GetFullPath(ConventionalNames.SettingsFile);
        return File.Exists(conventional) ? conventional : null;
    }

    /// <summary>
    /// The configuration the daemon boots from — the one that decides which crew directories
    /// become VFS mounts, read before the host exists —, composed as the host composes its own
    /// (<see cref="RunnerSettings.ReadConfiguration"/>): the variables without a prefix, the file
    /// <see cref="ResolveSettingsPath"/> finds, the <c>ORKEON_</c> variables.
    /// <para>
    /// The file is read from the working directory, explicitly. A bare
    /// <see cref="ConfigurationBuilder"/> resolves a relative file against
    /// <see cref="AppContext.BaseDirectory"/> — the executable's own folder — while
    /// <c>--help</c> promises <c>./appsettings.json</c>, <see cref="SettingsFileExists"/>
    /// probes the working directory, and the real host built a few lines later reads the
    /// working directory too. The daemon therefore took its crew list from one file and
    /// everything else from another, and an operator running
    /// <c>orkeon-host</c> from their crews folder got a daemon hosting nothing with no
    /// diagnostic — the file they were looking at had simply never been read.
    /// </para>
    /// <para>
    /// One file (GAP-36): <c>./appsettings.json</c> and the <c>--settings</c> file used to be read
    /// both, the named one over the other — as the default .NET host read them under the host.
    /// </para>
    /// </summary>
    /// <exception cref="HostConfigurationException">
    /// A settings file cannot be read: named, with the line and the position of what its JSON gets
    /// wrong (GAP-35) — it used to escape the start as an unhandled exception.
    /// </exception>
    public static IConfiguration BuildBootConfiguration(string? settingsPath)
    {
        try
        {
            return RunnerSettings.ReadConfiguration(ResolveSettingsPath(settingsPath));
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
        {
            throw new HostConfigurationException(RunnerSettings.DescribeUnreadableSettings(ex), ex);
        }
    }
}
