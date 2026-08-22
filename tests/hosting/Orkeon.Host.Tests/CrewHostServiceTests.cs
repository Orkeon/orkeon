using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Orkeon.Host.Tests;

/// <summary>
/// GATE-01's readiness claim, made true: a refused configuration fails the host START —
/// before UseSystemd() ever sends READY=1 — with the exit code the systemd unit excludes
/// from restarts. The first version validated after readiness: systemd recorded a host with
/// no crew as "active (running)" right up to its clean exit, and a crew whose path did not
/// exist was discovered on the first user message, when every run could only fail.
/// </summary>
public sealed class CrewHostServiceTests : IDisposable
{
    private readonly string _crewFile = Path.Combine(Path.GetTempPath(), $"orkeon-host-test-{Guid.NewGuid():N}.yaml");

    public CrewHostServiceTests() => File.WriteAllText(_crewFile, "name: support");

    public void Dispose() => File.Delete(_crewFile);

    private static CrewHostService Build(OrkeonHostOptions options) =>
        new(new CrewHostRegistry(Options.Create(options)), Options.Create(options), NullLogger<CrewHostService>.Instance);

    private HostedCrewOptions Crew(string? path = null) =>
        new() { Name = "support", Path = path ?? _crewFile };

    [Fact]
    public async Task A_host_with_no_crew_refuses_to_start()
    {
        using var service = Build(new OrkeonHostOptions());

        await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_crew_path_that_does_not_exist_is_refused_at_start_not_at_first_message()
    {
        using var service = Build(new OrkeonHostOptions { Crews = [Crew(path: "/nowhere/crew.yaml")] });

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("/nowhere/crew.yaml", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_zero_run_timeout_is_refused_with_the_words_to_fix_it()
    {
        // Zero cancels every run at its first instant; past the CancelAfter ceiling every
        // start throws. Both would otherwise be discovered one failed run at a time.
        using var service = Build(new OrkeonHostOptions { Crews = [Crew()], RunTimeout = TimeSpan.Zero });

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("RunTimeout", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_valid_configuration_starts_and_stops_cleanly()
    {
        using var service = Build(new OrkeonHostOptions
        {
            Crews = [Crew()],
            ShutdownGracePeriod = TimeSpan.FromMilliseconds(50),
        });

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }
}
