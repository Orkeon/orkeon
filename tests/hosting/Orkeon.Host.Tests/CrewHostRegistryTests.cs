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
        Assert.Null(registry.TryStart("billing", "discord:thread-1"));
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

        Assert.NotNull(registry.TryStart("support", "discord:thread-1"));
        Assert.NotNull(registry.TryStart("support", "discord:thread-2"));
        Assert.Null(registry.TryStart("support", "discord:thread-3"));

        Assert.Equal(2, registry.Running.Count);
    }

    [Fact]
    public void Finishing_a_run_frees_its_slot()
    {
        var registry = Build(Crew("support", maxRuns: 1));

        var first = registry.TryStart("support", "discord:thread-1")!;
        Assert.Null(registry.TryStart("support", "discord:thread-2"));

        registry.Finish(first.Id);

        Assert.NotNull(registry.TryStart("support", "discord:thread-2"));
    }

    [Fact]
    public void One_crew_s_ceiling_does_not_bind_another()
    {
        var registry = Build(Crew("support", maxRuns: 1), Crew("billing", maxRuns: 1));

        Assert.NotNull(registry.TryStart("support", "discord:thread-1"));
        Assert.NotNull(registry.TryStart("billing", "discord:thread-2"));
    }

    [Fact]
    public void Stopping_one_run_leaves_the_others_alone()
    {
        var registry = Build(Crew("support", maxRuns: 3));

        var first = registry.TryStart("support", "discord:thread-1")!;
        var second = registry.TryStart("support", "discord:thread-2")!;

        Assert.True(registry.RequestStop(first.Id));

        Assert.True(first.Cancellation.IsCancellationRequested);
        Assert.False(second.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Stopping_something_already_stopped_is_not_an_error()
    {
        // A user pressing the stop button twice has not done anything wrong.
        var registry = Build(Crew("support"));
        var run = registry.TryStart("support", "discord:thread-1")!;

        Assert.True(registry.RequestStop(run.Id));
        Assert.False(registry.RequestStop(run.Id));
        Assert.False(registry.RequestStop("never-existed"));
    }

    [Fact]
    public void Stopping_everything_reports_how_many_were_asked()
    {
        var registry = Build(Crew("support", maxRuns: 3));
        registry.TryStart("support", "discord:thread-1");
        registry.TryStart("support", "discord:thread-2");

        Assert.Equal(2, registry.RequestStopAll());
        Assert.Equal(0, registry.RequestStopAll());   // nothing left to ask
    }

    [Fact]
    public void A_run_is_not_linked_to_any_caller_token()
    {
        // The first version linked runs to the channel's stopping token — on SIGTERM every
        // run died at t=0 and the shutdown grace period politely waited for corpses. Stopping
        // a run is the registry's own gesture: RequestStop, or the drain after the grace.
        var registry = Build(Crew("support"));
        var run = registry.TryStart("support", "discord:thread-1")!;

        Assert.False(run.Cancellation.IsCancellationRequested);
        Assert.Equal(1, registry.RequestStopAll());
        Assert.True(run.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Stop_racing_a_finishing_run_reads_as_already_finished()
    {
        // Finish disposes the run's cancellation source. A Stop pressed the same instant a
        // run ends must come back false — not throw ObjectDisposedException out of a Discord
        // button handler, where nothing catches it and the user sees nothing at all.
        var registry = Build(Crew("support"));
        var run = registry.TryStart("support", "discord:thread-1")!;

        // The narrow window: Finish has disposed the source but the stop request still holds
        // a reference to the run (a snapshot in RequestStopAll, a TryGetValue in RequestStop).
        run.Cancellation.Dispose();

        Assert.False(registry.RequestStop(run.Id));
        Assert.Equal(0, registry.RequestStopAll());

        registry.Finish(run.Id);
        Assert.False(registry.RequestStop(run.Id));
    }

    [Fact]
    public async Task Simultaneous_admissions_cannot_overshoot_the_ceiling()
    {
        // Count-then-add let two callers both read Max-1 and both get in. Admission is
        // atomic now; under a burst the ceiling holds exactly.
        var registry = Build(Crew("support", maxRuns: 4));

        var admitted = 0;
        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            if (registry.TryStart("support", $"discord:thread-{i}") is not null)
                Interlocked.Increment(ref admitted);
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(4, admitted);
        Assert.Equal(4, registry.Running.Count);
    }
}
