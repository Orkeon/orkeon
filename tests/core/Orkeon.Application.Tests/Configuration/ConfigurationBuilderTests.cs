using Orkeon.Application.Configuration;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Application.Tests.Configuration;

public class ConfigurationBuilderTests
{
    #region CrewConfigurationBuilder Tests

    public class CrewConfigurationBuilderTests
    {
        [Fact]
        public void ShouldSetName_WhenUsingWithNameWithValidName()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var name = "Test Crew";

            // Act
            var result = builder.WithName(name);
            var config = builder.Build();

            // Assert
            Assert.Same(builder, result);
            Assert.Equal(name, config.Name);
        }

        [Fact]
        public void ShouldThrowArgumentNullException_WhenUsingWithNameWithNullName()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => builder.WithName(null!));
            Assert.Equal("name", exception.ParamName);
        }

        [Fact]
        public void ShouldSetDescription_WhenUsingWithDescriptionWithValidDescription()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var description = "Test Description";

            // Act
            var result = builder.WithDescription(description);
            var config = builder.Build();

            // Assert
            Assert.Same(builder, result);
            Assert.Equal(description, config.Description);
        }

        [Fact]
        public void ShouldSetProcessType_WhenUsingWithProcessType()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var processType = ProcessType.Hierarchical;

            // Act
            builder.WithProcessType(processType);
            var config = builder.Build();

            // Assert
            Assert.Equal(processType, config.ProcessType);
        }

        [Fact]
        public void ShouldSetVerbosityLevel_WhenUsingWithVerbosity()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var verbosity = VerbosityLevel.Debug;

            // Act
            builder.WithVerbosity(verbosity);
            var config = builder.Build();

            // Assert
            Assert.Equal(verbosity, config.Verbosity);
        }

        [Fact]
        public void ShouldSetAllowCodeExecution_WhenUsingWithCodeExecution()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act
            builder.WithCodeExecution(true);
            var config = builder.Build();

            // Assert
            Assert.True(config.AllowCodeExecution);
        }

        [Fact]
        public void ShouldSetMaxAgents_WhenUsingWithMaxAgentsWithValidValue()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var maxAgents = 20;

            // Act
            builder.WithMaxAgents(maxAgents);
            var config = builder.Build();

            // Assert
            Assert.Equal(maxAgents, config.MaxAgents);
        }

        [Fact]
        public void ShouldThrowArgumentException_WhenUsingWithMaxAgentsWithZeroOrNegative()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => builder.WithMaxAgents(0));
            Assert.Contains("Max agents must be positive", exception.Message);

            exception = Assert.Throws<ArgumentException>(() => builder.WithMaxAgents(-1));
            Assert.Contains("Max agents must be positive", exception.Message);
        }

        [Fact]
        public void ShouldSetMaxTasks_WhenUsingWithMaxTasksWithValidValue()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var maxTasks = 50;

            // Act
            builder.WithMaxTasks(maxTasks);
            var config = builder.Build();

            // Assert
            Assert.Equal(maxTasks, config.MaxTasks);
        }

        [Fact]
        public void ShouldSetTimeout_WhenUsingWithExecutionTimeoutWithValidTimeout()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var timeout = TimeSpan.FromHours(2);

            // Act
            builder.WithExecutionTimeout(timeout);
            var config = builder.Build();

            // Assert
            Assert.Equal(timeout, config.ExecutionTimeout);
        }

        [Fact]
        public void ShouldThrowArgumentException_WhenUsingWithExecutionTimeoutWithZeroOrNegative()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => builder.WithExecutionTimeout(TimeSpan.Zero));
            Assert.Contains("Timeout must be positive", exception.Message);
        }

        [Fact]
        public void ShouldAddToAgentsList_WhenUsingAddAgentWithValidAgent()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var agent = new AgentConfiguration
            {
                Name = "Agent1",
                Role = RoleDeveloper,
                Goal = GoalWriteCode,
                Backstory = "Experienced developer"
            };

            // Act
            builder.AddAgent(agent);
            var config = builder.Build();

            // Assert
            Assert.Single(config.Agents);
            Assert.Equal("Agent1", config.Agents[0].Name);
        }

        [Fact]
        public void ShouldThrowArgumentNullException_WhenUsingAddAgentWithNullAgent()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => builder.AddAgent((AgentConfiguration)null!));
            Assert.Equal("agent", exception.ParamName);
        }

        [Fact]
        public void ShouldConfigureAndAddAgent_WhenUsingAddAgentWithBuilder()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act
            builder.AddAgent(agent => agent
                .WithName("TestAgent")
                .WithRole("Tester")
                .WithGoal("Test everything")
                .WithBackstory("Quality assurance expert"));
            var config = builder.Build();

            // Assert
            Assert.Single(config.Agents);
            Assert.Equal("TestAgent", config.Agents[0].Name);
            Assert.Equal("Tester", config.Agents[0].Role);
        }

        [Fact]
        public void ShouldAddAllAgents_WhenAddingAgentsWithMultipleAgents()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var agents = new List<AgentConfiguration>
            {
                new() { Name = "Agent1", Role = "Role1", Goal = "Goal1", Backstory = "Story1" },
                new() { Name = "Agent2", Role = "Role2", Goal = "Goal2", Backstory = "Story2" }
            };

            // Act
            builder.AddAgents(agents);
            var config = builder.Build();

            // Assert
            Assert.Equal(2, config.Agents.Count);
            Assert.Equal("Agent1", config.Agents[0].Name);
            Assert.Equal("Agent2", config.Agents[1].Name);
        }

        [Fact]
        public void ShouldAddToTasksList_WhenUsingAddTaskWithValidTask()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var task = new TaskConfiguration
            {
                Name = "Task1",
                Description = "Test task",
                ExpectedOutput = "Test output"
            };

            // Act
            builder.AddTask(task);
            var config = builder.Build();

            // Assert
            Assert.Single(config.Tasks);
            Assert.Equal("Task1", config.Tasks[0].Name);
        }

        [Fact]
        public void ShouldConfigureAndAddTask_WhenUsingAddTaskWithBuilder()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act
            builder.AddTask(task => task
                .WithName("TestTask")
                .WithDescription("Test description")
                .WithExpectedOutput("Expected result")
                .WithPriority(TaskPriority.High));
            var config = builder.Build();

            // Assert
            Assert.Single(config.Tasks);
            Assert.Equal("TestTask", config.Tasks[0].Name);
            Assert.Equal(TaskPriority.High, config.Tasks[0].Priority);
        }

        [Fact]
        public void ShouldSetMemoryConfig_WhenUsingWithMemoryWithValidConfiguration()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();
            var memoryConfig = new MemoryConfiguration
            {
                Provider = "Redis",
                EnableShortTerm = true,
                EnableLongTerm = true
            };

            // Act
            builder.WithMemory(memoryConfig);
            var config = builder.Build();

            // Assert
            Assert.Equal("Redis", config.Memory.Provider);
            Assert.True(config.Memory.EnableShortTerm);
            Assert.True(config.Memory.EnableLongTerm);
        }

        [Fact]
        public void ShouldConfigureMemory_WhenUsingWithMemoryWithBuilder()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act
            builder.WithMemory(memory => memory
                .WithProvider("ChromaDB")
                .EnableShortTerm()
                .EnableLongTerm()
                .WithMaxShortTermEntries(500));
            var config = builder.Build();

            // Assert
            Assert.Equal("ChromaDB", config.Memory.Provider);
            Assert.Equal(500, config.Memory.MaxShortTermEntries);
        }

        [Fact]
        public void ShouldAddMetadataEntries_WhenUsingWithMetadata()
        {
            // Arrange
            var builder = new CrewConfigurationBuilder();

            // Act
            builder
                .WithMetadata("version", "1.0.0")
                .WithMetadata("environment", "production")
                .WithMetadata("retryCount", 3);
            var config = builder.Build();

            // Assert
            Assert.Equal(3, config.Metadata.Count);
            Assert.Equal("1.0.0", config.Metadata["version"]);
            Assert.Equal("production", config.Metadata["environment"]);
            Assert.Equal(3, config.Metadata["retryCount"]);
        }

        [Fact]
        public void ShouldCreateValidConfig_WhenBuildingWithCompleteConfiguration()
        {
            // Arrange & Act
            var config = new CrewConfigurationBuilder()
                .WithName("Complete Crew")
                .WithDescription("Full configuration test")
                .WithProcessType(ProcessType.Parallel)
                .WithVerbosity(VerbosityLevel.Verbose)
                .WithCodeExecution(true)
                .WithMaxAgents(15)
                .WithMaxTasks(200)
                .WithExecutionTimeout(TimeSpan.FromHours(1))
                .AddAgent(a => a.WithName("Agent1").WithRole(RoleDeveloper).WithGoal("Code").WithBackstory("Expert"))
                .AddTask(t => t.WithName("Task1").WithDescription("Test").WithExpectedOutput("Result"))
                .WithMemory(m => m.WithProvider("Redis").EnableLongTerm())
                .WithMetadata("test", true)
                .Build();

            // Assert
            Assert.Equal("Complete Crew", config.Name);
            Assert.Equal("Full configuration test", config.Description);
            Assert.Equal(ProcessType.Parallel, config.ProcessType);
            Assert.Equal(VerbosityLevel.Verbose, config.Verbosity);
            Assert.True(config.AllowCodeExecution);
            Assert.Equal(15, config.MaxAgents);
            Assert.Equal(200, config.MaxTasks);
            Assert.Equal(TimeSpan.FromHours(1), config.ExecutionTimeout);
            Assert.Single(config.Agents);
            Assert.Single(config.Tasks);
            Assert.Equal("Redis", config.Memory.Provider);
            Assert.True(config.Metadata.ContainsKey("test"));
        }
    }

    #endregion

    #region AgentConfigurationBuilder Tests

    public class AgentConfigurationBuilderTests
    {
        private static readonly string[] s_devTools = ["IDE", "Debugger", "Profiler"];

        [Fact]
        public void ShouldSetName_WhenUsingWithNameWithValidName()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            var result = builder.WithName("TestAgent");
            var config = builder.Build();

            // Assert
            Assert.Same(builder, result);
            Assert.Equal("TestAgent", config.Name);
        }

        [Fact]
        public void ShouldSetRole_WhenUsingWithRoleWithValidRole()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.WithRole(RoleDeveloper);
            var config = builder.Build();

            // Assert
            Assert.Equal(RoleDeveloper, config.Role);
        }

        [Fact]
        public void ShouldSetGoal_WhenUsingWithGoalWithValidGoal()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.WithGoal("Write high-quality code");
            var config = builder.Build();

            // Assert
            Assert.Equal("Write high-quality code", config.Goal);
        }

        [Fact]
        public void ShouldSetBackstory_WhenUsingWithBackstoryWithValidBackstory()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.WithBackstory("10 years of experience");
            var config = builder.Build();

            // Assert
            Assert.Equal("10 years of experience", config.Backstory);
        }

        [Fact]
        public void ShouldSetAgentType_WhenUsingWithType()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.WithType(AgentType.Manager);
            var config = builder.Build();

            // Assert
            Assert.Equal(AgentType.Manager, config.Type);
        }

        [Fact]
        public void ShouldSetVerboseFlag_WhenUsingWithVerbose()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.WithVerbose(true);
            var config = builder.Build();

            // Assert
            Assert.True(config.Verbose);
        }

        [Fact]
        public void ShouldSetAllowDelegation_WhenUsingWithDelegation()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.WithDelegation(true);
            var config = builder.Build();

            // Assert
            Assert.True(config.AllowDelegation);
        }

        [Fact]
        public void ShouldSetExecutionTime_WhenUsingWithMaxExecutionTimeWithValidTime()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.WithMaxExecutionTime(600);
            var config = builder.Build();

            // Assert
            Assert.Equal(600, config.MaxExecutionTime);
        }

        [Fact]
        public void ShouldThrowArgumentException_WhenUsingWithMaxExecutionTimeWithInvalidTime()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => builder.WithMaxExecutionTime(0));
            Assert.Contains("Execution time must be positive", exception.Message);
        }

        [Fact]
        public void ShouldAddToToolsList_WhenUsingAddToolWithValidTool()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder.AddTool(ToolSearch);
            builder.AddTool("CalculatorTool");
            var config = builder.Build();

            // Assert
            Assert.Equal(2, config.Tools.Count);
            Assert.Contains(ToolSearch, config.Tools);
            Assert.Contains("CalculatorTool", config.Tools);
        }

        [Fact]
        public void ShouldAddAllTools_WhenAddingToolsWithMultipleTools()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();
            var tools = new[] { "Tool1", "Tool2", "Tool3" };

            // Act
            builder.AddTools(tools);
            var config = builder.Build();

            // Assert
            Assert.Equal(3, config.Tools.Count);
            Assert.Equal(tools, config.Tools);
        }

        [Fact]
        public void ShouldSetLlmConfiguration_WhenUsingWithLlmWithValidConfig()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();
            var llmConfig = new LlmConfiguration { Provider = "OpenAI", Model = ModelGpt4 };

            // Act
            builder.WithLlm(llmConfig);
            var config = builder.Build();

            // Assert
            Assert.Equal("OpenAI", config.Llm.Provider);
            Assert.Equal(ModelGpt4, config.Llm.Model);
        }

        [Fact]
        public void ShouldAddParameters_WhenUsingWithParameter()
        {
            // Arrange
            var builder = new AgentConfigurationBuilder();

            // Act
            builder
                .WithParameter("temperature", 0.7)
                .WithParameter("maxTokens", 1000)
                .WithParameter("useCache", true);
            var config = builder.Build();

            // Assert
            Assert.Equal(3, config.Parameters.Count);
            Assert.Equal(0.7, config.Parameters["temperature"]);
            Assert.Equal(1000, config.Parameters["maxTokens"]);
            Assert.True((bool)config.Parameters["useCache"]);
        }

        [Fact]
        public void ShouldCreateValidConfig_WhenBuildingWithCompleteConfiguration()
        {
            // Arrange & Act
            var config = new AgentConfigurationBuilder()
                .WithName("CompleteAgent")
                .WithRole(RoleSeniorDeveloper)
                .WithGoal("Build robust applications")
                .WithBackstory("20 years in software development")
                .WithType(AgentType.Worker)
                .WithVerbose(true)
                .WithDelegation(true)
                .WithMaxExecutionTime(900)
                .AddTools(s_devTools)
                .WithLlm(new LlmConfiguration { Provider = "Claude", Model = ModelClaude3 })
                .WithParameter("skill_level", "expert")
                .Build();

            // Assert
            Assert.Equal("CompleteAgent", config.Name);
            Assert.Equal(RoleSeniorDeveloper, config.Role);
            Assert.Equal("Build robust applications", config.Goal);
            Assert.Equal("20 years in software development", config.Backstory);
            Assert.Equal(AgentType.Worker, config.Type);
            Assert.True(config.Verbose);
            Assert.True(config.AllowDelegation);
            Assert.Equal(900, config.MaxExecutionTime);
            Assert.Equal(3, config.Tools.Count);
            Assert.Equal("Claude", config.Llm.Provider);
            Assert.Equal("expert", config.Parameters["skill_level"]);
        }
    }

    #endregion

    #region TaskConfigurationBuilder Tests

    public class TaskConfigurationBuilderTests
    {
        [Fact]
        public void ShouldSetName_WhenUsingWithNameWithValidName()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.WithName("TestTask");
            var config = builder.Build();

            // Assert
            Assert.Equal("TestTask", config.Name);
        }

        [Fact]
        public void ShouldSetDescription_WhenUsingWithDescriptionWithValidDescription()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.WithDescription("Task description");
            var config = builder.Build();

            // Assert
            Assert.Equal("Task description", config.Description);
        }

        [Fact]
        public void ShouldSetExpectedOutput_WhenUsingWithExpectedOutputWithValidOutput()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.WithExpectedOutput("Expected result");
            var config = builder.Build();

            // Assert
            Assert.Equal("Expected result", config.ExpectedOutput);
        }

        [Fact]
        public void ShouldSetAssignedAgent_WhenAssigningToWithValidAgent()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.AssignTo("Agent1");
            var config = builder.Build();

            // Assert
            Assert.Equal("Agent1", config.AssignedAgent);
        }

        [Fact]
        public void ShouldSetPriority_WhenUsingWithPriority()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.WithPriority(TaskPriority.Critical);
            var config = builder.Build();

            // Assert
            Assert.Equal(TaskPriority.Critical, config.Priority);
        }

        [Fact]
        public void ShouldSetAsyncFlag_WhenUsingWithAsync()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.WithAsync(true);
            var config = builder.Build();

            // Assert
            Assert.True(config.IsAsync);
        }

        [Fact]
        public void ShouldSetDuration_WhenUsingWithEstimatedDurationWithValidDuration()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();
            var duration = TimeSpan.FromMinutes(30);

            // Act
            builder.WithEstimatedDuration(duration);
            var config = builder.Build();

            // Assert
            Assert.Equal(duration, config.EstimatedDuration);
        }

        [Fact]
        public void ShouldThrowArgumentException_WhenUsingWithEstimatedDurationWithInvalidDuration()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.WithEstimatedDuration(TimeSpan.Zero));
            Assert.Contains("Duration must be positive", exception.Message);
        }

        [Fact]
        public void ShouldAddToDependencies_WhenAddingDependencyWithValidDependency()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.AddDependency("Task1");
            builder.AddDependency("Task2");
            var config = builder.Build();

            // Assert
            Assert.Equal(2, config.Dependencies.Count);
            Assert.Contains("Task1", config.Dependencies);
            Assert.Contains("Task2", config.Dependencies);
        }

        [Fact]
        public void ShouldAddToRequiredTools_WhenUsingAddRequiredToolWithValidTool()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder.AddRequiredTool("WebScraper");
            builder.AddRequiredTool("DataAnalyzer");
            var config = builder.Build();

            // Assert
            Assert.Equal(2, config.RequiredTools.Count);
            Assert.Contains("WebScraper", config.RequiredTools);
            Assert.Contains("DataAnalyzer", config.RequiredTools);
        }

        [Fact]
        public void ShouldAddContextEntries_WhenUsingWithContext()
        {
            // Arrange
            var builder = new TaskConfigurationBuilder();

            // Act
            builder
                .WithContext("input_file", "data.csv")
                .WithContext("output_format", "json")
                .WithContext("validate", true);
            var config = builder.Build();

            // Assert
            Assert.Equal(3, config.Context.Count);
            Assert.Equal("data.csv", config.Context["input_file"]);
            Assert.Equal("json", config.Context["output_format"]);
            Assert.True((bool)config.Context["validate"]);
        }

        [Fact]
        public void ShouldCreateValidConfig_WhenBuildingWithCompleteConfiguration()
        {
            // Arrange & Act
            var config = new TaskConfigurationBuilder()
                .WithName("DataProcessingTask")
                .WithDescription("Process and analyze data")
                .WithExpectedOutput("Processed data report")
                .AssignTo("DataAnalyst")
                .WithPriority(TaskPriority.High)
                .WithAsync(false)
                .WithEstimatedDuration(TimeSpan.FromMinutes(45))
                .AddDependency("DataCollection")
                .AddDependency("DataValidation")
                .AddRequiredTool("PandasTool")
                .AddRequiredTool("MatplotlibTool")
                .WithContext("dataset", "sales_2024")
                .WithContext("format", "excel")
                .Build();

            // Assert
            Assert.Equal("DataProcessingTask", config.Name);
            Assert.Equal("Process and analyze data", config.Description);
            Assert.Equal("Processed data report", config.ExpectedOutput);
            Assert.Equal("DataAnalyst", config.AssignedAgent);
            Assert.Equal(TaskPriority.High, config.Priority);
            Assert.False(config.IsAsync);
            Assert.Equal(TimeSpan.FromMinutes(45), config.EstimatedDuration);
            Assert.Equal(2, config.Dependencies.Count);
            Assert.Equal(2, config.RequiredTools.Count);
            Assert.Equal(2, config.Context.Count);
        }
    }

    #endregion

    #region MemoryConfigurationBuilder Tests

    public class MemoryConfigurationBuilderTests
    {
        [Fact]
        public void ShouldSetProvider_WhenUsingWithProviderWithValidProvider()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act
            builder.WithProvider("Redis");
            var config = builder.Build();

            // Assert
            Assert.Equal("Redis", config.Provider);
        }

        [Fact]
        public void ShouldSetShortTermFlag_WhenEnablingShortTerm()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act
            builder.EnableShortTerm(true);
            var config = builder.Build();

            // Assert
            Assert.True(config.EnableShortTerm);
        }

        [Fact]
        public void ShouldSetLongTermFlag_WhenEnablingLongTerm()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act
            builder.EnableLongTerm(true);
            var config = builder.Build();

            // Assert
            Assert.True(config.EnableLongTerm);
        }

        [Fact]
        public void ShouldSetEpisodicFlag_WhenEnablingEpisodic()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act
            builder.EnableEpisodic(true);
            var config = builder.Build();

            // Assert
            Assert.True(config.EnableEpisodic);
        }

        [Fact]
        public void ShouldSetMaxEntries_WhenUsingWithMaxShortTermEntriesWithValidValue()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act
            builder.WithMaxShortTermEntries(2000);
            var config = builder.Build();

            // Assert
            Assert.Equal(2000, config.MaxShortTermEntries);
        }

        [Fact]
        public void ShouldThrowArgumentException_WhenUsingWithMaxShortTermEntriesWithInvalidValue()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.WithMaxShortTermEntries(0));
            Assert.Contains("Max entries must be positive", exception.Message);
        }

        [Fact]
        public void ShouldSetMaxEntries_WhenUsingWithMaxLongTermEntriesWithValidValue()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act
            builder.WithMaxLongTermEntries(50000);
            var config = builder.Build();

            // Assert
            Assert.Equal(50000, config.MaxLongTermEntries);
        }

        [Fact]
        public void ShouldSetRetentionPeriod_WhenUsingWithRetentionPeriodWithValidPeriod()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();
            var period = TimeSpan.FromDays(60);

            // Act
            builder.WithRetentionPeriod(period);
            var config = builder.Build();

            // Assert
            Assert.Equal(period, config.RetentionPeriod);
        }

        [Fact]
        public void ShouldThrowArgumentException_WhenUsingWithRetentionPeriodWithInvalidPeriod()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.WithRetentionPeriod(TimeSpan.Zero));
            Assert.Contains("Retention period must be positive", exception.Message);
        }

        [Fact]
        public void ShouldAddProviderSettings_WhenUsingWithProviderSetting()
        {
            // Arrange
            var builder = new MemoryConfigurationBuilder();

            // Act
            builder
                .WithProviderSetting("host", "localhost")
                .WithProviderSetting("port", 6379)
                .WithProviderSetting("password", "secret")
                .WithProviderSetting("ssl", true);
            var config = builder.Build();

            // Assert
            Assert.Equal(4, config.ProviderSettings.Count);
            Assert.Equal("localhost", config.ProviderSettings["host"]);
            Assert.Equal(6379, config.ProviderSettings["port"]);
            Assert.Equal("secret", config.ProviderSettings["password"]);
            Assert.True((bool)config.ProviderSettings["ssl"]);
        }

        [Fact]
        public void ShouldHaveDefaultValues_WhenBuildingWithDefaultConfiguration()
        {
            // Arrange & Act
            var config = new MemoryConfigurationBuilder().Build();

            // Assert
            Assert.Equal("InMemory", config.Provider);
            Assert.True(config.EnableShortTerm);
            Assert.False(config.EnableLongTerm);
            Assert.False(config.EnableEpisodic);
            Assert.Equal(1000, config.MaxShortTermEntries);
            Assert.Equal(10000, config.MaxLongTermEntries);
            Assert.Equal(TimeSpan.FromDays(30), config.RetentionPeriod);
            Assert.Empty(config.ProviderSettings);
        }

        [Fact]
        public void ShouldCreateValidConfig_WhenBuildingWithCompleteConfiguration()
        {
            // Arrange & Act
            var config = new MemoryConfigurationBuilder()
                .WithProvider("ChromaDB")
                .EnableShortTerm()
                .EnableLongTerm()
                .EnableEpisodic()
                .WithMaxShortTermEntries(5000)
                .WithMaxLongTermEntries(100000)
                .WithRetentionPeriod(TimeSpan.FromDays(90))
                .WithProviderSetting("collection", "crew_memories")
                .WithProviderSetting("embedding_model", "sentence-transformers")
                .WithProviderSetting("dimension", 768)
                .Build();

            // Assert
            Assert.Equal("ChromaDB", config.Provider);
            Assert.True(config.EnableShortTerm);
            Assert.True(config.EnableLongTerm);
            Assert.True(config.EnableEpisodic);
            Assert.Equal(5000, config.MaxShortTermEntries);
            Assert.Equal(100000, config.MaxLongTermEntries);
            Assert.Equal(TimeSpan.FromDays(90), config.RetentionPeriod);
            Assert.Equal(3, config.ProviderSettings.Count);
            Assert.Equal("crew_memories", config.ProviderSettings["collection"]);
            Assert.Equal("sentence-transformers", config.ProviderSettings["embedding_model"]);
            Assert.Equal(768, config.ProviderSettings["dimension"]);
        }
    }

    #endregion
}
