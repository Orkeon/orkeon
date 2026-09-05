using Microsoft.Extensions.Configuration;
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
/// Everything the daemon decides before a host exists: what the command line says, and every
/// reason to refuse a configuration rather than start on it (GATE-01).
/// <para>
/// Each check answers with the operator-facing message instead of writing it, so the single
/// exit path in <c>Program.cs</c> stays one <c>Report</c> + exit 78 — and so a test can hold
/// the wording without capturing a console.
/// </para>
/// </summary>
internal static class HostStartup
{
    private const string HelpText = """
    orkeon-host — the Orkeon service host: hosts crews as a daemon and answers chat channels.

    Usage:
      orkeon-host [--settings <file>] [--working-dir <dir>] [--mount <physical>:<virtual>:<ro|rw|rwnd>]... [--allow-external-mounts]

    Options:
      -s, --settings <file>     Configuration file (JSON). Defaults to ./appsettings.json,
                                resolved against the working directory.
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

    Configuration lives under Orkeon:Host (crews, RunTimeout, ShutdownGracePeriod) and
    Orkeon:Host:Discord. Secrets are named by environment variable, never written in files.
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
    /// The configuration the daemon boots from — the one that decides which crew directories
    /// become VFS mounts, read before the host exists.
    /// <para>
    /// The base path is the working directory, explicitly. A bare
    /// <see cref="ConfigurationBuilder"/> resolves a relative file against
    /// <see cref="AppContext.BaseDirectory"/> — the executable's own folder — while
    /// <c>--help</c> promises <c>./appsettings.json</c>, <see cref="SettingsFileExists"/>
    /// probes the working directory, and the real host built a few lines later reads the
    /// working directory too. The daemon therefore took its crew list from one file and
    /// everything else from another, and an operator running
    /// <c>orkeon-host</c> from their crews folder got a daemon hosting nothing with no
    /// diagnostic — the file they were looking at had simply never been read.
    /// </para>
    /// </summary>
    public static IConfigurationRoot BuildBootConfiguration(string? settingsPath) =>
        new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(ConventionalNames.SettingsFile, optional: true)
            .AddJsonFile(settingsPath ?? ConventionalNames.SettingsFile, optional: true)
            .AddEnvironmentVariables()
            .AddEnvironmentVariables("ORKEON_")
            .Build();
}
