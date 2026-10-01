using Orkeon.Constants.FileSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Host;
using Orkeon.Hosting;

// The Orkeon service host (GATE-01).
//
// The same binary runs three ways, and that is the point: `orkeon-host` in a terminal to see
// what it does, the same executable registered as a systemd unit or a Windows service to keep
// doing it after you log out. A daemon you cannot run in the foreground is a daemon you cannot
// debug.
//
// It hosts crews; it does not schedule them. Orkeon ships no scheduler, deliberately — a crew
// that should run every morning is run by the operating system, from the artifact
// `orkeon forge promote --schedule` produces, which `orkeon forge schedule` installs with the
// user's consent (Studio asks first) and `orkeon forge unschedule` removes (STUDIO-27).
//
// The startup decisions themselves live in HostStartup: each answers with the operator-facing
// refusal instead of writing it, so this file stays the sequence — ask, report, exit 78 — and
// every refusal goes out through the one reporter that also reaches the Windows event log.

// --help and --version answer and leave.
if (HostStartup.InformationalText(args) is { } informational)
{
    await Console.Out.WriteLineAsync(informational).ConfigureAwait(false);
    return 0;
}

var command = HostStartup.Read(args);

// Flags that need a value have one, --working-dir is applied (before anything else touches the
// disk) and the settings file the operator named really exists. All configuration errors,
// refused before anything starts, with exit 78 so systemd does not loop on them.
if (HostStartup.ValidateArguments(args, command) is { } argumentError)
{
    StartupFailureReporter.Report(argumentError);
    return HostConfigurationException.ExitCode;
}

// Each hosted crew's directory is mounted read-only under a NAME — /crews, /crews-1, …
// (ADR-008), never identity-mapped: the loader reads through the VFS, and a crew path that
// only exists on the physical disk would pass the startup probe and then fail on every
// message. The crew definitions are the host's primary input — declared by the operator in
// the configuration — so their mounts do not require the external-mounts opt-in any more
// than the CLI's own config directory does.
var bootConfiguration = StartupProbes.BuildBootConfiguration(command.SettingsPath);
var bootOptions = bootConfiguration.GetSection(OrkeonHostOptions.SectionName).Get<OrkeonHostOptions>() ?? new OrkeonHostOptions();
var crewPlan = HostCrewMounts.For(bootOptions.Crews);

// A malformed operator --mount is a configuration error, refused here rather than at the
// first message.
if (HostStartup.ValidateMounts(command.Mounts) is { } mountError)
{
    StartupFailureReporter.Report(mountError);
    return HostConfigurationException.ExitCode;
}

// An operator --mount claiming a root the daemon needs for its own crews would otherwise
// surface as a raw "Duplicate virtual paths" exception thrown out of a DI factory (ADR-008,
// decision 5). /sandbox is in this list because AddOrkeonFileSystem mounts it unconditionally,
// in every host — so the guard has to run even when the daemon hosts no crew of its own.
// settingsPath, not just --mount: the daemon is the most settings-driven entry point in the
// repo and was the one the guard could not see, so a /crews claimed in appsettings.json met the
// host's own crew mount and came back as "Duplicate virtual paths" out of a DI factory.
var reservedRoots = HostStartup.CheckReservedRoots(
    command.Mounts, command.SettingsPath, [.. crewPlan.Roots, RunnerVirtualRoots.Sandbox]);

if (reservedRoots.Error is { } reservedRootsError)
{
    StartupFailureReporter.Report(reservedRootsError);
    return HostConfigurationException.ExitCode;
}

// Accepted, but the guard still had something to say: re-emitted so the terminal and journald
// contracts stay byte-identical to the CLI's.
if (reservedRoots.Warnings is { } reservedRootsWarnings)
    await Console.Error.WriteAsync(reservedRootsWarnings).ConfigureAwait(false);

var mounts = new List<string>(command.Mounts);
mounts.AddRange(crewPlan.Mounts);

using var host = RunnerHost.Build(
    command.SettingsPath,
    new RunnerMountPlan
    {
        CliMounts = mounts,
        AllowExternalMounts = crewPlan.Mounts.Count > 0 || args.Contains("--allow-external-mounts", StringComparer.Ordinal),
    },
    configureLogging: null,
    // Everything the daemon adds to the runner host lives in HostServiceRegistration, so a
    // test builds the very host this binary runs.
    configureServices: (context, services) => services.AddHostServices(context.Configuration, crewPlan),
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
    // would bury the one message the operator needs. Under the SCM that message also goes to
    // the Application event log — stderr is invisible there.
    StartupFailureReporter.Report($"orkeon-host: {ex.Message}");
    return HostConfigurationException.ExitCode;
}

// A channel crash sets a non-zero code before stopping the application; a clean stop leaves 0.
return Environment.ExitCode;
