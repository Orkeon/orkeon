using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Application.Agent.Queries.GetAgent;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Services.Handlers;

public class GetAgentHandlerTests
{
    private readonly StubAgentRepository _repository;
    private readonly TestLogger<GetAgentHandler> _logger;
    private readonly GetAgentHandler _sut;

    public GetAgentHandlerTests()
    {
        _repository = new StubAgentRepository();
        _logger = new TestLogger<GetAgentHandler>();
        _sut = new GetAgentHandler(_repository, _logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnAgent_WhenAgentExists()
    {
        // Arrange
        var agent = DomainAgent.Create(
            role: AgentRole.From(RoleAnalyst),
            goal: AgentGoal.From(GoalAnalyzeData),
            backstory: AgentBackstory.From("Expert analyst"));
        _repository.SeedAgent(agent);
        var query = new GetAgentQuery(agent.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(agent.Id.ToString(), result!.Id);
        Assert.Equal(RoleAnalyst, result.Role);
        Assert.Equal(GoalAnalyzeData, result.Goal);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNull_WhenAgentNotFound()
    {
        // Arrange
        var query = new GetAgentQuery(Guid.NewGuid());

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.True(_logger.HasLoggedMessage("not found"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldMapAllProperties_WhenReturningDto()
    {
        // Arrange
        var agent = DomainAgent.Create(
            role: AgentRole.From(RoleDeveloper),
            goal: AgentGoal.From(GoalWriteCode),
            backstory: AgentBackstory.From("Senior dev"),
            allowDelegation: true,
            maxIterations: 15,
            maxRpm: 30,
            verbose: true);
        _repository.SeedAgent(agent);
        var query = new GetAgentQuery(agent.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(RoleDeveloper, result!.Role);
        Assert.Equal(GoalWriteCode, result.Goal);
        Assert.Equal("Senior dev", result.Backstory);
        Assert.True(result.Verbose);
        Assert.True(result.AllowDelegation);
        Assert.NotNull(result.Status);
        Assert.NotEqual(default, result.CreatedAt);
    }

    /// <summary>
    /// Stub repository that returns a pre-seeded agent by ID.
    /// </summary>
    private sealed class StubAgentRepository : IAgentRepository
    {
        private readonly Dictionary<string, DomainAgent?> _agents = [];

        public void SeedAgent(DomainAgent agent) => _agents[agent.Id.ToString()] = agent;

        public System.Threading.Tasks.Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
        {
            _agents.TryGetValue(id.ToString(), out var agent);
            return System.Threading.Tasks.Task.FromResult(agent);
        }

        // Not used by GetAgentHandler but required by interface
        public System.Threading.Tasks.Task AddAsync(DomainAgent aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task UpdateAsync(DomainAgent aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task DeleteAsync(AgentId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<bool> ExistsAsync(AgentId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, int skip, int take, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(IEnumerable<AgentId> ids, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(AgentStatus status, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(IEnumerable<ToolId> toolIds, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(CrewId crewId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
    }
}
