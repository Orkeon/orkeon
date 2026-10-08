using System.Runtime.CompilerServices;
using Orkeon.Hosting;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-40 — the settings file the service-host example hands an operator passes the daemon's start
/// validation: the runner host's sections, and the daemon's own — <c>Orkeon:Host</c> with its
/// <c>A2A</c>, <c>Orkeon:Host:Discord</c>. It carried <c>Llm:ApiKeyEnvironmentVariable</c>, which nothing
/// reads: the daemon started without its DeepSeek key, and every run failed on "API key is required".
/// The crews' and the mounts' folders belong to the machine, not to the settings: no crew mount here.
/// </summary>
public sealed class HostExampleSettingsTests
{
    [Fact]
    public void The_service_host_example_passes_the_daemons_start_validation()
    {
        var settings = Path.Combine(RepositoryRoot(), "examples", "service-host", "appsettings.host.json");
        var noCrewMount = new HostCrewMountPlan([], new Dictionary<string, string>(), []);

        var verdict = RunnerHost.InspectSettings(
            settings,
            (context, services) => services.AddHostServices(context.Configuration, noCrewMount));

        Assert.Empty(verdict.Refusals);
        Assert.Empty(verdict.Notices);
    }

    /// <summary>
    /// The daemon reports what a runner reports — a section Orkeon knows and no shipped binary reads —
    /// and says nothing of its own section, which <c>orkeon run</c> leaves to it.
    /// </summary>
    [Fact]
    public void The_daemon_reports_a_section_no_shipped_binary_reads_and_not_its_own()
    {
        var directory = Directory.CreateTempSubdirectory("orkeon-host-notices-").FullName;
        try
        {
            var settings = Path.Combine(directory, "host.json");
            File.WriteAllText(settings, """
                {
                  "RaggableTree": { "Enabled": false },
                  "Orkeon": { "Host": { "RunTimeout": "00:10:00" }, "Dlp": { "Enabled": true } }
                }
                """);
            var noCrewMount = new HostCrewMountPlan([], new Dictionary<string, string>(), []);

            var verdict = RunnerHost.InspectSettings(
                settings,
                (context, services) => services.AddHostServices(context.Configuration, noCrewMount));

            Assert.Empty(verdict.Refusals);
            Assert.StartsWith("Orkeon:Dlp is read by no component of this host", Assert.Single(verdict.Notices), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_key_the_daemon_does_not_know_is_refused_by_its_start_validation()
    {
        var directory = Directory.CreateTempSubdirectory("orkeon-host-keys-").FullName;
        try
        {
            var settings = Path.Combine(directory, "host.json");
            File.WriteAllText(settings, """
                {
                  "RaggableTree": { "Enabled": false },
                  "Orkeon": { "Host": { "RunTimeoutt": "00:10:00", "Discord": { "Enabeld": true } } }
                }
                """);
            var noCrewMount = new HostCrewMountPlan([], new Dictionary<string, string>(), []);

            var refusals = RunnerHost.ValidateSettings(
                settings,
                (context, services) => services.AddHostServices(context.Configuration, noCrewMount));

            Assert.Contains(refusals, refusal => refusal.Contains("Orkeon:Host:RunTimeoutt", StringComparison.Ordinal));
            Assert.Contains(refusals, refusal => refusal.Contains("Orkeon:Host:Discord:Enabeld", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
