using Microsoft.Extensions.Options;
using Orkeon.Host;

namespace Orkeon.Host.Tests;

/// <summary>
/// GATE-02: what the service hosts, what is in flight, and the ceiling that keeps a daemon
/// alive under a burst. A chat channel makes bursts trivial — one enthusiastic user, ten
/// threads — so refusing the eleventh with a clear answer is a feature, not a limitation.
/// </summary>
public class CrewHostRegistryTests
{
    private static CrewHostRegistry Build(params HostedCrewOptions[] crews) =>
        new(Options.Create(new OrkeonHostOptions { Crews = crews }));

    private static HostedCrewOptions Crew(string name, int maxRuns = 2) => new()
    {
        Name = name,
        Path = $"/crews/{name}.yaml",
        Profile = new CrewHostingProfile { MaxConcurrentRuns = maxRuns },
    };

    [Fact]
    public void An_unknown_crew_is_refused_rather_than_started()
    {
        var registry = Build(Crew("support"));

        Assert.Null(registry.Find("billing"));
        Assert.Null(registry.TryStart("billing", "discord:thread-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_crew_is_found_however_its_name_is_typed()
    {
        // People type crew names into a chat window. Case is not a meaningful distinction there.
        var registry = Build(Crew("Support"));

        Assert.NotNull(registry.Find("support"));
        Assert.NotNull(registry.Find("SUPPORT"));
    }

    [Fact]
    public void The_ceiling_refuses_rather_than_queues()
    {
        // "We are busy" is an answer a channel relays to a person. An invisible queue is not.
        var registry = Build(Crew("support", maxRuns: 2));

        Assert.NotNull(registry.TryStart("support", "discord:thread-1", TestContext.Current.CancellationToken));
        Assert.NotNull(registry.TryStart("support", "discord:thread-2", TestContext.Current.CancellationToken));
        Assert.Null(registry.TryStart("support", "discord:thread-3", TestContext.Current.CancellationToken));

        Assert.Equal(2, registry.Running.Count);
    }

    [Fact]
    public void Finishing_a_run_frees_its_slot()
    {
        var registry = Build(Crew("support", maxRuns: 1));

        var first = registry.TryStart("support", "discord:thread-1", TestContext.Current.CancellationToken)!;
        Assert.Null(registry.TryStart("support", "discord:thread-2", TestContext.Current.CancellationToken));

        registry.Finish(first.Id);

        Assert.NotNull(registry.TryStart("support", "discord:thread-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void One_crew_s_ceiling_does_not_bind_another()
    {
        var registry = Build(Crew("support", maxRuns: 1), Crew("billing", maxRuns: 1));

        Assert.NotNull(registry.TryStart("support", "discord:thread-1", TestContext.Current.CancellationToken));
        Assert.NotNull(registry.TryStart("billing", "discord:thread-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Stopping_one_run_leaves_the_others_alone()
    {
        var registry = Build(Crew("support", maxRuns: 3));

        var first = registry.TryStart("support", "discord:thread-1", TestContext.Current.CancellationToken)!;
        var second = registry.TryStart("support", "discord:thread-2", TestContext.Current.CancellationToken)!;

        Assert.True(registry.RequestStop(first.Id));

        Assert.True(first.Cancellation.IsCancellationRequested);
        Assert.False(second.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Stopping_something_already_stopped_is_not_an_error()
    {
        // A user pressing the stop button twice has not done anything wrong.
        var registry = Build(Crew("support"));
        var run = registry.TryStart("support", "discord:thread-1", TestContext.Current.CancellationToken)!;

        Assert.True(registry.RequestStop(run.Id));
        Assert.False(registry.RequestStop(run.Id));
        Assert.False(registry.RequestStop("never-existed"));
    }

    [Fact]
    public void Stopping_everything_reports_how_many_were_asked()
    {
        var registry = Build(Crew("support", maxRuns: 3));
        registry.TryStart("support", "discord:thread-1", TestContext.Current.CancellationToken);
        registry.TryStart("support", "discord:thread-2", TestContext.Current.CancellationToken);

        Assert.Equal(2, registry.RequestStopAll());
        Assert.Equal(0, registry.RequestStopAll());   // nothing left to ask
    }

    [Fact]
    public void A_run_is_cancelled_when_the_host_stops()
    {
        // The host's stopping token is linked into every run, which is how a shutdown reaches
        // work in flight instead of abandoning it.
        var registry = Build(Crew("support"));
        using var hostStopping = new CancellationTokenSource();

        var run = registry.TryStart("support", "discord:thread-1", hostStopping.Token)!;
        hostStopping.Cancel();

        Assert.True(run.Cancellation.IsCancellationRequested);
    }
}
