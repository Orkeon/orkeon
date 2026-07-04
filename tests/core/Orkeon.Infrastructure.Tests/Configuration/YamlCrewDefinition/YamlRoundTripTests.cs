using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Comprehensive round-trip tests for YAML configuration: export -> import fidelity.
/// Uses real YamlDotNetSerializer (no mocks) to validate actual serialization behavior.
///
/// Note: The loader generates new ULID-based IDs on each load, so round-trip comparisons
/// match agents by Role and tasks by Description rather than by Id.
/// </summary>
public class YamlRoundTripTests
{
    private readonly YamlDotNetSerializer _serializer;
    private readonly FakeFileSystemService _fakeFs;
    private readonly YamlCrewDefinitionLoader _loader;
    private readonly YamlCrewExporter _exporter;

    public YamlRoundTripTests()
    {
        _serializer = new YamlDotNetSerializer();
        _fakeFs = new FakeFileSystemService();
        _loader = new YamlCrewDefinitionLoader(
            _serializer,
            _fakeFs,
            NullLogger<YamlCrewDefinitionLoader>.Instance);
        _exporter = new YamlCrewExporter(
            _serializer,
            _fakeFs,
            NullLogger<YamlCrewExporter>.Instance);
    }

    #region Basic Round-Trip Tests

    [Fact]
    public async Task RoundTrip_SimpleCrewConfig_PreservesAllFields()
    {
        // Arrange
        var analystId = AgentId.Create();
        var original = new CrewConfiguration
        {
            Name = "simple-crew",
            Goal = "Accomplish a simple goal",
            Process = ProcessType.Sequential,
            Verbose = true,
            Memory = true,
            Planning = true,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = analystId,
                    Role = RoleDataAnalyst,
                    Goal = "Analyze data with precision",
                    Backstory = "A seasoned data analyst with expertise in statistics",
                    AllowDelegation = false,
                    Verbose = true,
                    MaxIterations = 15,
                    MaxRPM = 5,
                    Tools = ["file_read", "data_query"],
                    LlmConfig = LlmConfig.Default() with {
                        Model = ModelGpt4o,
                        Temperature = 0.3,
                        MaxTokens = 8192
                    }
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Analyze the provided dataset",
                    ExpectedOutput = "A detailed analysis report",
                    AssignedAgentId = analystId,
                    AsyncExecution = true,
                    HumanInput = true
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_MultiAgentCrewConfig_PreservesAgentDetails()
    {
        // Arrange
        var managerId = AgentId.Create();
        var developerId = AgentId.Create();
        var testerId = AgentId.Create();
        var original = new CrewConfiguration
        {
            Name = "multi-agent-crew",
            Goal = "Build and deploy a web application",
            Process = ProcessType.Hierarchical,
            ManagerAgentId = managerId,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = managerId,
                    Role = "Project Manager",
                    Goal = "Coordinate team efforts",
                    Backstory = "Experienced PM who has led many projects",
                    AllowDelegation = true,
                    Verbose = true,
                    MaxIterations = 25,
                    MaxRPM = 15,
                    Tools = ["task_tracker"]
                },
                new AgentConfiguration
                {
                    Id = developerId,
                    Role = "Full Stack Developer",
                    Goal = "Write clean and efficient code",
                    Backstory = "Senior developer with 8 years of experience",
                    AllowDelegation = false,
                    Verbose = false,
                    MaxIterations = 30,
                    MaxRPM = 20,
                    Tools = ["file_read", "file_write", "code_execute"]
                },
                new AgentConfiguration
                {
                    Id = testerId,
                    Role = "QA Engineer",
                    Goal = "Ensure software quality",
                    Backstory = "Quality assurance specialist",
                    AllowDelegation = false,
                    Verbose = false,
                    MaxIterations = 10,
                    MaxRPM = 8,
                    Tools = ["test_runner"]
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Design the application architecture",
                    ExpectedOutput = "Architecture document",
                    AssignedAgentId = managerId
                },
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Implement the application",
                    ExpectedOutput = "Working codebase",
                    AssignedAgentId = developerId
                },
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Test the application thoroughly",
                    ExpectedOutput = "Test report with results",
                    AssignedAgentId = testerId
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_TasksWithDependencies_PreservesTaskGraph()
    {
        // Arrange
        var workerId = AgentId.Create();
        var step1Id = TaskId.Create();
        var step2Id = TaskId.Create();
        var step3Id = TaskId.Create();
        var original = new CrewConfiguration
        {
            Name = "pipeline-crew",
            Goal = "Execute a multi-step pipeline",
            Process = ProcessType.Sequential,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = workerId,
                    Role = "Pipeline Worker",
                    Goal = "Execute pipeline steps"
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = step1Id,
                    Description = "First step: gather data",
                    ExpectedOutput = "Raw data collected",
                    AssignedAgentId = workerId
                },
                new TaskConfiguration
                {
                    Id = step2Id,
                    Description = "Second step: process data",
                    ExpectedOutput = "Processed data",
                    AssignedAgentId = workerId,
                    Dependencies = [step1Id]
                },
                new TaskConfiguration
                {
                    Id = step3Id,
                    Description = "Third step: generate report",
                    ExpectedOutput = "Final report",
                    AssignedAgentId = workerId,
                    Dependencies = [step1Id, step2Id]
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_LlmConfiguration_PreservesAllSettings()
    {
        // Arrange
        var customLlmAgentId = AgentId.Create();
        var original = new CrewConfiguration
        {
            Name = "llm-config-crew",
            Goal = "Test LLM configuration round-trip",
            Agents =
            [
                new AgentConfiguration
                {
                    Id = customLlmAgentId,
                    Role = "Custom LLM Agent",
                    Goal = "Use specific LLM settings",
                    Backstory = "Agent with fine-tuned LLM parameters",
                    LlmConfig = LlmConfig.Default() with {
                        Model = "gpt-4-turbo-preview",
                        Temperature = 0.1,
                        MaxTokens = 16384
                    }
                },
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Default LLM Agent",
                    Goal = "Use default LLM settings",
                    Backstory = "Agent with default parameters",
                    LlmConfig = LlmConfig.Default() with {
                        Model = ModelGpt35Turbo,
                        // Temperature=0.7 and MaxTokens=4096 are defaults;
                        // the exporter omits them, loader restores defaults
                        Temperature = 0.7,
                        MaxTokens = 4096
                    }
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Use the LLM",
                    ExpectedOutput = "LLM output",
                    AssignedAgentId = customLlmAgentId
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_ToolReferences_PreservedPerAgent()
    {
        // Arrange
        var original = new CrewConfiguration
        {
            Name = "tools-crew",
            Goal = "Test tool references per agent",
            Agents =
            [
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "File Handler",
                    Goal = "Handle file operations",
                    Tools = ["file_read", "file_write", "directory_list"]
                },
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Web Scraper",
                    Goal = "Scrape web content",
                    Tools = ["web_scrape", "http_api"]
                },
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Thinker",
                    Goal = "Think without tools",
                    Tools = [] // empty tool list
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Use tools",
                    ExpectedOutput = "Tool output"
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task RoundTrip_UnicodeContent_PreservesCharacters()
    {
        // Arrange
        var original = new CrewConfiguration
        {
            Name = "unicode-crew",
            Goal = "Test unicode: accents, CJK, and more",
            Agents =
            [
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Chercheur multilingue",
                    Goal = "Rechercher des informations en plusieurs langues",
                    Backstory = "Expert linguiste parlant le francais, l'allemand (Umlaute: ae, oe, ue), le japonais et le chinois (zhongwen)"
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Translate content across languages: francais, Deutsch, ri ben yu",
                    ExpectedOutput = "Translated documents with proper character encoding"
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_SpecialYamlChars_PreservesContent()
    {
        // Arrange: descriptions with characters that are special in YAML
        var original = new CrewConfiguration
        {
            Name = "special-chars-crew",
            Goal = "Test YAML special characters handling",
            Agents =
            [
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Parser Agent",
                    Goal = "Parse content with special characters: colons, hashes, and brackets",
                    Backstory = "Agent that handles tricky content: {key: value}, [item1, item2], # not a comment"
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Process data with special chars: key: value, list: [a, b, c], comment: # note",
                    ExpectedOutput = "Output containing 'single quotes' and \"double quotes\" preserved"
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_EmptyOptionalFields_HandledGracefully()
    {
        // Arrange: minimal configuration with only required-ish fields
        var original = new CrewConfiguration
        {
            Name = "minimal-crew",
            Goal = "Minimal configuration",
            Agents =
            [
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Basic Agent",
                    Goal = "Do basic work",
                    // Backstory intentionally left as empty string
                    Backstory = "",
                    // Tools left empty
                    Tools = [],
                    // LlmConfig left null
                    LlmConfig = null
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "A basic task",
                    ExpectedOutput = "Basic output",
                    // No agent assigned
                    AssignedAgentId = null,
                    // No dependencies
                    Dependencies = [],
                    // No context
                    Context = []
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_DefaultValues_PreservedOrOmitted()
    {
        // Arrange: configuration where all values are at their defaults
        var original = new CrewConfiguration
        {
            Name = "defaults-crew",
            Goal = "Test that default values survive round-trip",
            Process = ProcessType.Sequential,  // default
            Verbose = false,                    // default
            Memory = false,                     // default
            Planning = false,                   // default
            ManagerAgentId = null,              // default
            Agents =
            [
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Default Agent",
                    Goal = "Work with defaults",
                    Backstory = "An agent using all default values",
                    AllowDelegation = true,   // default
                    MaxIterations = 20,       // default
                    MaxRPM = 10,              // default
                    Verbose = false,          // default
                    LlmConfig = null          // no LLM config
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "A task with all defaults",
                    ExpectedOutput = "Default output",
                    AsyncExecution = false,   // default
                    HumanInput = false        // default
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    #endregion

    #region Idempotence Test

    [Fact]
    public async Task RoundTrip_MultipleIterations_IsIdempotent()
    {
        // Arrange: a rich configuration to test through 2 full cycles
        var leadId = AgentId.Create();
        var workerId = AgentId.Create();
        var planTaskId = TaskId.Create();
        var original = new CrewConfiguration
        {
            Name = "idempotent-crew",
            Goal = "Test that multiple export/import cycles produce identical results",
            Process = ProcessType.Hierarchical,
            Verbose = true,
            Memory = true,
            Planning = true,
            ManagerAgentId = leadId,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = leadId,
                    Role = "Team Lead",
                    Goal = "Lead the team effectively",
                    Backstory = "Experienced team leader with strong communication skills",
                    AllowDelegation = true,
                    Verbose = true,
                    MaxIterations = 25,
                    MaxRPM = 15,
                    Tools = ["task_tracker", "communicator"],
                    LlmConfig = LlmConfig.Default() with {
                        Model = ModelGpt4o,
                        Temperature = 0.5,
                        MaxTokens = 8192
                    }
                },
                new AgentConfiguration
                {
                    Id = workerId,
                    Role = RoleWorker,
                    Goal = "Execute assigned tasks",
                    Backstory = "Diligent worker",
                    AllowDelegation = false,
                    Verbose = false,
                    MaxIterations = 30,
                    MaxRPM = 20,
                    Tools = ["file_read", "file_write"],
                    LlmConfig = LlmConfig.Default() with {
                        Model = ModelGpt35Turbo,
                        Temperature = 0.2,
                        MaxTokens = 2048
                    }
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = planTaskId,
                    Description = "Create an execution plan",
                    ExpectedOutput = "Detailed plan document",
                    AssignedAgentId = leadId,
                    AsyncExecution = false,
                    HumanInput = true
                },
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Execute the plan",
                    ExpectedOutput = "Completed deliverables",
                    AssignedAgentId = workerId,
                    Dependencies = [planTaskId],
                    AsyncExecution = true,
                    HumanInput = false
                }
            ]
        };

        // Act: Cycle 1
        var yaml1 = _exporter.ExportToString(original);
        var config1 = await _loader.LoadFromStringAsync(yaml1, TestContext.Current.CancellationToken);

        // Act: Cycle 2
        var yaml2 = _exporter.ExportToString(config1);
        var config2 = await _loader.LoadFromStringAsync(yaml2, TestContext.Current.CancellationToken);

        // Assert: The configurations from both cycles should be structurally equivalent
        AssertCrewConfigurationsEqual(config1, config2);

        // Also verify the first cycle matches the original structurally
        AssertCrewConfigurationsEqual(original, config1);
    }

    #endregion

    #region Additional Edge Cases

    [Fact]
    public async Task RoundTrip_HierarchicalProcess_PreservesManagerAgent()
    {
        // Arrange
        var bossId = AgentId.Create();
        var original = new CrewConfiguration
        {
            Name = "hierarchical-crew",
            Goal = "Test hierarchical process round-trip",
            Process = ProcessType.Hierarchical,
            ManagerAgentId = bossId,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = bossId,
                    Role = "Boss",
                    Goal = "Manage the team",
                    Backstory = "The team manager"
                },
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Employee",
                    Goal = "Do the work",
                    Backstory = "A hardworking employee"
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Manage work distribution",
                    ExpectedOutput = "Work distributed",
                    AssignedAgentId = bossId
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ProcessType.Hierarchical, reloaded.Process);
        // Manager agent should be one of the reloaded agents
        Assert.NotNull(reloaded.ManagerAgentId);
        var reloadedBoss = reloaded.Agents.FirstOrDefault(a => a.Role == "Boss");
        Assert.NotNull(reloadedBoss);
        Assert.Equal(reloadedBoss.Id, reloaded.ManagerAgentId);
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    [Fact]
    public async Task RoundTrip_LongDescriptions_PreservesContent()
    {
        // Arrange: test with longer text content to verify no truncation
        var longBackstory = string.Join(" ", Enumerable.Repeat(
            "This agent has extensive experience in data analysis, machine learning, and AI research.", 10));
        var longDescription = string.Join(" ", Enumerable.Repeat(
            "Perform a thorough analysis of the dataset including statistical measures and trend identification.", 10));

        var original = new CrewConfiguration
        {
            Name = "long-text-crew",
            Goal = "Test that long text content survives round-trip without truncation",
            Agents =
            [
                new AgentConfiguration
                {
                    Id = AgentId.Create(),
                    Role = "Verbose Agent",
                    Goal = "Handle long descriptions",
                    Backstory = longBackstory
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = longDescription,
                    ExpectedOutput = "A comprehensive report that covers all aspects of the analysis in detail"
                }
            ]
        };

        // Act
        var yaml = _exporter.ExportToString(original);
        var reloaded = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        // Assert
        AssertCrewConfigurationsEqual(original, reloaded);
    }

    #endregion

    #region Deep Comparison Helper

    /// <summary>
    /// Deep comparison of two CrewConfiguration objects.
    /// Compares all fields that are expected to survive a YAML round-trip.
    /// Since the loader generates new IDs on each load, agents are matched by Role
    /// and tasks by Description. Structural relationships (agent assignments, dependencies)
    /// are verified via cross-referencing.
    /// </summary>
    private static void AssertCrewConfigurationsEqual(
        CrewConfiguration expected,
        CrewConfiguration actual)
    {
        // Crew-level fields
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Goal, actual.Goal);
        Assert.Equal(expected.Process, actual.Process);
        Assert.Equal(expected.Verbose, actual.Verbose);
        Assert.Equal(expected.Memory, actual.Memory);
        Assert.Equal(expected.Planning, actual.Planning);

        // Manager agent: verify structurally (both null or both reference the same role)
        if (expected.ManagerAgentId == null)
        {
            Assert.Null(actual.ManagerAgentId);
        }
        else
        {
            Assert.NotNull(actual.ManagerAgentId);
            var expectedManager = expected.Agents.FirstOrDefault(a => a.Id == expected.ManagerAgentId);
            var actualManager = actual.Agents.FirstOrDefault(a => a.Id == actual.ManagerAgentId);
            Assert.NotNull(expectedManager);
            Assert.NotNull(actualManager);
            Assert.Equal(expectedManager.Role, actualManager.Role);
        }

        // Agents — match by Role
        Assert.Equal(expected.Agents.Count, actual.Agents.Count);
        foreach (var expectedAgent in expected.Agents)
        {
            var actualAgent = actual.Agents.FirstOrDefault(a => a.Role == expectedAgent.Role);
            Assert.NotNull(actualAgent);
            AssertAgentsEqual(expectedAgent, actualAgent);
        }

        // Build agent ID mapping (expected -> actual) by Role
        var agentIdMap = new Dictionary<AgentId, AgentId>();
        foreach (var expectedAgent in expected.Agents)
        {
            var actualAgent = actual.Agents.First(a => a.Role == expectedAgent.Role);
            agentIdMap[expectedAgent.Id] = actualAgent.Id;
        }

        // Tasks — match by Description
        Assert.Equal(expected.Tasks.Count, actual.Tasks.Count);

        // Build task ID mapping (expected -> actual) by Description
        var taskIdMap = new Dictionary<TaskId, TaskId>();
        foreach (var expectedTask in expected.Tasks)
        {
            var actualTask = actual.Tasks.First(t => t.Description == expectedTask.Description);
            taskIdMap[expectedTask.Id] = actualTask.Id;
        }

        foreach (var expectedTask in expected.Tasks)
        {
            var actualTask = actual.Tasks.First(t => t.Description == expectedTask.Description);
            AssertTasksEqual(expectedTask, actualTask, agentIdMap, taskIdMap);
        }
    }

    private static void AssertAgentsEqual(
        AgentConfiguration expected,
        AgentConfiguration actual)
    {
        // ID is not compared (loader generates new IDs)
        Assert.Equal(expected.Role, actual.Role);
        Assert.Equal(expected.Goal, actual.Goal);

        // Backstory: empty string and empty-after-export are equivalent
        // The exporter maps empty/whitespace backstory to null,
        // and the loader maps null backstory to empty string
        Assert.Equal(
            string.IsNullOrWhiteSpace(expected.Backstory) ? string.Empty : expected.Backstory,
            actual.Backstory);

        Assert.Equal(expected.AllowDelegation, actual.AllowDelegation);
        Assert.Equal(expected.MaxIterations, actual.MaxIterations);
        Assert.Equal(expected.MaxRPM, actual.MaxRPM);
        Assert.Equal(expected.Verbose, actual.Verbose);

        // Tools
        Assert.Equal(expected.Tools.Count, actual.Tools.Count);
        for (int i = 0; i < expected.Tools.Count; i++)
        {
            Assert.Equal(expected.Tools[i], actual.Tools[i]);
        }

        // LLM Config
        if (expected.LlmConfig == null)
        {
            Assert.Null(actual.LlmConfig);
        }
        else
        {
            Assert.NotNull(actual.LlmConfig);
            Assert.Equal(expected.LlmConfig.Model, actual.LlmConfig!.Model);
            Assert.Equal(expected.LlmConfig.Temperature, actual.LlmConfig.Temperature, precision: 5);
            Assert.Equal(expected.LlmConfig.MaxTokens, actual.LlmConfig.MaxTokens);
        }
    }

    private static void AssertTasksEqual(
        TaskConfiguration expected,
        TaskConfiguration actual,
        Dictionary<AgentId, AgentId> agentIdMap,
        Dictionary<TaskId, TaskId> taskIdMap)
    {
        // ID is not compared directly (loader generates new IDs)
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.ExpectedOutput, actual.ExpectedOutput);
        Assert.Equal(expected.AsyncExecution, actual.AsyncExecution);
        Assert.Equal(expected.HumanInput, actual.HumanInput);

        // Assigned agent: verify via role mapping
        if (expected.AssignedAgentId == null)
        {
            Assert.Null(actual.AssignedAgentId);
        }
        else
        {
            Assert.NotNull(actual.AssignedAgentId);
            Assert.Equal(agentIdMap[expected.AssignedAgentId], actual.AssignedAgentId);
        }

        // Dependencies: verify count and mapped IDs
        Assert.Equal(expected.Dependencies.Count, actual.Dependencies.Count);
        for (int i = 0; i < expected.Dependencies.Count; i++)
        {
            Assert.Equal(taskIdMap[expected.Dependencies[i]], actual.Dependencies[i]);
        }

        // Context (only check count for basic types; complex objects may not survive YAML round-trip)
        Assert.Equal(expected.Context.Count, actual.Context.Count);
    }

    #endregion
}
