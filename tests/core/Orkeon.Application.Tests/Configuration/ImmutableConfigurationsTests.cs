using Orkeon.Application.Configuration;
using System.Collections.Immutable;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Configuration;

public class ImmutableConfigurationsTests
{
    #region CrewConfiguration Tests

    public class CrewConfigurationTests
    {
        [Fact]
        public void ShouldReplaceAgentsList_WhenUsingWithAgents()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "TestCrew",
                Description = "Test",
                Agents = []
            };

            var newAgents = new[]
            {
                new AgentConfiguration { Name = "Agent1", Role = "Dev", Goal = "Code", Backstory = "Expert" },
                new AgentConfiguration { Name = "Agent2", Role = "QA", Goal = "Test", Backstory = "Tester" }
            };

            // Act
            var updatedConfig = config.WithAgents(newAgents);

            // Assert
            Assert.NotSame(config, updatedConfig);
            Assert.Equal(2, updatedConfig.Agents.Count);
            Assert.Equal("Agent1", updatedConfig.Agents[0].Name);
            Assert.Equal("Agent2", updatedConfig.Agents[1].Name);
            Assert.Empty(config.Agents); // Original unchanged
        }

        [Fact]
        public void ShouldAppendAgent_WhenUsingAddAgent()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "TestCrew",
                Description = "Test",
                Agents = ImmutableList.Create(
                    new AgentConfiguration { Name = "Agent1", Role = "Dev", Goal = "Code", Backstory = "Expert" }
                )
            };

            var newAgent = new AgentConfiguration { Name = "Agent2", Role = "QA", Goal = "Test", Backstory = "Tester" };

            // Act
            var updatedConfig = config.AddAgent(newAgent);

            // Assert
            Assert.NotSame(config, updatedConfig);
            Assert.Equal(2, updatedConfig.Agents.Count);
            Assert.Equal("Agent2", updatedConfig.Agents[1].Name);
            Assert.Single(config.Agents); // Original unchanged
        }

        [Fact]
        public void ShouldReplaceTasksList_WhenUsingWithTasks()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "TestCrew",
                Description = "Test"
            };

            var newTasks = new[]
            {
                new TaskConfiguration { Name = "Task1", Description = "Desc1", ExpectedOutput = "Out1" },
                new TaskConfiguration { Name = "Task2", Description = "Desc2", ExpectedOutput = "Out2" }
            };

            // Act
            var updatedConfig = config.WithTasks(newTasks);

            // Assert
            Assert.Equal(2, updatedConfig.Tasks.Count);
            Assert.Equal("Task1", updatedConfig.Tasks[0].Name);
        }

        [Fact]
        public void ShouldAppendTask_WhenUsingAddTask()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "TestCrew",
                Description = "Test"
            };

            var task = new TaskConfiguration { Name = "Task1", Description = "Desc", ExpectedOutput = "Output" };

            // Act
            var updatedConfig = config.AddTask(task);

            // Assert
            Assert.Single(updatedConfig.Tasks);
            Assert.Equal("Task1", updatedConfig.Tasks[0].Name);
        }

        [Fact]
        public void ShouldUpdateMemoryConfiguration_WhenUsingWithMemory()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "TestCrew",
                Description = "Test"
            };

            var memoryConfig = new MemoryConfiguration
            {
                Provider = "Redis",
                EnableLongTerm = true
            };

            // Act
            var updatedConfig = config.WithMemory(memoryConfig);

            // Assert
            Assert.Equal("Redis", updatedConfig.Memory.Provider);
            Assert.True(updatedConfig.Memory.EnableLongTerm);
        }

        [Fact]
        public void ShouldAddOrUpdateMetadataEntry_WhenUsingWithMetadata()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "TestCrew",
                Description = "Test"
            };

            // Act
            var updatedConfig = config
                .WithMetadata("version", "1.0")
                .WithMetadata("environment", "test");

            // Assert
            Assert.Equal(2, updatedConfig.Metadata.Count);
            Assert.Equal("1.0", updatedConfig.Metadata["version"]);
            Assert.Equal("test", updatedConfig.Metadata["environment"]);
            Assert.Empty(config.Metadata); // Original unchanged
        }

        [Fact]
        public void ShouldReturnSuccess_WhenValidatingWithValidConfiguration()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "ValidCrew",
                Description = "Valid description",
                MaxAgents = 10,
                MaxTasks = 100,
                ExecutionTimeout = TimeSpan.FromMinutes(30),
                Agents = ImmutableList.Create(
                    new AgentConfiguration
                    {
                        Name = "Agent1",
                        Role = RoleDeveloper,
                        Goal = GoalWriteCode,
                        Backstory = "Expert developer",
                        MaxExecutionTime = 300
                    }
                ),
                Tasks = ImmutableList.Create(
                    new TaskConfiguration
                    {
                        Name = "Task1",
                        Description = "Test task",
                        ExpectedOutput = "Output",
                        EstimatedDuration = TimeoutExtended
                    }
                )
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithEmptyName()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "",
                Description = "Test"
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Crew name is required", result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithInvalidMaxAgents()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "Test",
                Description = "Test",
                MaxAgents = 0
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("MaxAgents must be greater than 0", result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithExcessAgents()
        {
            // Arrange
            var agents = Enumerable.Range(1, 3).Select(i =>
                new AgentConfiguration
                {
                    Name = $"Agent{i}",
                    Role = $"Role{i}",
                    Goal = $"Goal{i}",
                    Backstory = $"Story{i}"
                }).ToImmutableList();

            var config = new CrewConfiguration
            {
                Name = "Test",
                Description = "Test",
                MaxAgents = 2,
                Agents = agents
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Agent count (3) exceeds maximum (2)", result.Errors);
        }

        [Fact]
        public void ShouldIncludeAgentErrors_WhenValidatingWithInvalidAgent()
        {
            // Arrange
            var config = new CrewConfiguration
            {
                Name = "Test",
                Description = "Test",
                Agents = ImmutableList.Create(
                    new AgentConfiguration
                    {
                        Name = "", // Invalid
                        Role = "Role",
                        Goal = "Goal",
                        Backstory = "Story"
                    }
                )
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Agent[0]") && e.Contains("name is required"));
        }
    }

    #endregion

    #region AgentConfiguration Tests

    public class AgentConfigurationTests
    {
        [Fact]
        public void ShouldReplaceToolsList_WhenUsingWithTools()
        {
            // Arrange
            var config = new AgentConfiguration
            {
                Name = "Agent",
                Role = "Dev",
                Goal = "Code",
                Backstory = "Expert",
                Tools = ImmutableList.Create("Tool1")
            };

            var newTools = new[] { "Tool2", "Tool3" };

            // Act
            var updatedConfig = config.WithTools(newTools);

            // Assert
            Assert.NotSame(config, updatedConfig);
            Assert.Equal(2, updatedConfig.Tools.Count);
            Assert.Contains("Tool2", updatedConfig.Tools);
            Assert.Contains("Tool3", updatedConfig.Tools);
            Assert.Single(config.Tools); // Original unchanged
        }

        [Fact]
        public void ShouldAppendTool_WhenUsingAddTool()
        {
            // Arrange
            var config = new AgentConfiguration
            {
                Name = "Agent",
                Role = "Dev",
                Goal = "Code",
                Backstory = "Expert",
                Tools = ImmutableList.Create("Tool1")
            };

            // Act
            var updatedConfig = config.AddTool("Tool2");

            // Assert
            Assert.Equal(2, updatedConfig.Tools.Count);
            Assert.Contains("Tool1", updatedConfig.Tools);
            Assert.Contains("Tool2", updatedConfig.Tools);
        }

        [Fact]
        public void ShouldUpdateLlmConfiguration_WhenUsingWithLlm()
        {
            // Arrange
            var config = new AgentConfiguration
            {
                Name = "Agent",
                Role = "Dev",
                Goal = "Code",
                Backstory = "Expert"
            };

            var llmConfig = new LlmConfiguration
            {
                Provider = "Claude",
                Model = ModelClaude3
            };

            // Act
            var updatedConfig = config.WithLlm(llmConfig);

            // Assert
            Assert.Equal("Claude", updatedConfig.Llm.Provider);
            Assert.Equal(ModelClaude3, updatedConfig.Llm.Model);
        }

        [Fact]
        public void ShouldAddOrUpdateParameter_WhenUsingWithParameter()
        {
            // Arrange
            var config = new AgentConfiguration
            {
                Name = "Agent",
                Role = "Dev",
                Goal = "Code",
                Backstory = "Expert"
            };

            // Act
            var updatedConfig = config
                .WithParameter("skill", "advanced")
                .WithParameter("experience", 10);

            // Assert
            Assert.Equal(2, updatedConfig.Parameters.Count);
            Assert.Equal("advanced", updatedConfig.Parameters["skill"]);
            Assert.Equal(10, updatedConfig.Parameters["experience"]);
        }

        [Fact]
        public void ShouldReturnSuccess_WhenValidatingWithValidConfiguration()
        {
            // Arrange
            var config = new AgentConfiguration
            {
                Name = "ValidAgent",
                Role = RoleDeveloper,
                Goal = GoalWriteCode,
                Backstory = "Experienced developer",
                MaxExecutionTime = 300
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ShouldReturnErrors_WhenValidatingWithMissingRequiredFields()
        {
            // Arrange
            var config = new AgentConfiguration
            {
                Name = "",
                Role = "",
                Goal = "",
                Backstory = ""
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Agent name is required", result.Errors);
            Assert.Contains("Agent role is required", result.Errors);
            Assert.Contains("Agent goal is required", result.Errors);
            Assert.Contains("Agent backstory is required", result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithInvalidMaxExecutionTime()
        {
            // Arrange
            var config = new AgentConfiguration
            {
                Name = "Agent",
                Role = "Dev",
                Goal = "Code",
                Backstory = "Expert",
                MaxExecutionTime = 0
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("MaxExecutionTime must be positive", result.Errors);
        }
    }

    #endregion

    #region TaskConfiguration Tests

    public class TaskConfigurationTests
    {
        [Fact]
        public void ShouldReplaceDependenciesList_WhenUsingWithDependencies()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "Task",
                Description = "Desc",
                ExpectedOutput = "Output",
                Dependencies = ImmutableList.Create("Dep1")
            };

            var newDeps = new[] { "Dep2", "Dep3" };

            // Act
            var updatedConfig = config.WithDependencies(newDeps);

            // Assert
            Assert.Equal(2, updatedConfig.Dependencies.Count);
            Assert.Contains("Dep2", updatedConfig.Dependencies);
            Assert.Contains("Dep3", updatedConfig.Dependencies);
        }

        [Fact]
        public void ShouldAppendDependency_WhenAddingDependency()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "Task",
                Description = "Desc",
                ExpectedOutput = "Output"
            };

            // Act
            var updatedConfig = config.AddDependency("Dep1");

            // Assert
            Assert.Single(updatedConfig.Dependencies);
            Assert.Contains("Dep1", updatedConfig.Dependencies);
        }

        [Fact]
        public void ShouldReplaceToolsList_WhenUsingWithRequiredTools()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "Task",
                Description = "Desc",
                ExpectedOutput = "Output"
            };

            var tools = new[] { "Tool1", "Tool2" };

            // Act
            var updatedConfig = config.WithRequiredTools(tools);

            // Assert
            Assert.Equal(2, updatedConfig.RequiredTools.Count);
            Assert.Contains("Tool1", updatedConfig.RequiredTools);
            Assert.Contains("Tool2", updatedConfig.RequiredTools);
        }

        [Fact]
        public void ShouldAddOrUpdateContext_WhenUsingWithContext()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "Task",
                Description = "Desc",
                ExpectedOutput = "Output"
            };

            // Act
            var updatedConfig = config
                .WithContext("input", "data.csv")
                .WithContext("format", "json");

            // Assert
            Assert.Equal(2, updatedConfig.Context.Count);
            Assert.Equal("data.csv", updatedConfig.Context["input"]);
            Assert.Equal("json", updatedConfig.Context["format"]);
        }

        [Fact]
        public void ShouldSetAssignedAgent_WhenUsingAssignToAgent()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "Task",
                Description = "Desc",
                ExpectedOutput = "Output"
            };

            // Act
            var updatedConfig = config.AssignToAgent("Agent1");

            // Assert
            Assert.Equal("Agent1", updatedConfig.AssignedAgent);
        }

        [Fact]
        public void ShouldReturnSuccess_WhenValidatingWithValidConfiguration()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "ValidTask",
                Description = "Valid description",
                ExpectedOutput = "Expected output",
                EstimatedDuration = TimeoutLong
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ShouldReturnErrors_WhenValidatingWithMissingRequiredFields()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "",
                Description = "",
                ExpectedOutput = ""
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Task name is required", result.Errors);
            Assert.Contains("Task description is required", result.Errors);
            Assert.Contains("Task expected output is required", result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithInvalidDuration()
        {
            // Arrange
            var config = new TaskConfiguration
            {
                Name = "Task",
                Description = "Desc",
                ExpectedOutput = "Output",
                EstimatedDuration = TimeSpan.Zero
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("EstimatedDuration must be positive", result.Errors);
        }
    }

    #endregion

    #region LlmConfiguration Tests

    public class LlmConfigurationTests
    {
        [Fact]
        public void ShouldReturnDefaultConfiguration_WhenUsingDefault()
        {
            // Act
            var config = LlmConfiguration.Default;

            // Assert
            Assert.Equal("OpenAI", config.Provider);
            Assert.Equal(ModelGpt35Turbo, config.Model);
            Assert.Equal(0.7, config.Temperature);
            Assert.Equal(1000, config.MaxTokens);
            Assert.Equal(3, config.MaxRetries);
            Assert.Equal(TimeoutQuick, config.Timeout);
        }

        [Fact]
        public void ShouldAddOrUpdateParameter_WhenUsingWithParameter()
        {
            // Arrange
            var config = new LlmConfiguration
            {
                Provider = "OpenAI",
                Model = ModelGpt4
            };

            // Act
            var updatedConfig = config
                .WithParameter("top_p", 0.9)
                .WithParameter("frequency_penalty", 0.5);

            // Assert
            Assert.Equal(2, updatedConfig.Parameters.Count);
            Assert.Equal(0.9, updatedConfig.Parameters["top_p"]);
            Assert.Equal(0.5, updatedConfig.Parameters["frequency_penalty"]);
        }

        [Fact]
        public void ShouldClampToValidRange_WhenUsingWithTemperature()
        {
            // Arrange
            var config = new LlmConfiguration
            {
                Provider = "OpenAI",
                Model = ModelGpt4
            };

            // Act
            var config1 = config.WithTemperature(-0.5); // Below range
            var config2 = config.WithTemperature(1.5);  // Within range
            var config3 = config.WithTemperature(3.0);  // Above range

            // Assert
            Assert.Equal(0.0, config1.Temperature);
            Assert.Equal(1.5, config2.Temperature);
            Assert.Equal(2.0, config3.Temperature);
        }

        [Fact]
        public void ShouldEnsurePositiveValue_WhenUsingWithMaxTokens()
        {
            // Arrange
            var config = new LlmConfiguration
            {
                Provider = "OpenAI",
                Model = ModelGpt4
            };

            // Act
            var config1 = config.WithMaxTokens(-100);
            var config2 = config.WithMaxTokens(2000);

            // Assert
            Assert.Equal(1, config1.MaxTokens);
            Assert.Equal(2000, config2.MaxTokens);
        }

        [Fact]
        public void ShouldReturnSuccess_WhenValidatingWithValidConfiguration()
        {
            // Arrange
            var config = new LlmConfiguration
            {
                Provider = "OpenAI",
                Model = ModelGpt4,
                Temperature = 0.8,
                MaxTokens = 1500,
                MaxRetries = 3,
                Timeout = TimeSpan.FromSeconds(60)
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithMissingProvider()
        {
            // Arrange
            var config = new LlmConfiguration
            {
                Provider = "",
                Model = ModelGpt4
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("LLM provider is required", result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithInvalidTemperature()
        {
            // Arrange
            var config = new LlmConfiguration
            {
                Provider = "OpenAI",
                Model = ModelGpt4,
                Temperature = 2.5 // Out of range
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Temperature must be between 0.0 and 2.0", result.Errors);
        }

        [Fact]
        public void ShouldReturnError_WhenValidatingWithInvalidMaxTokens()
        {
            // Arrange
            var config = new LlmConfiguration
            {
                Provider = "OpenAI",
                Model = ModelGpt4,
                MaxTokens = 0
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("MaxTokens must be positive", result.Errors);
        }
    }

    #endregion

    #region MemoryConfiguration Tests

    public class MemoryConfigurationTests
    {
        [Fact]
        public void ShouldReturnDefaultConfiguration_WhenUsingDefault()
        {
            // Act
            var config = MemoryConfiguration.Default;

            // Assert
            Assert.Equal("InMemory", config.Provider);
            Assert.True(config.EnableShortTerm);
            Assert.False(config.EnableLongTerm);
            Assert.False(config.EnableEpisodic);
            Assert.Equal(1000, config.MaxShortTermEntries);
            Assert.Equal(10000, config.MaxLongTermEntries);
            Assert.Equal(TimeSpan.FromDays(30), config.RetentionPeriod);
        }

        [Fact]
        public void ShouldAddOrUpdateSetting_WhenUsingWithProviderSetting()
        {
            // Arrange
            var config = new MemoryConfiguration
            {
                Provider = "Redis"
            };

            // Act
            var updatedConfig = config
                .WithProviderSetting("host", "localhost")
                .WithProviderSetting("port", 6379);

            // Assert
            Assert.Equal(2, updatedConfig.ProviderSettings.Count);
            Assert.Equal("localhost", updatedConfig.ProviderSettings["host"]);
            Assert.Equal(6379, updatedConfig.ProviderSettings["port"]);
        }

        [Fact]
        public void ShouldUpdateRetentionPeriod_WhenUsingWithRetentionPeriod()
        {
            // Arrange
            var config = new MemoryConfiguration
            {
                Provider = "InMemory"
            };

            var newPeriod = TimeSpan.FromDays(90);

            // Act
            var updatedConfig = config.WithRetentionPeriod(newPeriod);

            // Assert
            Assert.Equal(newPeriod, updatedConfig.RetentionPeriod);
        }
    }

    #endregion

    #region RetryConfiguration Tests

    public class RetryConfigurationTests
    {
        [Fact]
        public void ShouldReturnDefaultConfiguration_WhenUsingDefault()
        {
            // Act
            var config = RetryConfiguration.Default;

            // Assert
            Assert.Equal(3, config.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(1), config.InitialDelay);
            Assert.Equal(TimeoutQuick, config.MaxDelay);
            Assert.Equal(2.0, config.BackoffMultiplier);
            Assert.True(config.UseJitter);
            Assert.Empty(config.RetryableExceptions);
        }

        [Fact]
        public void ShouldReplaceExceptionsList_WhenUsingWithRetryableExceptions()
        {
            // Arrange
            var config = RetryConfiguration.Default;
            var exceptions = new[] { typeof(TimeoutException), typeof(InvalidOperationException) };

            // Act
            var updatedConfig = config.WithRetryableExceptions(exceptions);

            // Assert
            Assert.Equal(2, updatedConfig.RetryableExceptions.Count);
            Assert.Contains(typeof(TimeoutException), updatedConfig.RetryableExceptions);
            Assert.Contains(typeof(InvalidOperationException), updatedConfig.RetryableExceptions);
        }

        [Fact]
        public void ShouldAppendException_WhenAddingRetryableException()
        {
            // Arrange
            var config = RetryConfiguration.Default;

            // Act
            var updatedConfig = config
                .AddRetryableException<TimeoutException>()
                .AddRetryableException<ArgumentException>();

            // Assert
            Assert.Equal(2, updatedConfig.RetryableExceptions.Count);
            Assert.Contains(typeof(TimeoutException), updatedConfig.RetryableExceptions);
            Assert.Contains(typeof(ArgumentException), updatedConfig.RetryableExceptions);
        }
    }

    #endregion

    #region ValidationResult Tests

    public class ValidationResultTests
    {
        [Fact]
        public void ShouldReturnValidResult_WhenUsingSuccess()
        {
            // Act
            var result = ValidationResult.Success();

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ShouldReturnInvalidResult_WhenUsingFailedWithParamsArray()
        {
            // Act
            var result = ValidationResult.Failed("Error 1", "Error 2", "Error 3");

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(3, result.Errors.Count);
            Assert.Contains("Error 1", result.Errors);
            Assert.Contains("Error 2", result.Errors);
            Assert.Contains("Error 3", result.Errors);
        }

        [Fact]
        public void ShouldReturnInvalidResult_WhenUsingFailedWithEnumerable()
        {
            // Arrange
            var errors = new List<string> { "Error A", "Error B" };

            // Act
            var result = ValidationResult.Failed(errors);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
            Assert.Contains("Error A", result.Errors);
            Assert.Contains("Error B", result.Errors);
        }

        [Fact]
        public void ShouldReturnTrue_WhenUsingIsValidWithNoErrors()
        {
            // Arrange
            var result = new ValidationResult([]);

            // Assert
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ShouldReturnFalse_WhenUsingIsValidWithErrors()
        {
            // Arrange
            var result = new ValidationResult(ImmutableList.Create("Error"));

            // Assert
            Assert.False(result.IsValid);
        }
    }

    #endregion

    #region Immutability Tests

    public class ImmutabilityTests
    {
        [Fact]
        public void ShouldModificationsShouldNotAffectOriginal_WhenUsingCrewConfiguration()
        {
            // Arrange
            var original = new CrewConfiguration
            {
                Name = "Original",
                Description = "Original Desc",
                MaxAgents = 5
            };

            // Act
            var modified = original with { Name = "Modified" };
            var withAgent = original.AddAgent(new AgentConfiguration
            {
                Name = "Agent",
                Role = "Role",
                Goal = "Goal",
                Backstory = "Story"
            });
            var withMetadata = original.WithMetadata("key", "value");

            // Assert
            Assert.Equal("Original", original.Name);
            Assert.Equal("Modified", modified.Name);
            Assert.Empty(original.Agents);
            Assert.Single(withAgent.Agents);
            Assert.Empty(original.Metadata);
            Assert.Single(withMetadata.Metadata);
        }

        [Fact]
        public void ShouldModificationsShouldNotAffectOriginal_WhenUsingAgentConfiguration()
        {
            // Arrange
            var original = new AgentConfiguration
            {
                Name = "Original",
                Role = "OriginalRole",
                Goal = "OriginalGoal",
                Backstory = "OriginalStory"
            };

            // Act
            var modified = original with { Name = "Modified" };
            var withTool = original.AddTool("NewTool");
            var withParam = original.WithParameter("param", "value");

            // Assert
            Assert.Equal("Original", original.Name);
            Assert.Equal("Modified", modified.Name);
            Assert.Empty(original.Tools);
            Assert.Single(withTool.Tools);
            Assert.Empty(original.Parameters);
            Assert.Single(withParam.Parameters);
        }
    }

    #endregion
}
