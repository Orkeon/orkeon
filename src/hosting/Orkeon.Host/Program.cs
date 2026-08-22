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

var settingsPath = ArgumentValue(args, "--settings") ?? ArgumentValue(args, "-s");
var mounts = ArgumentValues(args, "--mount").Concat(ArgumentValues(args, "-m")).ToList();

// A --settings with no value used to become a silent null, and a typo'd path was silently
// ignored by the host builder — the daemon then started with zero crews and the operator got
// "nothing configured" instead of "your file is not where you said". Both are configuration
// errors, refused before anything starts, with exit 78 so systemd does not loop on them.
// OUT-OF-SCOPE: probing the operator-supplied settings path; bootstrap runs before the VFS.
if ((Array.IndexOf(args, "--settings") == args.Length - 1) || (Array.IndexOf(args, "-s") == args.Length - 1))
{
    await Console.Error.WriteLineAsync("orkeon-host: --settings requires a path.").ConfigureAwait(false);
    return HostConfigurationException.ExitCode;
}

if (settingsPath is not null && !StartupProbes.SettingsFileExists(settingsPath))
{
    await Console.Error.WriteLineAsync($"orkeon-host: settings file not found: {settingsPath}").ConfigureAwait(false);
    return HostConfigurationException.ExitCode;
}

using var host = RunnerHost.Build(
    settingsPath,
    mounts,
    allowExternalMounts: args.Contains("--allow-external-mounts", StringComparer.Ordinal),
    llmLogPath: null,
    configureLogging: null,
    configureServices: (context, services) =>
    {
        services.Configure<OrkeonHostOptions>(context.Configuration.GetSection(OrkeonHostOptions.SectionName));

        services.AddSingleton<CrewHostRegistry>();
        services.AddSingleton<CrewRunner>();
        services.AddSingleton<ICrewRunner>(sp => sp.GetRequiredService<CrewRunner>());
        services.AddHostedService<CrewHostService>();

        services.Configure<Orkeon.Host.Gateway.DiscordChannelOptions>(
            context.Configuration.GetSection(Orkeon.Host.Gateway.DiscordChannelOptions.SectionName));
        services.AddHostedService<Orkeon.Host.Gateway.ChatChannelService>();
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
