using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;

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
    public async Task Two_crews_with_one_name_are_refused_as_ambiguous()
    {
        // GAP-11: a chat route names a crew; two crews answering to one name (names compare
        // case-insensitively) would leave the route to a coin toss.
        using var service = Build(new OrkeonHostOptions
        {
            Crews = [Crew(), new HostedCrewOptions { Name = "SUPPORT", Path = _crewFile }],
        });

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("SUPPORT", ex.Message, StringComparison.Ordinal);
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

    private static IConfiguration Profiles(params string[] names) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(names.Select(n => new KeyValuePair<string, string?>($"Llm:Profiles:{n}:Model", "m")))
            .Build();

    [Fact]
    public async Task An_llm_profile_allow_list_naming_an_undefined_profile_is_refused_at_start()
    {
        // GAP-17: the allow-list names the profiles hosted crews may pick; a typo there would
        // refuse, one crew load at a time, the profile the operator meant to offer.
        var options = new OrkeonHostOptions { Crews = [Crew()], LlmProfiles = ["default", "claude", "cluade"] };
        using var service = new CrewHostService(
            new CrewHostRegistry(Options.Create(options)), Options.Create(options), NullLogger<CrewHostService>.Instance,
            configuration: Profiles("claude", "local"));

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("'cluade'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Defined profiles: default, claude, local.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_llm_profile_allow_list_of_defined_profiles_is_accepted()
    {
        var options = new OrkeonHostOptions
        {
            Crews = [Crew()],
            LlmProfiles = ["default", "LOCAL"],
            ShutdownGracePeriod = TimeSpan.FromMilliseconds(50),
        };
        var log = new MockLogger<CrewHostService>();
        using var service = new CrewHostService(
            new CrewHostRegistry(Options.Create(options)), Options.Create(options), log,
            configuration: Profiles("claude", "local"));

        await service.StartAsync(CancellationToken.None);

        // Accepted: the loop is up (its first act is to announce each hosted crew). Waited
        // for, as in the clean start/stop test below: a stop landing before the loop's first
        // instruction completes ExecuteTask as Canceled under a loaded parallel run.
        Assert.NotNull(service.ExecuteTask);
        await Polling.WaitUntilAsync(() => log.LogCallCount > 0);
        Assert.Contains("Hosting crew 'support'", log.LastLogMessage, StringComparison.Ordinal);

        await service.StopAsync(CancellationToken.None);

        // The loop ended on the stop signal, not on a refusal.
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_valid_configuration_starts_and_stops_cleanly()
    {
        var options = new OrkeonHostOptions
        {
            Crews = [Crew()],
            ShutdownGracePeriod = TimeSpan.FromMilliseconds(50),
        };
        var log = new MockLogger<CrewHostService>();
        using var service = new CrewHostService(new CrewHostRegistry(Options.Create(options)), Options.Create(options), log);

        await service.StartAsync(TestContext.Current.CancellationToken);

        // Started: the configuration was accepted and the loop is up. "Up" is the loop's own
        // announcement (its first act is to log each hosted crew), not StartAsync returning:
        // on .NET 10 the base class hands ExecuteAsync to the thread pool, so a stop that
        // lands before its first instruction ran completes ExecuteTask as Canceled — not the
        // clean stop this test pins, and exactly what happened whenever the assembly's other
        // classes kept the pool busy.
        Assert.NotNull(service.ExecuteTask);
        await Polling.WaitUntilAsync(() => log.LogCallCount > 0);
        Assert.Contains("Hosting crew 'support'", log.LastLogMessage, StringComparison.Ordinal);

        await service.StopAsync(TestContext.Current.CancellationToken);

        // Stopped cleanly: the loop really ended, on the stop signal rather than on a
        // fault. Awaited rather than asserted on the spot because the base StopAsync does
        // not wait for ExecuteAsync's post-cancellation tail on .NET 10.
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task The_drain_stops_what_the_grace_did_not_see_finish()
    {
        // With runs no longer linked to the stopping token, the drain is the thing that
        // actually reaches them: grace first, then RequestStopAll. This pins that a stop
        // request really lands on a run still in flight at shutdown.
        var options = new OrkeonHostOptions
        {
            Crews = [Crew()],
            ShutdownGracePeriod = TimeSpan.FromMilliseconds(50),
        };
        var registry = new CrewHostRegistry(Options.Create(options));
        using var service = new CrewHostService(registry, Options.Create(options), NullLogger<CrewHostService>.Instance);

        // Deliberately CancellationToken.None: BackgroundService links its stopping token
        // to whatever StartAsync receives, and xUnit's ambient test token firing under a
        // loaded parallel run made the service stop itself before the run was even started.
        await service.StartAsync(CancellationToken.None);

        var run = registry.TryStart("support", "test:drain", out _)!;
        Assert.False(run.Cancellation.IsCancellationRequested);

        await service.StopAsync(CancellationToken.None);

        Assert.True(run.Cancellation.IsCancellationRequested);
        Assert.True(run.StopRequested);
    }

    [Fact]
    public async Task A_run_asked_for_during_the_drain_is_refused_and_the_drain_ends_with_the_run_admitted_before()
    {
        // GAP-35: the drain admitted what arrived during the grace period, waited for it, then
        // stopped it — the person read "The run was stopped." about work that should never have
        // begun, and the stop lasted the whole grace. The stop closes admission first.
        var options = new OrkeonHostOptions
        {
            Crews = [Crew()],
            ShutdownGracePeriod = TimeSpan.FromSeconds(30),
        };
        var registry = new CrewHostRegistry(Options.Create(options));
        using var service = new CrewHostService(registry, Options.Create(options), NullLogger<CrewHostService>.Instance);
        await service.StartAsync(CancellationToken.None);
        var before = registry.TryStart("support", "test:before", out _)!;

        var stopping = service.StopAsync(CancellationToken.None);

        Assert.Null(registry.TryStart("support", "test:during", out var refusal));
        Assert.Equal(AdmissionRefusal.HostStopping, refusal);
        Assert.False(stopping.IsCompleted);

        // The run admitted before finishes within its grace: the drain ends with it, long before
        // the 30-second deadline, and never had to stop it.
        registry.Finish(before.Id);
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(before.StopRequested);
    }
}
