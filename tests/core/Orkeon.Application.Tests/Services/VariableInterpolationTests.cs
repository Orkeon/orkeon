using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Planning;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Microsoft.Extensions.Logging;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Tests that {variable} placeholders in task descriptions are correctly
/// interpolated with values from SimpleExecutionContext.Variables when
/// the ExecutionOrchestrator builds the user prompt for the LLM.
/// </summary>
public class VariableInterpolationTests
{
    #region Test Doubles

    private class TestLogger : ILogger<ExecutionOrchestrator>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        private class NoOpDisposable : IDisposable { public void Dispose() { } }
    }

    private class CapturingLlmProvider : IBasicLlmProvider
    {
        private readonly List<string> _receivedMessages = [];
        public IReadOnlyList<string> ReceivedMessages => _receivedMessages;
        public string Name => "CapturingLlm";

        public System.Threading.Tasks.Task<string> ChatAsync(string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            _receivedMessages.Add(message);
            return System.Threading.Tasks.Task.FromResult("LLM response");
        }

        public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(true);
    }

    private class TestAgentPlanner : IAgentPlanner
    {
        public System.Threading.Tasks.Task<TaskPlan> CreatePlanAsync(DomainTask task, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new TaskPlan
            {
                TaskId = task.Id,
                Steps = [new PlanStep { Action = "Execute", Description = "Do it", EstimatedDuration = TimeSpan.FromMinutes(1) }],
                EstimatedDuration = TimeSpan.FromMinutes(1),
                ConfidenceScore = 0.9
            });

        public System.Threading.Tasks.Task<TaskPlan> RefinePlanAsync(TaskPlan plan, PlanFeedback feedback, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(plan);

        public System.Threading.Tasks.Task<PlanValidationResult> ValidatePlanAsync(TaskPlan plan, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new PlanValidationResult { IsValid = true });
    }

    private class TestMemoryScope : IMemoryScope
    {
        public string ScopeId => "test-scope";
        public string AgentId => "test-agent";
        public async System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> action) => await action();
        public async System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> action) => await action();
        public void Dispose() { }
    }

    #endregion

    #region Helpers

    private static DomainAgent CreateTestAgent()
    {
        return DomainAgent.Create(
            AgentRole.From("Test Agent"),
            AgentGoal.From("Execute tasks"),
            allowDelegation: false,
            maxIterations: 3);
    }

    private static DomainTask CreateTestTask(string description, string expectedOutput = "Some output")
    {
        return DomainTask.Create(
            TaskDescription.From(description),
            ExpectedOutput.From(expectedOutput));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the TestMemoryScope is transferred to the returned SimpleExecutionContext; the double's Dispose is a no-op and the object lives for the duration of the test.")]
    private static SimpleExecutionContext CreateContextWithVariables(Dictionary<string, string> variables)
    {
        return new SimpleExecutionContext(
            CrewId.From(Guid.NewGuid()),
            variables,
            new TestMemoryScope(),
            [],
            CancellationToken.None);
    }

    #endregion

    #region Interpolation Tests

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTaskCoreAsync_WithVariableInDescription_InterpolatesVariable()
    {
        // Arrange
        var llm = new CapturingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(new TestLogger(), llm, new TestAgentPlanner());
        var agent = CreateTestAgent();
        var task = CreateTestTask("Analyze the question: {question}");
        var context = CreateContextWithVariables(new Dictionary<string, string>
        {
            ["question"] = "What is clean architecture?"
        });

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert — the LLM should receive the interpolated description
        Assert.NotEmpty(llm.ReceivedMessages);
        var userPrompt = llm.ReceivedMessages[^1];
        Assert.Contains("What is clean architecture?", userPrompt);
        Assert.DoesNotContain("{question}", userPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTaskCoreAsync_WithMultipleVariables_InterpolatesAll()
    {
        // Arrange
        var llm = new CapturingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(new TestLogger(), llm, new TestAgentPlanner());
        var agent = CreateTestAgent();
        var task = CreateTestTask("Research {topic} in {country}");
        var context = CreateContextWithVariables(new Dictionary<string, string>
        {
            ["topic"] = "AI regulations",
            ["country"] = "France"
        });

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(llm.ReceivedMessages);
        var userPrompt = llm.ReceivedMessages[^1];
        Assert.Contains("AI regulations", userPrompt);
        Assert.Contains("France", userPrompt);
        Assert.DoesNotContain("{topic}", userPrompt);
        Assert.DoesNotContain("{country}", userPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTaskCoreAsync_WithNoVariables_LeavesDescriptionIntact()
    {
        // Arrange
        var llm = new CapturingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(new TestLogger(), llm, new TestAgentPlanner());
        var agent = CreateTestAgent();
        var task = CreateTestTask("Analyze the data thoroughly");
        var context = CreateContextWithVariables([]);

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(llm.ReceivedMessages);
        var userPrompt = llm.ReceivedMessages[^1];
        Assert.Contains("Analyze the data thoroughly", userPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTaskCoreAsync_WithUnmatchedPlaceholder_LeavesPlaceholderIntact()
    {
        // Arrange
        var llm = new CapturingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(new TestLogger(), llm, new TestAgentPlanner());
        var agent = CreateTestAgent();
        var task = CreateTestTask("Analyze {unknown_var} please");
        var context = CreateContextWithVariables(new Dictionary<string, string>
        {
            ["question"] = "some question"
        });

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert — unmatched placeholders should survive
        Assert.NotEmpty(llm.ReceivedMessages);
        var userPrompt = llm.ReceivedMessages[^1];
        Assert.Contains("{unknown_var}", userPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTaskCoreAsync_WithVariableInExpectedOutput_InterpolatesExpectedOutput()
    {
        // Arrange
        var llm = new CapturingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(new TestLogger(), llm, new TestAgentPlanner());
        var agent = CreateTestAgent();
        var task = CreateTestTask(
            description: "Analyze {topic}",
            expectedOutput: "A report about {topic}");
        var context = CreateContextWithVariables(new Dictionary<string, string>
        {
            ["topic"] = "machine learning"
        });

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert — both description and expected output should be interpolated
        Assert.NotEmpty(llm.ReceivedMessages);
        var userPrompt = llm.ReceivedMessages[^1];
        Assert.Contains("Analyze machine learning", userPrompt);
        Assert.Contains("A report about machine learning", userPrompt);
        Assert.DoesNotContain("{topic}", userPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTaskCoreAsync_VariableInterpolationIsCaseInsensitive()
    {
        // Arrange
        var llm = new CapturingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(new TestLogger(), llm, new TestAgentPlanner());
        var agent = CreateTestAgent();
        var task = CreateTestTask("Answer: {Question}");
        var context = CreateContextWithVariables(new Dictionary<string, string>
        {
            ["question"] = "Why is the sky blue?"
        });

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert — case-insensitive interpolation
        Assert.NotEmpty(llm.ReceivedMessages);
        var userPrompt = llm.ReceivedMessages[^1];
        Assert.Contains("Why is the sky blue?", userPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTaskCoreAsync_ContextVariablesSection_StillAppended()
    {
        // Arrange
        var llm = new CapturingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(new TestLogger(), llm, new TestAgentPlanner());
        var agent = CreateTestAgent();
        var task = CreateTestTask("Answer: {question}");
        var context = CreateContextWithVariables(new Dictionary<string, string>
        {
            ["question"] = "Hello world"
        });

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert — context section with variable values should still be appended
        Assert.NotEmpty(llm.ReceivedMessages);
        var userPrompt = llm.ReceivedMessages[^1];
        Assert.Contains("Context variables:", userPrompt);
        Assert.Contains("- question: Hello world", userPrompt);
    }

    #endregion
}
