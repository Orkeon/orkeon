using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Memory;
using Orkeon.Infrastructure.Persistence.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainAgentRole = Orkeon.Domain.Agent.ValueObjects.AgentRole;
using DomainAgentGoal = Orkeon.Domain.Agent.ValueObjects.AgentGoal;
using DomainCrewTask = Orkeon.Domain.Task.CrewTask;
using DomainTaskDescription = Orkeon.Domain.Task.ValueObjects.TaskDescription;
using DomainExpectedOutput = Orkeon.Domain.Task.ValueObjects.ExpectedOutput;

namespace Orkeon.Infrastructure.Tests.CovStubs;

/// <summary>Predicate-backed specification used by repository coverage tests.</summary>
internal sealed class CovStubsPredicateSpec<T> : ISpecification<T>
{
    private readonly Func<T, bool> _predicate;
    public CovStubsPredicateSpec(Func<T, bool> predicate) => _predicate = predicate;
    public bool IsSatisfiedBy(T candidate) => _predicate(candidate);
    public ISpecification<T> AndWith(ISpecification<T> other) => new CovStubsPredicateSpec<T>(c => _predicate(c) && other.IsSatisfiedBy(c));
    public ISpecification<T> OrWith(ISpecification<T> other) => new CovStubsPredicateSpec<T>(c => _predicate(c) || other.IsSatisfiedBy(c));
    public ISpecification<T> Negate() => new CovStubsPredicateSpec<T>(c => !_predicate(c));
}

/// <summary>Recording unit-of-work that captures tracked aggregates.</summary>
internal sealed class CovStubsRecordingUnitOfWork : IUnitOfWork
{
    public List<IHasDomainEvents> Tracked { get; } = [];
    public void Track(IHasDomainEvents aggregate) => Tracked.Add(aggregate);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}

public sealed class CovStubs_InMemoryAgentRepositoryTests
{
    private readonly CovStubsRecordingUnitOfWork _uow = new();
    private InMemoryAgentRepository CreateSut() => new(_uow);

    private static DomainAgent CreateAgent(string role = "analyst")
        => DomainAgent.Create(DomainAgentRole.From(role), DomainAgentGoal.From("goal of " + role));

    [Fact]
    public void Constructor_NullUnitOfWork_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryAgentRepository(null!));

    [Fact]
    public async Task Add_TracksAndPersists()
    {
        var sut = CreateSut();
        var agent = CreateAgent();

        await sut.AddAsync(agent, TestContext.Current.CancellationToken);

        Assert.Same(agent, await sut.GetByIdAsync(agent.Id, TestContext.Current.CancellationToken));
        Assert.True(await sut.ExistsAsync(agent.Id, TestContext.Current.CancellationToken));
        Assert.Contains(agent, _uow.Tracked);
    }

    [Fact]
    public async Task GetById_Unknown_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(await sut.GetByIdAsync(AgentId.Create(), TestContext.Current.CancellationToken));
        Assert.False(await sut.ExistsAsync(AgentId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ReplacesAndTracks()
    {
        var sut = CreateSut();
        var agent = CreateAgent();
        await sut.AddAsync(agent, TestContext.Current.CancellationToken);

        await sut.UpdateAsync(agent, TestContext.Current.CancellationToken);

        Assert.Equal(1, await sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_RemovesAgent()
    {
        var sut = CreateSut();
        var agent = CreateAgent();
        await sut.AddAsync(agent, TestContext.Current.CancellationToken);

        await sut.DeleteAsync(agent.Id, TestContext.Current.CancellationToken);

        Assert.False(await sut.ExistsAsync(agent.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, await sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Find_WithSpecification_FiltersPagesAndCounts()
    {
        var sut = CreateSut();
        var a1 = CreateAgent("alpha");
        var a2 = CreateAgent("beta");
        var a3 = CreateAgent("alpha");
        await sut.AddAsync(a1, TestContext.Current.CancellationToken);
        await sut.AddAsync(a2, TestContext.Current.CancellationToken);
        await sut.AddAsync(a3, TestContext.Current.CancellationToken);

        var spec = new CovStubsPredicateSpec<DomainAgent>(a => a.Role.Value == "alpha");

        Assert.Equal(2, (await sut.FindAsync(spec, TestContext.Current.CancellationToken)).Count);
        Assert.Single(await sut.FindAsync(spec, skip: 1, take: 1, TestContext.Current.CancellationToken));
        Assert.Equal(2, await sut.CountAsync(spec, TestContext.Current.CancellationToken));
        Assert.True(await sut.AnyAsync(spec, TestContext.Current.CancellationToken));
        Assert.False(await sut.AnyAsync(new CovStubsPredicateSpec<DomainAgent>(a => a.Role.Value == "gamma"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByIds_ReturnsRequestedSubset()
    {
        var sut = CreateSut();
        var a1 = CreateAgent();
        var a2 = CreateAgent();
        await sut.AddAsync(a1, TestContext.Current.CancellationToken);
        await sut.AddAsync(a2, TestContext.Current.CancellationToken);

        var result = await sut.GetByIdsAsync([a1.Id], TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(a1.Id, result[0].Id);
    }

    [Fact]
    public async Task GetByRole_FiltersByRole()
    {
        var sut = CreateSut();
        var a1 = CreateAgent("writer");
        await sut.AddAsync(a1, TestContext.Current.CancellationToken);
        await sut.AddAsync(CreateAgent("reader"), TestContext.Current.CancellationToken);

        var result = await sut.GetByRoleAsync(DomainAgentRole.From("writer"), TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(a1.Id, result[0].Id);
    }

    [Fact]
    public async Task GetAvailableAgents_ExcludesDeactivated()
    {
        var sut = CreateSut();
        var active = CreateAgent();
        await sut.AddAsync(active, TestContext.Current.CancellationToken);

        var result = await sut.GetAvailableAgentsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(active, result);
    }

    [Fact]
    public async Task GetByStatus_FiltersByStatus()
    {
        var sut = CreateSut();
        var agent = CreateAgent();
        await sut.AddAsync(agent, TestContext.Current.CancellationToken);

        var result = await sut.GetByStatusAsync(agent.Status, TestContext.Current.CancellationToken);

        Assert.Contains(agent, result);
    }

    [Fact]
    public async Task GetAgentsWithTools_ReturnsEmptyWhenNoToolsAssigned()
    {
        var sut = CreateSut();
        await sut.AddAsync(CreateAgent(), TestContext.Current.CancellationToken);

        var result = await sut.GetAgentsWithToolsAsync([ToolId.Create()], TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByCrewId_AlwaysEmpty()
    {
        var sut = CreateSut();
        Assert.Empty(await sut.GetByCrewIdAsync(CrewId.Create(), TestContext.Current.CancellationToken));
    }
}

public sealed class CovStubs_InMemoryCrewRepositoryTests
{
    private readonly CovStubsRecordingUnitOfWork _uow = new();
    private InMemoryCrewRepository CreateSut() => new(_uow);

    private static DomainCrew CreateCrew(ProcessType? type = null)
    {
        var process = type ?? ProcessType.Sequential;
        return process == ProcessType.Hierarchical
            ? DomainCrew.Create("goal", process, managerAgentId: AgentId.Create())
            : DomainCrew.Create("goal", process);
    }

    [Fact]
    public void Constructor_NullUnitOfWork_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryCrewRepository(null!));

    [Fact]
    public async Task AddGetExistsCountDelete_RoundTrips()
    {
        var sut = CreateSut();
        var crew = CreateCrew();

        await sut.AddAsync(crew, TestContext.Current.CancellationToken);
        Assert.Same(crew, await sut.GetByIdAsync(crew.Id, TestContext.Current.CancellationToken));
        Assert.True(await sut.ExistsAsync(crew.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, await sut.CountAsync(TestContext.Current.CancellationToken));

        await sut.UpdateAsync(crew, TestContext.Current.CancellationToken);
        await sut.DeleteAsync(crew.Id, TestContext.Current.CancellationToken);
        Assert.Null(await sut.GetByIdAsync(crew.Id, TestContext.Current.CancellationToken));
        Assert.Contains(crew, _uow.Tracked);
    }

    [Fact]
    public async Task Find_WithSpecification_AndIdsAndStatusAndProcessType()
    {
        var sut = CreateSut();
        var seq = CreateCrew(ProcessType.Sequential);
        var hier = CreateCrew(ProcessType.Hierarchical);
        await sut.AddAsync(seq, TestContext.Current.CancellationToken);
        await sut.AddAsync(hier, TestContext.Current.CancellationToken);

        var spec = new CovStubsPredicateSpec<DomainCrew>(c => c.ProcessType == ProcessType.Sequential);
        Assert.Single(await sut.FindAsync(spec, TestContext.Current.CancellationToken));
        Assert.Single(await sut.FindAsync(spec, 0, 10, TestContext.Current.CancellationToken));
        Assert.Equal(1, await sut.CountAsync(spec, TestContext.Current.CancellationToken));
        Assert.True(await sut.AnyAsync(spec, TestContext.Current.CancellationToken));

        Assert.Single(await sut.GetByIdsAsync([seq.Id], TestContext.Current.CancellationToken));
        Assert.Equal(2, (await sut.GetByStatusAsync(seq.Status, TestContext.Current.CancellationToken)).Count); // both newly-created crews share the Created status
        Assert.Single(await sut.GetByProcessTypeAsync(ProcessType.Hierarchical, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByAgent_FiltersByMembership()
    {
        var sut = CreateSut();
        var crew = CreateCrew();
        var agentId = AgentId.Create();
        crew.AddAgent(agentId);
        await sut.AddAsync(crew, TestContext.Current.CancellationToken);

        Assert.Single(await sut.GetByAgentAsync(agentId, TestContext.Current.CancellationToken));
        Assert.Empty(await sut.GetByAgentAsync(AgentId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByTask_FiltersByMembership()
    {
        var sut = CreateSut();
        var crew = CreateCrew();
        var taskId = TaskId.Create();
        crew.AddTask(taskId);
        await sut.AddAsync(crew, TestContext.Current.CancellationToken);

        Assert.Single(await sut.GetByTaskAsync(taskId, TestContext.Current.CancellationToken));
        Assert.Empty(await sut.GetByTaskAsync(TaskId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetWithRecentExecutions_AlwaysEmpty()
    {
        var sut = CreateSut();
        Assert.Empty(await sut.GetWithRecentExecutionsAsync(DateTime.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetExecutionStatistics_ReturnsZeroedStats()
    {
        var sut = CreateSut();
        var stats = await sut.GetExecutionStatisticsAsync(CrewId.Create(), TestContext.Current.CancellationToken);
        Assert.Equal(0, stats.TotalExecutions);
    }
}

public sealed class CovStubs_InMemoryTaskRepositoryTests
{
    private readonly CovStubsRecordingUnitOfWork _uow = new();
    private InMemoryTaskRepository CreateSut() => new(_uow);

    private static DomainCrewTask CreateTask(string desc = "desc")
        => DomainCrewTask.Create(DomainTaskDescription.From(desc), DomainExpectedOutput.From("out"));

    [Fact]
    public void Constructor_NullUnitOfWork_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryTaskRepository(null!));

    [Fact]
    public async Task Add_ReturnsTaskTracksAndPersists()
    {
        var sut = CreateSut();
        var task = CreateTask();

        var returned = await sut.AddAsync(task, TestContext.Current.CancellationToken);

        Assert.Same(task, returned);
        Assert.Same(task, await sut.GetByIdAsync(task.Id, TestContext.Current.CancellationToken));
        Assert.True(await sut.ExistsAsync(task.Id, TestContext.Current.CancellationToken));
        Assert.Contains(task, _uow.Tracked);
    }

    [Fact]
    public async Task ExplicitIRepositoryAdd_TracksAndPersists()
    {
        var sut = CreateSut();
        var task = CreateTask();

        await ((IRepository<DomainCrewTask, TaskId>)sut).AddAsync(task, TestContext.Current.CancellationToken);

        Assert.True(await sut.ExistsAsync(task.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByIds_ReturnsSubset()
    {
        var sut = CreateSut();
        var t1 = CreateTask("one");
        var t2 = CreateTask("two");
        await sut.AddAsync(t1, TestContext.Current.CancellationToken);
        await sut.AddAsync(t2, TestContext.Current.CancellationToken);

        var result = await sut.GetByIdsAsync([t1.Id], TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(t1.Id, result[0].Id);
    }

    [Fact]
    public async Task GetByStatus_AndGetByPriority_AndDateRange()
    {
        var sut = CreateSut();
        var task = CreateTask();
        await sut.AddAsync(task, TestContext.Current.CancellationToken);

        Assert.Single(await sut.GetByStatusAsync(task.Status, TestContext.Current.CancellationToken));
        Assert.Single(await sut.GetByPriorityAsync(task.Priority, TestContext.Current.CancellationToken));
        Assert.Single(await sut.GetByDateRangeAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), TestContext.Current.CancellationToken));
        Assert.Empty(await sut.GetByDateRangeAsync(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByAgent_FiltersByAssignment()
    {
        var sut = CreateSut();
        var task = CreateTask();
        await sut.AddAsync(task, TestContext.Current.CancellationToken);

        // No agent assigned → not returned for a random agent.
        Assert.Empty(await sut.GetByAgentAsync(AgentId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetReadyTasks_ReturnsPendingWithoutDependencies()
    {
        var sut = CreateSut();
        var task = CreateTask();
        await sut.AddAsync(task, TestContext.Current.CancellationToken);

        var ready = await sut.GetReadyTasksAsync(TestContext.Current.CancellationToken);

        Assert.Contains(task.Id, ready.Select(t => t.Id));
    }

    [Fact]
    public async Task GetDependentTasks_ReturnsEmptyWhenNoDependencies()
    {
        var sut = CreateSut();
        await sut.AddAsync(CreateTask(), TestContext.Current.CancellationToken);

        Assert.Empty(await sut.GetDependentTasksAsync(TaskId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsCompleted_FalseForPendingTask()
    {
        var sut = CreateSut();
        var task = CreateTask();
        await sut.AddAsync(task, TestContext.Current.CancellationToken);

        Assert.False(await sut.IsCompletedAsync(task.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.IsCompletedAsync(TaskId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ReplacesTask()
    {
        var sut = CreateSut();
        var task = CreateTask();
        await sut.AddAsync(task, TestContext.Current.CancellationToken);

        await sut.UpdateAsync(task, TestContext.Current.CancellationToken);

        Assert.Equal(1, await sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_PublicAndExplicit_Remove()
    {
        var sut = CreateSut();
        var t1 = CreateTask("one");
        var t2 = CreateTask("two");
        await sut.AddAsync(t1, TestContext.Current.CancellationToken);
        await sut.AddAsync(t2, TestContext.Current.CancellationToken);

        Assert.True(await sut.DeleteAsync(t1.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.DeleteAsync(t1.Id, TestContext.Current.CancellationToken));

        await ((IRepository<DomainCrewTask, TaskId>)sut).DeleteAsync(t2.Id, TestContext.Current.CancellationToken);
        Assert.Equal(0, await sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Find_WithSpecification_FiltersPagesCountsAny()
    {
        var sut = CreateSut();
        await sut.AddAsync(CreateTask("keep"), TestContext.Current.CancellationToken);
        await sut.AddAsync(CreateTask("drop"), TestContext.Current.CancellationToken);

        var spec = new CovStubsPredicateSpec<DomainCrewTask>(t => t.Description.Value == "keep");

        Assert.Single(await sut.FindAsync(spec, TestContext.Current.CancellationToken));
        Assert.Single(await sut.FindAsync(spec, 0, 5, TestContext.Current.CancellationToken));
        Assert.Equal(1, await sut.CountAsync(spec, TestContext.Current.CancellationToken));
        Assert.True(await sut.AnyAsync(spec, TestContext.Current.CancellationToken));
    }
}

public sealed class CovStubs_InMemoryAgentMemoryStoreRepositoryTests
{
    private readonly CovStubsRecordingUnitOfWork _uow = new();
    private InMemoryAgentMemoryStoreRepository CreateSut() => new(_uow);

    private static AgentMemoryStore CreateStore(AgentId? owner = null)
        => AgentMemoryStore.Create(owner ?? AgentId.Create());

    [Fact]
    public void Constructor_NullUnitOfWork_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryAgentMemoryStoreRepository(null!));

    [Fact]
    public async Task AddGetExistsCountUpdateDelete_RoundTrips()
    {
        var sut = CreateSut();
        var store = CreateStore();

        await sut.AddAsync(store, TestContext.Current.CancellationToken);
        Assert.Same(store, await sut.GetByIdAsync(store.Id, TestContext.Current.CancellationToken));
        Assert.True(await sut.ExistsAsync(store.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, await sut.CountAsync(TestContext.Current.CancellationToken));
        Assert.Contains(store, _uow.Tracked);

        await sut.UpdateAsync(store, TestContext.Current.CancellationToken);
        await sut.DeleteAsync(store.Id, TestContext.Current.CancellationToken);
        Assert.Null(await sut.GetByIdAsync(store.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByAgentId_FindsOwner()
    {
        var sut = CreateSut();
        var owner = AgentId.Create();
        var store = CreateStore(owner);
        await sut.AddAsync(store, TestContext.Current.CancellationToken);

        Assert.Same(store, await sut.GetByAgentIdAsync(owner, TestContext.Current.CancellationToken));
        Assert.Null(await sut.GetByAgentIdAsync(AgentId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Find_WithSpecification_FiltersPagesCountsAny()
    {
        var sut = CreateSut();
        var owner = AgentId.Create();
        var target = CreateStore(owner);
        await sut.AddAsync(target, TestContext.Current.CancellationToken);
        await sut.AddAsync(CreateStore(), TestContext.Current.CancellationToken);

        var spec = new CovStubsPredicateSpec<AgentMemoryStore>(s => s.OwnerAgentId == owner);

        Assert.Single(await sut.FindAsync(spec, TestContext.Current.CancellationToken));
        Assert.Single(await sut.FindAsync(spec, 0, 10, TestContext.Current.CancellationToken));
        Assert.Equal(1, await sut.CountAsync(spec, TestContext.Current.CancellationToken));
        Assert.True(await sut.AnyAsync(spec, TestContext.Current.CancellationToken));
    }
}
