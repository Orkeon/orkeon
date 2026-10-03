using Microsoft.Extensions.Hosting;
using Orkeon.Host;

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

// Everything that precedes the start — the command line, the configuration the daemon boots from,
// its mounts, the runner host — is HostStartup's sequence: a refusal is reported (stderr, and the
// Application event log under the SCM) and the process leaves with exit 78. The two supervisors'
// lifetimes are no-ops when the process is not running under them, so the same build works in a
// terminal, under systemd and under the Windows SCM without a flag.
var launch = HostStartup.Prepare(
    args,
    StartupFailureReporter.Report,
    builder => builder
        .UseSystemd()
        .UseWindowsService(options => options.ServiceName = "Orkeon"));
if (launch.Host is null)
    return launch.ExitCode;

using var host = launch.Host;

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
