using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Host;
using Orkeon.Hosting;

// The Orkeon service host (GATE-01).
//
// The same binary runs three ways, and that is the point: `orkeon-host` in a terminal to see
// what it does, the same executable registered as a systemd unit or a Windows service to keep
// doing it after you log out. A daemon you cannot run in the foreground is a daemon you cannot
// debug.
//
// It hosts crews; it does not schedule them. rc.2 ships no scheduler, deliberately — a crew
// that should run every morning still needs the artifact `orkeon forge promote --schedule`
// produces, installed by a person.

// --help and --version answer and leave. Before this, `orkeon-host --help` silently
// ignored the flag and started the daemon — the least helpful possible reading of a
// question, from a binary whose whole documentation says "run it in a terminal first".
if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
{
    await Console.Out.WriteLineAsync("""
orkeon-host — the Orkeon service host: hosts crews as a daemon and answers chat channels.

Usage:
  orkeon-host [--settings <file>] [--mount <physical:virtual[:rw|ro]>]... [--allow-external-mounts]

Options:
  -s, --settings <file>     Configuration file (JSON). Defaults to ./appsettings.json.
  -m, --mount <spec>        Additional VFS mount. Hosted crew directories are mounted
                            automatically, read-only.
      --allow-external-mounts
                            Allow mounts outside the working directory.
  -h, --help                Show this help and exit.
      --version             Show the version and exit.

Configuration lives under Orkeon:Host (crews, RunTimeout, ShutdownGracePeriod) and
Orkeon:Host:Discord. Secrets are named by environment variable, never written in files.
Documentation: docs/architecture/service-host.md
""").ConfigureAwait(false);
    return 0;
}

if (args.Contains("--version", StringComparer.Ordinal))
{
    var version = typeof(OrkeonHostOptions).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
        is [System.Reflection.AssemblyInformationalVersionAttribute info, ..] ? info.InformationalVersion : "unknown";
    await Console.Out.WriteLineAsync($"orkeon-host {version}").ConfigureAwait(false);
    return 0;
}

var settingsPath = ArgumentValue(args, "--settings") ?? ArgumentValue(args, "-s");
var mounts = ArgumentValues(args, "--mount").Concat(ArgumentValues(args, "-m")).ToList();

// A --settings with no value used to become a silent null, and a typo'd path was silently
// ignored by the host builder — the daemon then started with zero crews and the operator got
// "nothing configured" instead of "your file is not where you said". Both are configuration
// errors, refused before anything starts, with exit 78 so systemd does not loop on them.
// OUT-OF-SCOPE: probing the operator-supplied settings path; bootstrap runs before the VFS.
// LastIndexOf, and only when the flag is actually present: Array.IndexOf returns -1 for an
// absent flag, and with no arguments at all `-1 == args.Length - 1` was true — a bare
// `orkeon-host` exited 78 complaining about a flag nobody typed, and the unit's
// RestartPreventExitStatus then made sure it was never retried.
var lastSettingsFlag = Math.Max(Array.LastIndexOf(args, "--settings"), Array.LastIndexOf(args, "-s"));
if (lastSettingsFlag >= 0 && lastSettingsFlag == args.Length - 1)
{
    await Console.Error.WriteLineAsync("orkeon-host: --settings requires a path.").ConfigureAwait(false);
    return HostConfigurationException.ExitCode;
}

if (settingsPath is not null && !StartupProbes.SettingsFileExists(settingsPath))
{
    await Console.Error.WriteLineAsync($"orkeon-host: settings file not found: {settingsPath}").ConfigureAwait(false);
    return HostConfigurationException.ExitCode;
}

// Each hosted crew's directory is mounted read-only, 1:1: the loader reads through the VFS,
// and a crew path that only exists on the physical disk would pass the startup probe and
// then fail on every message. The crew definitions are the host's primary input — declared
// by the operator in the configuration — so their mounts do not require the external-mounts
// opt-in any more than the CLI's own config directory does.
var bootConfiguration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(settingsPath ?? "appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddEnvironmentVariables("ORKEON_")
    .Build();
var bootOptions = bootConfiguration.GetSection(OrkeonHostOptions.SectionName).Get<OrkeonHostOptions>() ?? new OrkeonHostOptions();
var crewPlan = HostCrewMounts.For(bootOptions.Crews);
mounts.AddRange(crewPlan.Mounts);

using var host = RunnerHost.Build(
    settingsPath,
    mounts,
    allowExternalMounts: crewPlan.Mounts.Count > 0 || args.Contains("--allow-external-mounts", StringComparer.Ordinal),
    llmLogVirtualPath: null,
    configureLogging: null,
    configureServices: (context, services) =>
    {
        services.Configure<OrkeonHostOptions>(context.Configuration.GetSection(OrkeonHostOptions.SectionName));

        // The crew→virtual-path map travels with the mounts that made it true: CrewRunner
        // loads a hosted crew by its virtual spelling, never by the operator's disk path.
        services.AddSingleton(crewPlan);

        // The generic host caps the WHOLE stop sequence at HostOptions.ShutdownTimeout
        // (default 30 s). Left alone, an operator raising ShutdownGracePeriod past ~25 s
        // silently truncated their own drain — the docs tell them to scale TimeoutStopSec,
        // and the framework then cut them off underneath it. Budget: the grace, the drain's
        // 5 s teardown wait, and 5 s for the channel to disconnect.
        var hostSection = context.Configuration.GetSection(OrkeonHostOptions.SectionName).Get<OrkeonHostOptions>() ?? new OrkeonHostOptions();
        services.Configure<Microsoft.Extensions.Hosting.HostOptions>(
            o => o.ShutdownTimeout = hostSection.ShutdownGracePeriod + TimeSpan.FromSeconds(10));

        services.AddSingleton<CrewHostRegistry>();
        services.AddSingleton<CrewRunner>();
        services.AddSingleton<ICrewRunner>(sp => sp.GetRequiredService<CrewRunner>());

        // One progress hook per run scope: the strategies dispatch task completions into it,
        // and CrewRunner wires its callback to the conversation watching the run. This is
        // what makes "reports progress as tasks finish" true rather than documented.
        services.AddScoped<RunProgressHook>();
        services.AddScoped<Orkeon.Application.Crew.ICrewExecutionHook>(
            sp => sp.GetRequiredService<RunProgressHook>());

        services.Configure<Orkeon.Host.Gateway.DiscordChannelOptions>(
            context.Configuration.GetSection(Orkeon.Host.Gateway.DiscordChannelOptions.SectionName));

        // Registration order is stop order reversed (hosted services stop LIFO): the channel
        // FIRST so it stops LAST — the drain must run while the channel can still deliver,
        // or the grace period keeps runs alive to produce answers nobody can receive.
        services.AddHostedService<Orkeon.Host.Gateway.ChatChannelService>();
        services.AddHostedService<CrewHostService>();
    },
    configureBuilder: builder => builder
        .UseSystemd()
        .UseWindowsService(options => options.ServiceName = "Orkeon"));

// Both are no-ops when the process is not running under the corresponding supervisor, so the
// same build works in a terminal, under systemd and under the Windows SCM without a flag.
try
{
    await host.RunAsync().ConfigureAwait(false);
}
catch (HostConfigurationException ex)
{
    // A refused configuration fails the START — before READY=1 ever went out — and exits 78,
    // which the systemd unit excludes from restarts: looping on a typo every ten seconds
    // would bury the one message the operator needs.
    await Console.Error.WriteLineAsync($"orkeon-host: {ex.Message}").ConfigureAwait(false);
    return HostConfigurationException.ExitCode;
}

// A channel crash sets a non-zero code before stopping the application; a clean stop leaves 0.
return Environment.ExitCode;

static string? ArgumentValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static IEnumerable<string> ArgumentValues(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
            yield return args[index + 1];
    }
}

/// <summary>Bootstrap-time disk probes, before the host and its VFS exist.</summary>
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: probes the operator-supplied settings path before the host (and thus IFileSystemService) is built.")]
internal static class StartupProbes
{
    /// <summary>Whether the operator-supplied settings file exists on the physical disk.</summary>
    public static bool SettingsFileExists(string path) => File.Exists(path);
}
