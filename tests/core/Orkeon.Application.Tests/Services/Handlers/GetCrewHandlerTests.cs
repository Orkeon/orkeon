using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Application.Crew.Queries.GetCrew;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using DomainCrewStatus = Orkeon.Domain.Crew.ValueObjects.CrewStatus;
using DomainProcessType = Orkeon.Domain.SharedKernel.ValueObjects.ProcessType;

namespace Orkeon.Application.Tests.Services.Handlers;

public class GetCrewHandlerTests
{
    private readonly StubCrewRepository _repository;
    private readonly TestLogger<GetCrewHandler> _logger;
    private readonly GetCrewHandler _sut;

    public GetCrewHandlerTests()
    {
        _repository = new StubCrewRepository();
        _logger = new TestLogger<GetCrewHandler>();
        _sut = new GetCrewHandler(_repository, _logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnCrew_WhenCrewExists()
    {
        // Arrange
        var crew = DomainCrew.Create("Research AI capabilities", DomainProcessType.Sequential);
        _repository.SeedCrew(crew);
        var query = new GetCrewQuery(crew.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(crew.Id.ToString(), result!.Id);
        Assert.Equal("Research AI capabilities", result.Description);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNull_WhenCrewNotFound()
    {
        // Arrange
        var query = new GetCrewQuery(Guid.NewGuid());

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
        var crew = DomainCrew.Create(
            goal: "Complete analysis",
            processType: DomainProcessType.Parallel,
            verbose: true,
            planning: true);
        _repository.SeedCrew(crew);
        var query = new GetCrewQuery(crew.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Complete analysis", result!.Description);
        Assert.Equal("Parallel", result.ProcessType);
        Assert.Equal("verbose", result.Verbosity); // verbose=true
        Assert.Equal("Idle", result.Status);
        Assert.NotEqual(default, result.CreatedAt);
    }

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Parallel")]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    public async System.Threading.Tasks.Task ShouldMapProcessType_WhenDifferentTypesUsed(string domainProcessTypeStr)
    {
        // Arrange
        var domainProcessType = DomainProcessType.From(domainProcessTypeStr);
        // Hierarchical requires a manager agent ID
        var managerAgentId = domainProcessType == DomainProcessType.Hierarchical
            ? AgentId.Create()
            : null;
        var crew = DomainCrew.Create("Test goal", domainProcessType, managerAgentId: managerAgentId);
        _repository.SeedCrew(crew);
        var query = new GetCrewQuery(crew.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(domainProcessType.Value, result!.ProcessType);
    }

    /// <summary>
    /// Stub repository that returns a pre-seeded crew by ID.
    /// </summary>
    private sealed class StubCrewRepository : ICrewRepository
    {
        private readonly Dictionary<string, DomainCrew?> _crews = [];

        public void SeedCrew(DomainCrew crew) => _crews[crew.Id.ToString()] = crew;

        public System.Threading.Tasks.Task<DomainCrew?> GetByIdAsync(CrewId id, CancellationToken cancellationToken = default)
        {
            _crews.TryGetValue(id.ToString(), out var crew);
            return System.Threading.Tasks.Task.FromResult(crew);
        }

        // Not used by GetCrewHandler but required by interface
        public System.Threading.Tasks.Task AddAsync(DomainCrew aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task UpdateAsync(DomainCrew aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task DeleteAsync(CrewId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<bool> ExistsAsync(CrewId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, int skip, int take, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByIdsAsync(IEnumerable<CrewId> ids, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByStatusAsync(DomainCrewStatus status, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByProcessTypeAsync(DomainProcessType processType, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByTaskAsync(TaskId taskId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetWithRecentExecutionsAsync(DateTime since, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public System.Threading.Tasks.Task<CrewExecutionStatistics> GetExecutionStatisticsAsync(CrewId crewId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(new CrewExecutionStatistics(0, 0, 0, 0, 0, null));
    }
}
