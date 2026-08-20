using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

var host = RunnerHost.Build(
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

        services.AddHealthChecks().AddCheck<CrewHostHealthCheck>("orkeon-host");
    },
    configureBuilder: builder => builder
        .UseSystemd()
        .UseWindowsService(options => options.ServiceName = "Orkeon"));

// Both are no-ops when the process is not running under the corresponding supervisor, so the
// same build works in a terminal, under systemd and under the Windows SCM without a flag.
await host.RunAsync().ConfigureAwait(false);

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
