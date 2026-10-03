using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// GAP-38 — a crew's <c>maxRpm</c> bounds the model requests of all its agents during its runs,
/// parallel waves and the manager included, and a manager agent's own <c>maxRpm</c> bounds the
/// manager's calls; the request of too many waits its turn, and the run cancels it. Kicked off
/// through the real container, on a manual clock the container hands every part of the run.
/// </summary>
public sealed class MaxRpmKickoffTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    private static readonly string[] ThreeRoles = ["First", "Second", "Third"];

    private static ServiceProvider Host(ClockedLlmVendor vendor, ManualTimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddSingleton<TimeProvider>(clock);
        services.AddOrkeonLlmProvider(_ => vendor);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static CrewTask Task(string description, DomainAgent? agent = null)
    {
        var builder = new CrewTaskBuilder().Description(description).ExpectedOutput("Done");
        if (agent is not null)
            builder.AssignTo(agent);
        return builder.Build();
    }

    /// <summary>Saves the crew, its agents and its tasks, then starts its run.</summary>
    private static async Task<Task<CrewOutput>> StartAsync(
        IServiceProvider services, DomainCrew crew, IEnumerable<DomainAgent> agents, IEnumerable<CrewTask> tasks, CancellationToken run)
    {
        foreach (var agent in agents)
            await services.GetRequiredService<IAgentRepository>().AddAsync(agent, Ct);
        foreach (var task in tasks)
            await services.GetRequiredService<ITaskRepository>().AddAsync(task, Ct);
        await services.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);

        return services.GetRequiredService<ICrewOrchestrationService>()
            .KickoffAsync(crew.Id, new CrewInput("paced", new Dictionary<string, object>()), run);
    }

    /// <summary>
    /// Lets the run go to its end, moving the clock a minute each time something waits on it — never
    /// in real time: the run only ever waits on the clock.
    /// </summary>
    private static async Task<CrewOutput> DriveAsync(Task<CrewOutput> run, ManualTimeProvider clock)
    {
        while (!run.IsCompleted)
        {
            await Polling.WaitUntilAsync(() => run.IsCompleted || clock.PendingTimers > 0);
            if (!run.IsCompleted)
                clock.Advance(Seconds(60));
        }

        return await run;
    }

    // 11 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_parallel_wave_of_three_agents_in_a_crew_with_maxRpm_2_sends_two_requests_then_the_third()
    {
        var clock = new ManualTimeProvider();
        var vendor = new ClockedLlmVendor(() => clock.Elapsed);
        await using var container = Host(vendor, clock);
        await using var scope = container.CreateAsyncScope();
        var agents = ThreeRoles
            .Select(role => new AgentBuilder().Role(role).Goal("Answer one question").Build())
            .ToList();
        var tasks = agents.Select(agent => Task($"Answer for {agent.Role.Value}", agent)).ToList();
        var crew = new CrewBuilder().Goal("Answer three questions").Parallel().MaxRpm(2)
            .WithAgents(agents).WithTasks(tasks).Build();

        var run = await StartAsync(scope.ServiceProvider, crew, agents, tasks, Ct);

        await Polling.WaitUntilAsync(() => run.IsCompleted || clock.PendingTimers > 0);
        Assert.False(run.IsCompleted);
        Assert.Equal([Seconds(0), Seconds(0)], vendor.InstantsOf());

        clock.Advance(Seconds(60));
        var output = await run;

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal([Seconds(0), Seconds(0), Seconds(60)], vendor.InstantsOf().Order());
    }

    // 12 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_manager_agent_with_maxRpm_1_waits_a_minute_between_its_calls()
    {
        var clock = new ManualTimeProvider();
        var vendor = new ClockedLlmVendor(() => clock.Elapsed);
        await using var container = Host(vendor, clock);
        await using var scope = container.CreateAsyncScope();
        var chef = new AgentBuilder().Role("Chef").Goal("Lead the team").MaxRpm(1).Build();
        var writer = new AgentBuilder().Role("Writer").Goal("Write").Build();
        var tasks = new[] { Task("Draft the article"), Task("Proofread the article") };
        var crew = new CrewBuilder().Goal("Ship the article").Hierarchical(chef)
            .WithAgents([chef, writer]).WithTasks(tasks).Build();

        var output = await DriveAsync(await StartAsync(scope.ServiceProvider, crew, [chef, writer], tasks, Ct), clock);

        Assert.True(output.Succeeded, output.Error);
        // Assign, review, assign, review: one a minute. The writer, bound by nothing, works at once.
        Assert.Equal([Seconds(0), Seconds(60), Seconds(120), Seconds(180)], vendor.InstantsOf("Chef"));
        Assert.Equal([Seconds(0), Seconds(120)], vendor.InstantsOf("Writer"));
    }

    [Fact]
    public async Task A_crew_with_maxRpm_counts_its_managers_calls_with_its_workers()
    {
        var clock = new ManualTimeProvider();
        var vendor = new ClockedLlmVendor(() => clock.Elapsed);
        await using var container = Host(vendor, clock);
        await using var scope = container.CreateAsyncScope();
        var chef = new AgentBuilder().Role("Chef").Goal("Lead the team").Build();
        var writer = new AgentBuilder().Role("Writer").Goal("Write").Build();
        var tasks = new[] { Task("Draft the article") };
        var crew = new CrewBuilder().Goal("Ship the article").Hierarchical(chef).MaxRpm(2)
            .WithAgents([chef, writer]).WithTasks(tasks).Build();

        var output = await DriveAsync(await StartAsync(scope.ServiceProvider, crew, [chef, writer], tasks, Ct), clock);

        Assert.True(output.Succeeded, output.Error);
        // The assignment and the writer's turn fill the crew's minute: the review waits for the next.
        Assert.Equal([Seconds(0)], vendor.InstantsOf("Chef", "assign"));
        Assert.Equal([Seconds(0)], vendor.InstantsOf("Writer"));
        Assert.Equal([Seconds(60)], vendor.InstantsOf("Chef", "review"));
    }

    [Fact]
    public async Task Cancelling_the_run_during_a_review_stops_it()
    {
        var clock = new ManualTimeProvider();
        var vendor = new ClockedLlmVendor(() => clock.Elapsed);
        var reviewSawItsTokenCancelled = false;
        vendor.Hold = async (kind, token) =>
        {
            if (kind != "review")
                return;
            try
            {
                await System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                reviewSawItsTokenCancelled = true;
                throw;
            }
        };
        await using var container = Host(vendor, clock);
        await using var scope = container.CreateAsyncScope();
        var chef = new AgentBuilder().Role("Chef").Goal("Lead the team").Build();
        var writer = new AgentBuilder().Role("Writer").Goal("Write").Build();
        var tasks = new[] { Task("Draft the article"), Task("Proofread the article") };
        var crew = new CrewBuilder().Goal("Ship the article").Hierarchical(chef)
            .WithAgents([chef, writer]).WithTasks(tasks).Build();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = await StartAsync(scope.ServiceProvider, crew, [chef, writer], tasks, stop.Token);
        await Polling.WaitUntilAsync(() => vendor.InstantsOf("Chef", "review").Length == 1);
        await stop.CancelAsync();
        var output = await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.False(output.Succeeded);
        Assert.True(reviewSawItsTokenCancelled);
        // The run stopped there: the second task was never handed out.
        Assert.Single(vendor.InstantsOf("Chef", "assign"));
    }
}
