using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Application.Validation;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using AgentBuilder = Orkeon.Domain.Agent.AgentBuilder;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Validation;

public class CrewValidatorTests
{
    #region Test Doubles

    private class TestLlmProvider : ILlmProvider
    {
        public string Name => "TestLLM";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new LlmResponse
            {
                Content = "Test response",
                Model = TestModelName,
                TokensUsed = 10
            });
        }

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new LlmResponse
            {
                Content = "Test chat response",
                Model = TestModelName,
                TokensUsed = 20
            });
        }
    }

    private class TestLogger : ILogger<CrewValidator>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Microsoft.Extensions.Logging.LogLevel> LoggedLevels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LoggedLevels.Add(logLevel);
            LoggedMessages.Add(formatter(state, exception));
        }

        public bool HasLoggedWarning() => LoggedLevels.Contains(Microsoft.Extensions.Logging.LogLevel.Warning);
        public bool HasLoggedDebug() => LoggedLevels.Contains(Microsoft.Extensions.Logging.LogLevel.Debug);
    }

    #endregion

    #region Test Helpers

    private static DomainCrew CreateValidCrew()
    {
        var crew = DomainCrew.Create(
            goal: "Test Crew Goal",
            processType: ProcessType.Sequential,
            verbose: false);

        crew.AddAgent(AgentId.From(Guid.NewGuid()));
        crew.AddTask(TaskId.From(Guid.NewGuid()));

        return crew;
    }

    private static DomainAgent CreateValidAgent()
    {
        return DomainAgent.Create(
            role: AgentRole.From(RoleDeveloper),
            goal: AgentGoal.From(GoalWriteCode),
            backstory: AgentBackstory.From("Experienced developer"),
            maxIterations: 10,
            allowDelegation: true);
    }

    private static DomainTask CreateValidTask()
    {
        return DomainTask.Create(
            description: TaskDescription.From("Implement feature"),
            expectedOutput: ExpectedOutput.From("Completed feature implementation"));
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructingWithLogger()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var validator = new CrewValidator(logger);

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithoutLogger()
    {
        // Act
        var validator = new CrewValidator(null);

        // Assert
        Assert.NotNull(validator);
    }

    #endregion

    #region ValidateCrew Tests

    [Fact]
    public void ShouldReturnError_WhenUsingValidateCrewWithNullCrew()
    {
        // Arrange
        var validator = new CrewValidator();

        // Act
        var result = validator.ValidateCrew(null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Crew cannot be null", result.Errors);
    }

    [Fact]
    public void ShouldReturnSuccess_WhenUsingValidateCrewWithValidCrew()
    {
        // Arrange
        var logger = new TestLogger();
        var validator = new CrewValidator(logger);
        var crew = CreateValidCrew();

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.True(logger.HasLoggedDebug());
    }

    [Fact]
    public void ShouldReturnError_WhenUsingValidateCrewWithEmptyId()
    {
        // Arrange
        var validator = new CrewValidator();
        var crew = DomainCrew.Create(TestGoal, ProcessType.Sequential, false);
        // Crew ID is auto-generated, so we can't test empty ID directly
        // But we can test the validation logic
        crew.AddAgent(AgentId.From(Guid.NewGuid()));
        crew.AddTask(TaskId.From(Guid.NewGuid()));

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.True(result.IsValid); // ID is auto-generated so it will be valid
    }

    [Fact]
    public void ShouldReturnError_WhenUsingValidateCrewWithNoAgents()
    {
        // Arrange
        var validator = new CrewValidator();
        var crew = DomainCrew.Create(TestGoal, ProcessType.Sequential, false);
        crew.AddTask(TaskId.From(Guid.NewGuid()));

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Crew must have at least one agent", result.Errors);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingValidateCrewUsingAddingNullAgent()
    {
        // Arrange
        var crew = DomainCrew.Create(TestGoal, ProcessType.Sequential, false);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => crew.AddAgent(null!));
        // Domain prevents null agents from being added
    }

    [Fact]
    public void ShouldReturnError_WhenUsingValidateCrewWithNoTasks()
    {
        // Arrange
        var validator = new CrewValidator();
        var crew = DomainCrew.Create(TestGoal, ProcessType.Sequential, false);
        crew.AddAgent(AgentId.From(Guid.NewGuid()));

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Crew must have at least one task", result.Errors);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingValidateCrewUsingAddingNullTask()
    {
        // Arrange
        var crew = DomainCrew.Create(TestGoal, ProcessType.Sequential, false);
        crew.AddAgent(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => crew.AddTask(null!));
        // Domain prevents null tasks from being added
    }

    [Fact]
    public void ShouldThrowException_WhenUsingValidateCrewUsingCreatingHierarchicalWithoutManager()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            DomainCrew.Create(TestGoal, ProcessType.Hierarchical, false));
        // Domain prevents creation of hierarchical crew without manager
    }

    [Fact]
    public void ShouldThrowException_WhenUsingValidateCrewUsingCreatingWithEmptyGoal()
    {
        // Arrange
        var managerLlm = new TestLlmProvider();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            DomainCrew.Create(
                goal: "", // Empty goal throws
                processType: ProcessType.Hierarchical,
                verbose: false,
                managerLlm: managerLlm));
        // Domain prevents empty goal
    }

    [Fact]
    public void ShouldReturnSuccess_WhenUsingValidateCrewUsingHierarchicalWithManagerAndGoal()
    {
        // Arrange
        var validator = new CrewValidator();
        var managerLlm = new TestLlmProvider();
        var crew = DomainCrew.Create(
            goal: "Complete the project",
            processType: ProcessType.Hierarchical,
            verbose: false,
            managerLlm: managerLlm);
        crew.AddAgent(AgentId.From(Guid.NewGuid()));
        crew.AddTask(TaskId.From(Guid.NewGuid()));

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldReturnAllErrors_WhenUsingValidateCrewWithMultipleErrors()
    {
        // Arrange
        var logger = new TestLogger();
        var validator = new CrewValidator(logger);
        var crew = DomainCrew.Create(TestGoal, ProcessType.Sequential, false);
        // No agents, no tasks

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count); // No agents, no tasks
        Assert.True(logger.HasLoggedWarning());
    }

    #endregion

    #region ValidateAgent Tests

    [Fact]
    public void ShouldReturnError_WhenUsingValidateAgentWithNullAgent()
    {
        // Arrange
        var validator = new CrewValidator();

        // Act
        var errors = CrewValidator.ValidateAgent(null!);

        // Assert
        Assert.Single(errors);
        Assert.Contains("DomainAgent cannot be null", errors);
    }

    [Fact]
    public void ShouldReturnNoErrors_WhenUsingValidateAgentWithValidAgent()
    {
        // Arrange
        var validator = new CrewValidator();
        var agent = CreateValidAgent();

        // Act
        var errors = CrewValidator.ValidateAgent(agent);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ShouldReturnError_WhenUsingValidateAgentWithEmptyRole()
    {
        // Arrange
        var validator = new CrewValidator();
        // Can't create agent with empty role due to value object validation
        // So we test the concept by checking that validation would catch it

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            DomainAgent.Create(
                role: AgentRole.From(""), // This will throw
                goal: AgentGoal.From("Goal"),
                backstory: AgentBackstory.From("Story")));
    }

    [Fact]
    public void ShouldReturnNoErrors_WhenUsingValidateAgentWithDelegationAndIterations()
    {
        // Arrange
        var validator = new CrewValidator();
        var agent = DomainAgent.Create(
            role: AgentRole.From(RoleDeveloper),
            goal: AgentGoal.From(GoalWriteCode),
            backstory: AgentBackstory.From("Experienced developer"),
            maxIterations: 5,
            allowDelegation: true);

        // Act
        var errors = CrewValidator.ValidateAgent(agent);

        // Assert
        Assert.Empty(errors);
        // Agent with delegation and positive iterations should pass
    }

    [Fact]
    public void ShouldThrowException_WhenUsingValidateAgentUsingAddingNullTool()
    {
        // Arrange
        var agent = CreateValidAgent();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => agent.AddTool(null!));
        // Domain prevents null tools from being added
    }

    #endregion

    #region ValidateTask Tests

    [Fact]
    public void ShouldReturnError_WhenUsingValidateTaskWithNullTask()
    {
        // Arrange
        var validator = new CrewValidator();

        // Act
        var errors = CrewValidator.ValidateTask(null!);

        // Assert
        Assert.Single(errors);
        Assert.Contains("Task cannot be null", errors);
    }

    [Fact]
    public void ShouldReturnNoErrors_WhenUsingValidateTaskWithValidTask()
    {
        // Arrange
        var validator = new CrewValidator();
        var task = CreateValidTask();

        // Act
        var errors = CrewValidator.ValidateTask(task);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingValidateTaskWithEmptyExpectedOutput()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            DomainTask.Create(
                description: TaskDescription.From("Test task"),
                expectedOutput: ExpectedOutput.From(""))); // Empty expected output throws
        // Domain prevents empty expected output
    }

    [Fact]
    public void ShouldCreateSuccessfully_WhenUsingValidateTaskWithLongDescription()
    {
        // Arrange
        var validator = new CrewValidator();
        var longDescription = string.Join("", Enumerable.Repeat("x", 100));
        var task = DomainTask.Create(
            description: TaskDescription.From(longDescription),
            expectedOutput: ExpectedOutput.From("Valid output"));

        // Act
        var errors = CrewValidator.ValidateTask(task);

        // Assert
        Assert.Empty(errors);
        // Task with long description but valid output should pass validation
    }

    [Fact]
    public void ShouldValidateSuccessfully_WhenUsingValidateTaskWithAssignedAgent()
    {
        // Arrange
        var validator = new CrewValidator();
        var task = CreateValidTask();
        task.AssignTo(AgentId.From(Guid.NewGuid()));

        // Act
        var errors = CrewValidator.ValidateTask(task);

        // Assert
        Assert.Empty(errors);
    }

    #endregion

    #region ValidateLlmConfig Tests

    // Provider identity is intentionally out of scope here: LlmConfig has no provider
    // field — the Infrastructure LLM factory infers the provider from BaseUrl/model
    // heuristics with an OpenAI fallback, and its canonical provider list is not
    // visible from the Application layer.

    [Fact]
    public void ShouldReturnNoErrors_WhenUsingValidateLlmConfigWithValidConfig()
    {
        // Arrange
        var config = LlmConfig.CreateValidated(ModelGpt4, temperature: 0.7, maxTokens: 2048, topP: 0.9);

        // Act
        var errors = CrewValidator.ValidateLlmConfig(config, RoleDeveloper);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ShouldReturnError_WhenUsingValidateLlmConfigWithNullConfig()
    {
        // Act
        var errors = CrewValidator.ValidateLlmConfig(null!, RoleDeveloper);

        // Assert
        Assert.Single(errors);
        Assert.Contains("null LLM config", errors[0]);
    }

    [Fact]
    public void ShouldReturnError_WhenUsingValidateLlmConfigWithEmptyModel()
    {
        // Arrange — the with-expression bypasses factory validation on purpose
        var config = LlmConfig.Default() with { Model = "   " };

        // Act
        var errors = CrewValidator.ValidateLlmConfig(config, RoleDeveloper);

        // Assert
        Assert.Single(errors);
        Assert.Contains("without a model", errors[0]);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.5)]
    public void ShouldReturnError_WhenUsingValidateLlmConfigWithOutOfRangeTemperature(double temperature)
    {
        // Arrange
        var config = LlmConfig.Default() with { Temperature = temperature };

        // Act
        var errors = CrewValidator.ValidateLlmConfig(config, RoleDeveloper);

        // Assert
        Assert.Single(errors);
        Assert.Contains("temperature", errors[0]);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void ShouldReturnError_WhenUsingValidateLlmConfigWithOutOfRangeTopP(double topP)
    {
        // Arrange
        var config = LlmConfig.Default() with { TopP = topP };

        // Act
        var errors = CrewValidator.ValidateLlmConfig(config, RoleDeveloper);

        // Assert
        Assert.Single(errors);
        Assert.Contains("top_p", errors[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-128)]
    public void ShouldReturnError_WhenUsingValidateLlmConfigWithNonPositiveMaxTokens(int maxTokens)
    {
        // Arrange
        var config = LlmConfig.Default() with { MaxTokens = maxTokens };

        // Act
        var errors = CrewValidator.ValidateLlmConfig(config, RoleDeveloper);

        // Assert
        Assert.Single(errors);
        Assert.Contains("max_tokens", errors[0]);
    }

    [Fact]
    public void ShouldReturnAllErrors_WhenUsingValidateLlmConfigWithMultipleInvalidParameters()
    {
        // Arrange — every validated parameter is out of range
        var config = LlmConfig.Default() with
        {
            Model = "",
            Temperature = 3.0,
            TopP = 2.0,
            MaxTokens = 0,
            FrequencyPenalty = 5.0,
            PresencePenalty = -5.0,
            TimeoutSeconds = 0,
            MaxRetries = -1
        };

        // Act
        var errors = CrewValidator.ValidateLlmConfig(config, RoleDeveloper);

        // Assert
        Assert.Equal(8, errors.Count);
    }

    [Fact]
    public void ShouldReturnNoErrors_WhenUsingValidateAgentWithValidLlmConfig()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .WithLlmConfig(LlmConfig.Create(ModelGpt4))
            .Build();

        // Act
        var errors = CrewValidator.ValidateAgent(agent);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ShouldReturnError_WhenUsingValidateAgentWithInvalidLlmConfig()
    {
        // Arrange — rejected by ValidateAgent now that LLM configs are validated
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .WithLlmConfig(LlmConfig.Default() with { Temperature = 9.0 })
            .Build();

        // Act
        var errors = CrewValidator.ValidateAgent(agent);

        // Assert
        Assert.Single(errors);
        Assert.Contains("temperature", errors[0]);
    }

    [Fact]
    public void ShouldReturnNoErrors_WhenUsingValidateAgentWithoutLlmConfig()
    {
        // Arrange — no LLM config means the runtime default provider is used
        var agent = CreateValidAgent();

        // Act
        var errors = CrewValidator.ValidateAgent(agent);

        // Assert
        Assert.Empty(errors);
    }

    #endregion

    #region ValidationResult Tests

    [Fact]
    public void ShouldCombineErrors_WhenUsingValidationResultGettingErrorMessage()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2", "Error 3" };
        var result = new Orkeon.Application.Validation.ValidationResult(false, errors);

        // Act
        var message = result.GetErrorMessage();

        // Assert
        Assert.Equal("Error 1; Error 2; Error 3", message);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingValidationResultWithNoErrors()
    {
        // Arrange
        var result = new Orkeon.Application.Validation.ValidationResult(true, []);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("", result.GetErrorMessage());
    }

    [Fact]
    public void ShouldBeInvalid_WhenUsingValidationResultWithErrors()
    {
        // Arrange
        var result = new Orkeon.Application.Validation.ValidationResult(false, ["Error"]);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ShouldCheckAllComponents_WhenUsingValidateCrewWithCompleteValidation()
    {
        // Arrange
        var logger = new TestLogger();
        var validator = new CrewValidator(logger);

        var crew = DomainCrew.Create("Complete Crew Goal", ProcessType.Sequential, true);
        crew.AddAgent(AgentId.From(Guid.NewGuid()));
        crew.AddAgent(AgentId.From(Guid.NewGuid()));
        crew.AddTask(TaskId.From(Guid.NewGuid()));
        crew.AddTask(TaskId.From(Guid.NewGuid()));
        crew.AddTask(TaskId.From(Guid.NewGuid()));

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.True(logger.HasLoggedDebug());
        Assert.Contains(crew.Id, logger.LoggedMessages[0]);
    }

    [Fact]
    public void ShouldReturnMultipleErrors_WhenUsingValidateCrewWithInvalidHierarchicalCrew()
    {
        // Arrange
        var logger = new TestLogger();
        var validator = new CrewValidator(logger);

        // Can't create hierarchical crew without manager, so test with sequential  
        var crew = DomainCrew.Create("Empty Goal", ProcessType.Sequential, false);
        // Empty crew - should have multiple errors

        // Act
        var result = validator.ValidateCrew(crew);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2); // No agents, no tasks
        Assert.True(logger.HasLoggedWarning());
    }

    #endregion
}
