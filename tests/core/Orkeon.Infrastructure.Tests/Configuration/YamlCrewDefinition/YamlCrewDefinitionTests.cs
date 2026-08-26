using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Infrastructure.Tests.Configuration;

public class YamlCrewDefinitionTests
{
    private readonly MockYamlSerializer _yamlSerializer;
    private readonly MockToolRegistry _toolRegistry;
    private readonly FakeFileSystemService _fakeFs;
    private readonly ILogger<YamlCrewDefinitionLoader> _loaderLogger;
    private readonly ILogger<CrewFactory> _factoryLogger;
    private readonly ILogger<YamlCrewExporter> _exporterLogger;
    private readonly YamlCrewDefinitionLoader _loader;
    private readonly ICrewRepository _crewRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ITaskRepository _taskRepository;

    public YamlCrewDefinitionTests()
    {
        _yamlSerializer = new MockYamlSerializer();
        _toolRegistry = new MockToolRegistry();
        _fakeFs = new FakeFileSystemService();
        _loaderLogger = NullLogger<YamlCrewDefinitionLoader>.Instance;
        _factoryLogger = NullLogger<CrewFactory>.Instance;
        _exporterLogger = NullLogger<YamlCrewExporter>.Instance;
        _loader = new YamlCrewDefinitionLoader(_yamlSerializer, _fakeFs, _loaderLogger);
        var unitOfWork = new NullUnitOfWork();
        _crewRepository = new InMemoryCrewRepository(unitOfWork);
        _agentRepository = new InMemoryAgentRepository(unitOfWork);
        _taskRepository = new InMemoryTaskRepository(unitOfWork);
    }

    #region LoadFromStringAsync Tests

    [Fact]
    public async Task ShouldReturnCrewConfiguration_WhenLoadFromStringAsyncValidYaml()
    {
        // Arrange
        var crewYaml = new CrewYamlConfig
        {
            Name = "test-crew",
            Goal = "Test the crew",
            Process = "sequential",
            Agents = new Dictionary<string, AgentYamlConfig>
            {
                ["researcher"] = new AgentYamlConfig
                {
                    Role = "Researcher",
                    Goal = "Research topics",
                    Backstory = "Expert researcher"
                }
            },
            Tasks = new Dictionary<string, TaskYamlConfig>
            {
                ["research_task"] = new TaskYamlConfig
                {
                    Description = "Research AI",
                    ExpectedOutput = "Research report",
                    Agent = "researcher"
                }
            }
        };

        _yamlSerializer.SetDeserializeResult(crewYaml);

        // Act
        var config = await _loader.LoadFromStringAsync("name: test-crew", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("test-crew", config.Name);
        Assert.Equal("Test the crew", config.Goal);
        Assert.Equal(ProcessType.Sequential, config.Process);
        Assert.Single(config.Agents);
        Assert.Single(config.Tasks);
        Assert.Equal("Researcher", config.Agents[0].Role);
        Assert.NotNull(config.Agents[0].Id);
        Assert.NotNull(config.Tasks[0].Id);
        Assert.Equal(config.Agents[0].Id, config.Tasks[0].AssignedAgentId);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenLoadFromStringAsyncEmptyYaml()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _loader.LoadFromStringAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenLoadFromStringAsyncNullYaml()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _loader.LoadFromStringAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldSetProcessType_WhenLoadFromStringAsyncHierarchicalProcess()
    {
        // Arrange
        var crewYaml = new CrewYamlConfig
        {
            Name = "hierarchical-crew",
            Goal = "Test hierarchical",
            Process = "hierarchical",
            ManagerAgent = "manager",
            Agents = new Dictionary<string, AgentYamlConfig>
            {
                ["manager"] = new AgentYamlConfig { Role = RoleManager, Goal = "Manage team" }
            },
            Tasks = new Dictionary<string, TaskYamlConfig>
            {
                ["task1"] = new TaskYamlConfig { Description = "Do work", ExpectedOutput = "Work done" }
            }
        };

        _yamlSerializer.SetDeserializeResult(crewYaml);

        // Act
        var config = await _loader.LoadFromStringAsync("yaml", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ProcessType.Hierarchical, config.Process);
        Assert.NotNull(config.ManagerAgentId);
        // Manager should reference the agent with Role "Manager"
        var manager = config.Agents.FirstOrDefault(a => a.Role == RoleManager);
        Assert.NotNull(manager);
        Assert.Equal(manager.Id, config.ManagerAgentId);
    }

    [Fact]
    public async Task ShouldMapLlmSettings_WhenLoadFromStringAsyncAgentWithLlmConfig()
    {
        // Arrange
        var crewYaml = new CrewYamlConfig
        {
            Name = "crew",
            Goal = "Test",
            Agents = new Dictionary<string, AgentYamlConfig>
            {
                ["agent1"] = new AgentYamlConfig
                {
                    Role = "Agent",
                    Goal = "Test",
                    Llm = new LlmYamlConfig
                    {
                        Model = ModelGpt4o,
                        Temperature = 0.5,
                        MaxTokens = 2048
                    }
                }
            },
            Tasks = []
        };

        _yamlSerializer.SetDeserializeResult(crewYaml);

        // Act
        var config = await _loader.LoadFromStringAsync("yaml", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(config.Agents[0].LlmConfig);
        Assert.Equal(ModelGpt4o, config.Agents[0].LlmConfig!.Model);
        Assert.Equal(0.5, config.Agents[0].LlmConfig!.Temperature);
        Assert.Equal(2048, config.Agents[0].LlmConfig!.MaxTokens);
    }

    [Fact]
    public async Task ShouldMapCorrectly_WhenLoadFromStringAsyncAgentWithToolsAndSettings()
    {
        // Arrange
        var crewYaml = new CrewYamlConfig
        {
            Name = "crew",
            Goal = "Test",
            Agents = new Dictionary<string, AgentYamlConfig>
            {
                ["agent1"] = new AgentYamlConfig
                {
                    Role = RoleDeveloper,
                    Goal = GoalWriteCode,
                    Tools = ["file_read", "web_search"],
                    AllowDelegation = false,
                    MaxIter = 30,
                    MaxRpm = 20,
                    Verbose = true
                }
            },
            Tasks = []
        };

        _yamlSerializer.SetDeserializeResult(crewYaml);

        // Act
        var config = await _loader.LoadFromStringAsync("yaml", TestContext.Current.CancellationToken);

        // Assert
        var agent = config.Agents[0];
        Assert.Equal(2, agent.Tools.Count);
        Assert.Contains("file_read", agent.Tools);
        Assert.Contains("web_search", agent.Tools);
        Assert.False(agent.AllowDelegation);
        Assert.Equal(30, agent.MaxIterations);
        Assert.Equal(20, agent.MaxRPM);
        Assert.True(agent.Verbose);
    }

    [Fact]
    public async Task ShouldMapDependencies_WhenLoadFromStringAsyncTaskWithDependencies()
    {
        // Arrange
        var crewYaml = new CrewYamlConfig
        {
            Name = "crew",
            Goal = "Test",
            Agents = [],
            Tasks = new Dictionary<string, TaskYamlConfig>
            {
                ["task1"] = new TaskYamlConfig
                {
                    Description = "First task",
                    ExpectedOutput = "Output 1"
                },
                ["task2"] = new TaskYamlConfig
                {
                    Description = "Second task",
                    ExpectedOutput = "Output 2",
                    Dependencies = ["task1"],
                    AsyncExecution = true,
                    HumanInput = true
                }
            }
        };

        _yamlSerializer.SetDeserializeResult(crewYaml);

        // Act
        var config = await _loader.LoadFromStringAsync("yaml", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, config.Tasks.Count);
        var task1 = config.Tasks.First(t => t.Description == "First task");
        var task2 = config.Tasks.First(t => t.Description == "Second task");
        Assert.Single(task2.Dependencies);
        Assert.Equal(task1.Id, task2.Dependencies[0]);
        Assert.True(task2.AsyncExecution);
        Assert.True(task2.HumanInput);
    }

    /// <summary>
    /// An unknown <c>process:</c> is a typo, and a typo is refused with the list of what was
    /// expected.
    /// <para>
    /// It used to fall through to Sequential, silently — so <c>process: graf</c> ran a
    /// pipeline the author never asked for, and the author's only clue was that the crew
    /// behaved oddly. This very test asserted the fallback, which is how it survived: the
    /// neighbouring <c>deliverable:</c> parser refuses an unknown value, and the scripting
    /// authoring path refuses one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ShouldRefuseAnUnknownProcessType_AndNameTheValidOnes()
    {
        var crewYaml = new CrewYamlConfig
        {
            Name = "crew",
            Goal = "Test",
            Process = "unknown_process",
            Agents = [],
            Tasks = []
        };
        _yamlSerializer.SetDeserializeResult(crewYaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _loader.LoadFromStringAsync("yaml", TestContext.Current.CancellationToken));

        Assert.Contains("unknown_process", ex.Message, StringComparison.Ordinal);
        foreach (var process in ProcessType.All)
            Assert.Contains(process.Value, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldDefaultToSequential_WhenLoadFromStringAsyncNullProcessType()
    {
        var crewYaml = new CrewYamlConfig
        {
            Name = "crew",
            Goal = "Test",
            Process = null,
            Agents = [],
            Tasks = []
        };
        _yamlSerializer.SetDeserializeResult(crewYaml);

        var config = await _loader.LoadFromStringAsync("yaml", TestContext.Current.CancellationToken);
        Assert.Equal(ProcessType.Sequential, config.Process);
    }

    #endregion

    #region LoadFromFileAsync Tests

    [Fact]
    public async Task ShouldThrowFileNotFoundException_WhenLoadFromFileAsyncFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _loader.LoadFromFileAsync("/nonexistent/path/crew.yaml", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenLoadFromFileAsyncEmptyPath()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _loader.LoadFromFileAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldLoadConfiguration_WhenLoadFromFileAsyncValidFile()
    {
        // Arrange
        var virtualPath = "/crews/file-crew.yaml";
        _fakeFs.AddFile(virtualPath, "name: file-crew");

        var crewYaml = new CrewYamlConfig
        {
            Name = "file-crew",
            Goal = "Test from file",
            Agents = new Dictionary<string, AgentYamlConfig>
            {
                ["agent1"] = new AgentYamlConfig { Role = "Agent", Goal = "Do work" }
            },
            Tasks = new Dictionary<string, TaskYamlConfig>
            {
                ["task1"] = new TaskYamlConfig { Description = "Work", ExpectedOutput = "Done" }
            }
        };

        _yamlSerializer.SetDeserializeResult(crewYaml);

        // Act
        var config = await _loader.LoadFromFileAsync(virtualPath, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("file-crew", config.Name);
        Assert.Equal("Test from file", config.Goal);
    }

    #endregion

    #region LoadFromDirectoryAsync Tests

    [Fact]
    public async Task ShouldThrowDirectoryNotFoundException_WhenLoadFromDirectoryAsyncDirectoryNotFound()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _loader.LoadFromDirectoryAsync("/nonexistent/directory", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowFileNotFoundException_WhenLoadFromDirectoryAsyncMissingCrewYaml()
    {
        // Directory exists but crew.yaml is absent
        var fs = new FakeFileSystemService();
        fs.AddDirectory("/crews/empty-dir");
        var loader = new YamlCrewDefinitionLoader(_yamlSerializer, fs, _loaderLogger);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => loader.LoadFromDirectoryAsync("/crews/empty-dir", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldLoadMultiFileConfig_WhenLoadFromDirectoryAsyncValidDirectory()
    {
        // Arrange
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/my-crew/crew.yaml", "name: dir-crew");
        fs.AddFile("/crews/my-crew/agents.yaml", "researcher:\n  role: Researcher");
        fs.AddFile("/crews/my-crew/tasks.yaml", "research:\n  description: Research");

        var crewSettings = new CrewSettingsYamlConfig
        {
            Name = "dir-crew",
            Goal = "Directory test",
            Process = "sequential"
        };

        var agents = new Dictionary<string, AgentYamlConfig>
        {
            ["researcher"] = new AgentYamlConfig
            {
                Role = "Researcher",
                Goal = "Research things"
            }
        };

        var tasks = new Dictionary<string, TaskYamlConfig>
        {
            ["research"] = new TaskYamlConfig
            {
                Description = "Do research",
                ExpectedOutput = "Report",
                Agent = "researcher"
            }
        };

        _yamlSerializer.SetDeserializeResult(crewSettings);
        _yamlSerializer.SetDeserializeResult(agents);
        _yamlSerializer.SetDeserializeResult(tasks);

        var loader = new YamlCrewDefinitionLoader(_yamlSerializer, fs, _loaderLogger);

        // Act
        var config = await loader.LoadFromDirectoryAsync("/crews/my-crew", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("dir-crew", config.Name);
        Assert.Equal("Directory test", config.Goal);
        Assert.Single(config.Agents);
        Assert.Single(config.Tasks);
        Assert.NotNull(config.Agents[0].Id);
        Assert.NotNull(config.Tasks[0].Id);
        Assert.Equal(config.Agents[0].Id, config.Tasks[0].AssignedAgentId);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void ShouldReturnIsValid_WhenValidateValidConfig()
    {
        var config = CreateValidConfig();
        var result = _loader.Validate(config);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldReturnError_WhenValidateEmptyName()
    {
        var config = CreateValidConfig() with { Name = "" };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateEmptyGoal()
    {
        var config = CreateValidConfig() with { Goal = "" };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("goal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateNoAgents()
    {
        var config = CreateValidConfig() with { Agents = Array.Empty<AgentConfiguration>() };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("agent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateNoTasks()
    {
        var config = CreateValidConfig() with { Tasks = Array.Empty<TaskConfiguration>() };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("task", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateTaskReferencesUnknownAgent()
    {
        var config = CreateValidConfig();
        config = config with { Tasks = [config.Tasks[0] with { AssignedAgentId = AgentId.Create() }] };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown agent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateTaskReferencesUnknownDependency()
    {
        var config = CreateValidConfig();
        config = config with { Tasks = [config.Tasks[0] with { Dependencies = [TaskId.Create()] }] };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown dependency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateCircularDependencies()
    {
        var config = CreateValidConfig();
        var task2Id = TaskId.Create();
        var task1WithDep = config.Tasks[0] with { Dependencies = [task2Id] };
        config = config with
        {
            Tasks =
            [
                task1WithDep,
                new TaskConfiguration { Id = task2Id, Description = "Second task", ExpectedOutput = "Output", Dependencies = [config.Tasks[0].Id] }
            ]
        };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("circular", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnWarning_WhenValidateHierarchicalWithoutManager()
    {
        var config = CreateValidConfig() with { Process = ProcessType.Hierarchical, ManagerAgentId = null };
        var result = _loader.Validate(config);
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("manager", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateHierarchicalWithInvalidManager()
    {
        var config = CreateValidConfig() with { Process = ProcessType.Hierarchical, ManagerAgentId = AgentId.Create() };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("manager", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateAgentWithoutRole()
    {
        var config = CreateValidConfig();
        config = config with { Agents = [config.Agents[0] with { Role = "" }] };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateAgentWithoutGoal()
    {
        var config = CreateValidConfig();
        config = config with { Agents = [config.Agents[0] with { Goal = "" }] };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("goal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateTaskWithoutDescription()
    {
        var config = CreateValidConfig();
        config = config with { Tasks = [config.Tasks[0] with { Description = "" }] };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("description", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldReturnError_WhenValidateTaskWithoutExpectedOutput()
    {
        var config = CreateValidConfig();
        config = config with { Tasks = [config.Tasks[0] with { ExpectedOutput = "" }] };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("expected output", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenValidateNullConfig()
    {
        Assert.Throws<ArgumentNullException>(() => _loader.Validate(null!));
    }

    [Fact]
    public void ShouldReturnAllErrors_WhenValidateMultipleErrors()
    {
        var config = new CrewConfiguration
        {
            Name = "",
            Goal = "",
            Agents = [],
            Tasks = []
        };
        var result = _loader.Validate(config);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 4);
    }

    #endregion

    #region CrewFactory Tests

    [Fact]
    public async Task ShouldCreateCrew_WhenCrewFactoryValidConfig()
    {
        // Arrange
        var config = CreateValidConfig();
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));

        var factory = new CrewFactory(loaderMock, _toolRegistry, _factoryLogger, _crewRepository, _agentRepository, _taskRepository);

        // Act
        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(crew);
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
        Assert.Single(crew.Agents);
        Assert.Single(crew.Tasks);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperationException_WhenCrewFactoryInvalidConfig()
    {
        var config = new CrewConfiguration
        {
            Name = "",
            Goal = "test",
            Agents = [],
            Tasks = []
        };

        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateErrors("Name is required");

        var factory = new CrewFactory(loaderMock, _toolRegistry, _factoryLogger, _crewRepository, _agentRepository, _taskRepository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenCrewFactoryNullConfig()
    {
        var loaderMock = new MockCrewDefinitionLoader();
        var factory = new CrewFactory(loaderMock, _toolRegistry, _factoryLogger, _crewRepository, _agentRepository, _taskRepository);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => factory.CreateFromConfigAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldCreateAll_WhenCrewFactoryConfigWithMultipleAgentsAndTasks()
    {
        var config = CreateValidConfig();
        var writerId = AgentId.Create();
        config = config with
        {
            Agents = config.Agents.Concat(
            [
                new AgentConfiguration
                {
                    Id = writerId, Role = "Writer", Goal = "Write content", Backstory = "Experienced writer"
                }
            ]).ToArray(),
            Tasks = config.Tasks.Concat(
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(), Description = "Write an article", ExpectedOutput = "Article", AssignedAgentId = writerId
                }
            ]).ToArray()
        };

        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));

        var factory = new CrewFactory(loaderMock, _toolRegistry, _factoryLogger, _crewRepository, _agentRepository, _taskRepository);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(2, crew.Agents.Count);
        Assert.Equal(2, crew.Tasks.Count);
    }

    [Fact]
    public async Task ShouldLoadAndCreates_WhenCrewFactoryCreateFromFileAsync()
    {
        var config = CreateValidConfig();
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetLoadResult(config);
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));

        var factory = new CrewFactory(loaderMock, _toolRegistry, _factoryLogger, _crewRepository, _agentRepository, _taskRepository);

        var crew = await factory.CreateFromFileAsync("/path/to/crew.yaml", TestContext.Current.CancellationToken);

        Assert.NotNull(crew);
        Assert.Equal(1, loaderMock.LoadFromFileCallCount);
        Assert.Equal("/path/to/crew.yaml", loaderMock.LastLoadedFilePath);
    }

    [Fact]
    public async Task ShouldLoadAndCreates_WhenCrewFactoryCreateFromDirectoryAsync()
    {
        var config = CreateValidConfig();
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetLoadResult(config);
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));

        var factory = new CrewFactory(loaderMock, _toolRegistry, _factoryLogger, _crewRepository, _agentRepository, _taskRepository);

        var crew = await factory.CreateFromDirectoryAsync("/path/to/crew-dir", TestContext.Current.CancellationToken);

        Assert.NotNull(crew);
        Assert.Equal(1, loaderMock.LoadFromDirectoryCallCount);
        Assert.Equal("/path/to/crew-dir", loaderMock.LastLoadedDirectoryPath);
    }

    [Fact]
    public async Task ShouldSetCrewProperties_WhenCrewFactoryConfigWithVerboseAndMemory()
    {
        var config = CreateValidConfig() with { Verbose = true, Memory = true, Planning = true };

        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));

        var factory = new CrewFactory(loaderMock, _toolRegistry, _factoryLogger, _crewRepository, _agentRepository, _taskRepository);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.True(crew.Verbose);
        Assert.True(crew.MemoryEnabled);
        Assert.True(crew.Planning);
    }

    #endregion

    #region YamlCrewExporter Tests

    [Fact]
    public void ShouldSerializeConfig_WhenYamlCrewExporterExportToString()
    {
        var config = CreateValidConfig();
        _yamlSerializer.SetSerializeResult("name: test-crew");

        var exporter = new YamlCrewExporter(_yamlSerializer, _fakeFs, _exporterLogger);

        var yaml = exporter.ExportToString(config);

        Assert.Equal("name: test-crew", yaml);
        Assert.Equal(1, _yamlSerializer.SerializeCallCount);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenYamlCrewExporterExportToStringNullConfig()
    {
        var exporter = new YamlCrewExporter(_yamlSerializer, _fakeFs, _exporterLogger);
        Assert.Throws<ArgumentNullException>(() => exporter.ExportToString(null!));
    }

    [Fact]
    public async Task ShouldWriteFile_WhenYamlCrewExporterExportToFileAsync()
    {
        var config = CreateValidConfig();
        _yamlSerializer.SetSerializeResult("name: exported-crew");

        var fs = new FakeFileSystemService();
        var exporter = new YamlCrewExporter(_yamlSerializer, fs, _exporterLogger);

        await exporter.ExportToFileAsync(config, "/output/crew.yaml", TestContext.Current.CancellationToken);

        var content = await fs.TryReadAllTextAsync("/output/crew.yaml", CancellationToken.None);
        Assert.Equal("name: exported-crew", content);
    }

    [Fact]
    public async Task ShouldWriteThreeFiles_WhenYamlCrewExporterExportToDirectoryAsync()
    {
        var config = CreateValidConfig();
        _yamlSerializer.SetSerializeResult("content");

        var fs = new FakeFileSystemService();
        var exporter = new YamlCrewExporter(_yamlSerializer, fs, _exporterLogger);

        await exporter.ExportToDirectoryAsync(config, "/output/crew-dir", TestContext.Current.CancellationToken);

        Assert.NotNull(await fs.TryReadAllTextAsync("/output/crew-dir/crew.yaml", CancellationToken.None));
        Assert.NotNull(await fs.TryReadAllTextAsync("/output/crew-dir/agents.yaml", CancellationToken.None));
        Assert.NotNull(await fs.TryReadAllTextAsync("/output/crew-dir/tasks.yaml", CancellationToken.None));
    }

    [Fact]
    public async Task ShouldWriteThreeFiles_WhenYamlCrewExporterExportToDirectoryCreatesParentsImplicitly()
    {
        // VFS creates parent directories without explicit Directory.CreateDirectory
        var config = CreateValidConfig();
        _yamlSerializer.SetSerializeResult("content");

        var fs = new FakeFileSystemService();
        var exporter = new YamlCrewExporter(_yamlSerializer, fs, _exporterLogger);

        // No pre-existing directory — VFS must create parents automatically
        await exporter.ExportToDirectoryAsync(config, "/deep/nested/dir", TestContext.Current.CancellationToken);

        Assert.NotNull(await fs.TryReadAllTextAsync("/deep/nested/dir/crew.yaml", CancellationToken.None));
        Assert.NotNull(await fs.TryReadAllTextAsync("/deep/nested/dir/agents.yaml", CancellationToken.None));
        Assert.NotNull(await fs.TryReadAllTextAsync("/deep/nested/dir/tasks.yaml", CancellationToken.None));
    }

    [Fact]
    public async Task ShouldProduceEquivalentConfig_WhenYamlCrewExporterRoundTrip()
    {
        // Use real serializer for round-trip
        var realSerializer = new Orkeon.Infrastructure.Serialization.YamlDotNetSerializer();
        var loader = new YamlCrewDefinitionLoader(realSerializer, _fakeFs, _loaderLogger);
        var exporter = new YamlCrewExporter(realSerializer, _fakeFs, _exporterLogger);

        var baseConfig = CreateValidConfig();
        var originalConfig = baseConfig with
        {
            Agents = [baseConfig.Agents[0] with { Verbose = true }],
            Tasks = [baseConfig.Tasks[0] with { AsyncExecution = true }]
        };

        var yaml = exporter.ExportToString(originalConfig);
        var reloadedConfig = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(originalConfig.Name, reloadedConfig.Name);
        Assert.Equal(originalConfig.Goal, reloadedConfig.Goal);
        Assert.Equal(originalConfig.Process, reloadedConfig.Process);
        Assert.Equal(originalConfig.Agents.Count, reloadedConfig.Agents.Count);
        Assert.Equal(originalConfig.Tasks.Count, reloadedConfig.Tasks.Count);
        Assert.NotNull(reloadedConfig.Agents[0].Id);
        Assert.Equal(originalConfig.Agents[0].Role, reloadedConfig.Agents[0].Role);
        Assert.Equal(originalConfig.Agents[0].Goal, reloadedConfig.Agents[0].Goal);
        Assert.NotNull(reloadedConfig.Tasks[0].Id);
        Assert.Equal(originalConfig.Tasks[0].Description, reloadedConfig.Tasks[0].Description);
        Assert.Equal(originalConfig.Tasks[0].ExpectedOutput, reloadedConfig.Tasks[0].ExpectedOutput);
    }

    #endregion

    #region DI Tests

    [Fact]
    public void ShouldRegisterCrewDefinitionLoader_WhenAddOrkeonYaml()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IYamlSerializer>(new Orkeon.Infrastructure.Serialization.YamlDotNetSerializer());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());

        services.AddOrkeonYaml();
        var sp = services.BuildServiceProvider();

        var loader = sp.GetService<ICrewDefinitionLoader>();
        Assert.NotNull(loader);
        Assert.IsType<YamlCrewDefinitionLoader>(loader);
    }

    [Fact]
    public void ShouldRegisterYamlCrewExporter_WhenAddOrkeonYaml()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IYamlSerializer>(new Orkeon.Infrastructure.Serialization.YamlDotNetSerializer());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());

        services.AddOrkeonYaml();
        var sp = services.BuildServiceProvider();

        var exporter = sp.GetService<YamlCrewExporter>();
        Assert.NotNull(exporter);
    }

    [Fact]
    public void ShouldRegisterCrewFactory_WhenAddOrkeonYaml()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IYamlSerializer>(new Orkeon.Infrastructure.Serialization.YamlDotNetSerializer());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IToolRegistry>(_toolRegistry);
        var unitOfWork = new NullUnitOfWork();
        services.AddScoped<ICrewRepository>(_ => new InMemoryCrewRepository(unitOfWork));
        services.AddScoped<IAgentRepository>(_ => new InMemoryAgentRepository(unitOfWork));
        services.AddScoped<ITaskRepository>(_ => new InMemoryTaskRepository(unitOfWork));

        services.AddOrkeonYaml();
        var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var factory = scope.ServiceProvider.GetService<ICrewFactory>();
        Assert.NotNull(factory);
        Assert.IsType<CrewFactory>(factory);
    }

    [Fact]
    public void ShouldBeSingleton_WhenAddOrkeonYamlCrewDefinitionLoader()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IYamlSerializer>(new Orkeon.Infrastructure.Serialization.YamlDotNetSerializer());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());

        services.AddOrkeonYaml();
        var sp = services.BuildServiceProvider();

        var loader1 = sp.GetService<ICrewDefinitionLoader>();
        var loader2 = sp.GetService<ICrewDefinitionLoader>();
        Assert.Same(loader1, loader2);
    }

    #endregion

    #region Helpers

    private static CrewConfiguration CreateValidConfig()
    {
        var researcherId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "test-crew",
            Goal = "Test the crew system",
            Process = ProcessType.Sequential,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = researcherId,
                    Role = "Researcher",
                    Goal = "Research topics thoroughly",
                    Backstory = "Expert researcher with 10 years of experience"
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Research the topic of AI",
                    ExpectedOutput = "A comprehensive research report",
                    AssignedAgentId = researcherId
                }
            ]
        };
    }

    #endregion
}
