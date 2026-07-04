using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Crew.Planning;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Context;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Orkeon.Domain.Constants.Agent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Additional tests for ExecutionOrchestrator covering Phase 1 gaps:
/// Rate limiter integration, output validation pipeline, max iterations,
/// constructor overloads, and edge cases.
/// </summary>
public class ExecutionOrchestratorAdditionalTests
{
    #region Test Doubles

    private class TestLogger : ILogger<ExecutionOrchestrator>
    {
        private readonly List<string> _loggedMessages = [];
        public List<string> LoggedMessages => _loggedMessages;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _loggedMessages.Add($"[{logLevel}] {formatter(state, exception)}");
        }

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private class TestLlmProvider : IBasicLlmProvider
    {
        private Exception? _exceptionToThrow;
        private string _defaultResponse = "Default response";

        public string Name => "TestLLM";

        public void SetException(Exception exception) => _exceptionToThrow = exception;
        public void SetDefaultResponse(string response) => _defaultResponse = response;

        public async System.Threading.Tasks.Task<string> ChatAsync(
            string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Yield();
            if (_exceptionToThrow != null) throw _exceptionToThrow;
            return _defaultResponse;
        }

        public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(true);
    }

    private class TestAgentPlanner : IAgentPlanner
    {
        public System.Threading.Tasks.Task<TaskPlan> CreatePlanAsync(DomainTask task, CancellationToken cancellationToken = default)
        {
            var plan = new TaskPlan
            {
                TaskId = task.Id,
                Steps = [new PlanStep { Action = "Execute", Description = "Execute task" }],
                EstimatedDuration = TimeoutStandard,
                ConfidenceScore = 0.8
            };
            return System.Threading.Tasks.Task.FromResult(plan);
        }

        public System.Threading.Tasks.Task<TaskPlan> RefinePlanAsync(TaskPlan plan, PlanFeedback feedback, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(plan);

        public System.Threading.Tasks.Task<PlanValidationResult> ValidatePlanAsync(TaskPlan plan, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new PlanValidationResult { IsValid = true });
    }

    private class TestTool : Domain.Common.ITool
    {
        private bool _shouldSucceed = true;
        private string _result = "Tool result";

        public TestTool(string name, string description = "Test tool")
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>
        {
            { "input", new ParameterSchema("string", "Input", true) }
        });

        public void SetResult(bool success, string result) { _shouldSucceed = success; _result = result; }

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(new ToolResult { Success = _shouldSucceed, Output = _result });

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(_shouldSucceed, _result, _shouldSucceed ? null : "error"));

        public bool ValidateInput(string input) => true;
    }

    private class TestChatClient : Microsoft.Extensions.AI.IChatClient
    {
        private readonly Queue<Microsoft.Extensions.AI.ChatResponse> _responses = new();
        public int CallCount { get; private set; }

        public void EnqueueResponse(string text)
        {
            var msg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, text);
            _responses.Enqueue(new Microsoft.Extensions.AI.ChatResponse([msg]));
        }

        public void EnqueueFunctionCallResponse(string name, IDictionary<string, object?>? args = null)
        {
            var fc = new Microsoft.Extensions.AI.FunctionCallContent(Guid.NewGuid().ToString(), name, args);
            var msg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, [fc]);
            _responses.Enqueue(new Microsoft.Extensions.AI.ChatResponse([msg]));
        }

        public System.Threading.Tasks.Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (_responses.Count > 0)
                return System.Threading.Tasks.Task.FromResult(_responses.Dequeue());
            var defaultMsg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "Default");
            return System.Threading.Tasks.Task.FromResult(new Microsoft.Extensions.AI.ChatResponse([defaultMsg]));
        }

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private class TestRateLimiter : ILlmRateLimiter
    {
        private bool _shouldAcquire = true;
        private string _denialReason = "Rate limit exceeded";
        public int AcquireCount { get; private set; }
        public int DisposedLeaseCount { get; private set; }

        public void SetDenied(string reason = "Rate limit exceeded")
        {
            _shouldAcquire = false;
            _denialReason = reason;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Lease ownership is transferred to RateLimitAcquisition.Acquired; the orchestrator under test is responsible for disposing it (verified via DisposedLeaseCount).")]
        public System.Threading.Tasks.Task<RateLimitAcquisition> AcquireAsync(
            string provider, string agentRole, CancellationToken ct = default)
        {
            AcquireCount++;
            if (_shouldAcquire)
            {
                var lease = new TrackingLease(() => DisposedLeaseCount++);
                return System.Threading.Tasks.Task.FromResult(RateLimitAcquisition.Acquired(lease));
            }
            return System.Threading.Tasks.Task.FromResult(
                RateLimitAcquisition.Denied(_denialReason, TimeoutQuick));
        }

        private class TrackingLease(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }

    private class TestOutputValidationPipeline : IOutputValidationPipeline
    {
        private readonly Queue<OutputPipelineResult> _results = new();

        public void EnqueueResult(bool isValid, string? errorMessage = null, string? suggestedFix = null)
        {
            var result = new OutputValidationResult(isValid, errorMessage, suggestedFix, "TestValidator");
            _results.Enqueue(new OutputPipelineResult(isValid, [result], errorMessage));
        }

        public System.Threading.Tasks.Task<OutputPipelineResult> ValidateAsync(
            string output, OutputValidationContext context, CancellationToken ct = default)
        {
            if (_results.Count > 0)
                return System.Threading.Tasks.Task.FromResult(_results.Dequeue());
            return System.Threading.Tasks.Task.FromResult(
                new OutputPipelineResult(true, [new OutputValidationResult(true)]));
        }

        public IOutputValidationPipeline AddValidator(IOutputValidator validator) => this;
    }

    private class TestOutputParserFactory : IOutputParserFactory
    {
        public IStructuredOutputParser CreateParser(OutputFormat format) => new NoopParser();
        public IStructuredOutputParser<T> CreateParser<T>(OutputFormat format) where T : class, new()
            => throw new NotImplementedException();

        private class NoopParser : IStructuredOutputParser
        {
            public object? Parse(string llmOutput, Type targetType) => null;
            public bool TryParse(string llmOutput, Type targetType, out object? result) { result = null; return false; }
        }
    }

    private class TestMemoryScope : Orkeon.Application.Interfaces.Ports.IMemoryScope
    {
        public string ScopeId => "test";
        public string AgentId => "test-agent";
        public System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> action) => action();
        public System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> action) => action();
        public void Dispose() { }
    }

    private static DomainAgent CreateTestAgent(
        string role = "Test Agent",
        string goal = "Test",
        bool allowDelegation = true,
        List<IBaseTool>? tools = null,
        int maxIterations = AgentDefaults.MaxIterations)
    {
        return DomainAgent.Create(
            AgentRole.From(role),
            AgentGoal.From(goal),
            allowDelegation: allowDelegation,
            maxIterations: maxIterations,
            tools: tools?.Cast<Domain.Common.ITool>().ToList());
    }

    private static DomainTask CreateTestTask(string description = "Test task", string expectedOutput = "Expected")
    {
        return DomainTask.Create(TaskDescription.From(description), ExpectedOutput.From(expectedOutput));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the TestMemoryScope is transferred to the returned SimpleExecutionContext; the double's Dispose is a no-op and the object lives for the duration of the test.")]
    private static SimpleExecutionContext CreateTestContext(Dictionary<string, string>? variables = null)
    {
        return new SimpleExecutionContext(
            CrewId.From(Guid.NewGuid()),
            variables ?? [],
            new TestMemoryScope(),
            [],
            CancellationToken.None);
    }

    #endregion

    #region Rate Limiter Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldAcquireAndReleaseRateLimitLease_WhenExecutingSuccessfully()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        using var chatClient = new TestChatClient();
        var rateLimiter = new TestRateLimiter();
        var validationPipeline = new TestOutputValidationPipeline();
        var parserFactory = new TestOutputParserFactory();

        chatClient.EnqueueResponse("Task completed successfully");

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, planner, chatClient,
            Array.Empty<IBaseTool>(),
            validationPipeline, parserFactory, rateLimiter, new FakeFileSystemService());

        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = CreateTestContext();

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, rateLimiter.AcquireCount);
        Assert.Equal(1, rateLimiter.DisposedLeaseCount); // Lease was properly disposed
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFail_WhenRateLimiterDeniesRequest()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        using var chatClient = new TestChatClient();
        var rateLimiter = new TestRateLimiter();
        rateLimiter.SetDenied("Quota exhausted for this agent");
        var validationPipeline = new TestOutputValidationPipeline();
        var parserFactory = new TestOutputParserFactory();

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, planner, chatClient,
            Array.Empty<IBaseTool>(),
            validationPipeline, parserFactory, rateLimiter, new FakeFileSystemService());

        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = CreateTestContext();

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("rate limit exceeded", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReleaseRateLimitLease_EvenWhenExceptionOccurs()
    {
        // Arrange — the rate limit lease is now acquired per-LLM-call inside the
        // iteration loop (not once at the top of ExecuteWithProviderAsync). This
        // prevents deadlocks when tools like delegate_work trigger nested agent
        // executions that need the same concurrency slot.
        //
        // We test that the lease is still correctly released when the ChatClient
        // throws an exception after the lease has been acquired.
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        var rateLimiter = new TestRateLimiter();

        using var chatClient = new ThrowingChatClient(new InvalidOperationException("LLM failure"));

        var fullOrchestrator = new ExecutionOrchestrator(
            logger, llmProvider, planner, chatClient,
            Array.Empty<IBaseTool>(),
            new TestOutputValidationPipeline(), new TestOutputParserFactory(), rateLimiter, new FakeFileSystemService());

        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = CreateTestContext();

        // Act
        var result = await fullOrchestrator.ExecuteTaskCoreAsync(agent, task, context, CancellationToken.None);

        // Assert - lease should be disposed even when the LLM call throws
        Assert.False(result.Success);
        Assert.Equal(1, rateLimiter.AcquireCount);
        Assert.Equal(1, rateLimiter.DisposedLeaseCount);
    }

    /// <summary>
    /// A ChatClient that throws on the first call to GetResponseAsync,
    /// used to verify that the rate limit lease is released on exception.
    /// </summary>
    private class ThrowingChatClient(Exception exception) : Microsoft.Extensions.AI.IChatClient
    {
        public System.Threading.Tasks.Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw exception;

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    #endregion

    #region Output Validation Pipeline Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRetryWithCorrectionPrompt_WhenValidationFails()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        using var chatClient = new TestChatClient();
        var validationPipeline = new TestOutputValidationPipeline();
        var parserFactory = new TestOutputParserFactory();

        // First response fails validation, second passes
        chatClient.EnqueueResponse("Invalid JSON output");
        chatClient.EnqueueResponse("{\"valid\": true}");

        validationPipeline.EnqueueResult(false, "Invalid JSON format", "Wrap output in JSON braces");
        validationPipeline.EnqueueResult(true);

        var agent = CreateTestAgent();
        var task = CreateTestTask();
        // Set OutputJson so validation context is built
        var taskWithJson = DomainTask.Create(
            TaskDescription.From("Generate JSON report"),
            ExpectedOutput.From("JSON report"),
            outputOptions: new Domain.Task.TaskOutputOptions
            {
                OutputJson = Domain.Task.JsonSchema.From("{\"type\":\"object\"}")
            });

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, planner, chatClient,
            Array.Empty<IBaseTool>(),
            validationPipeline, parserFactory, new FakeFileSystemService());

        var context = CreateTestContext();

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(agent, taskWithJson, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, chatClient.CallCount); // Initial + retry
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStopAfterMaxOutputRetries_WhenValidationKeepsFailing()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        using var chatClient = new TestChatClient();
        var validationPipeline = new TestOutputValidationPipeline();
        var parserFactory = new TestOutputParserFactory();

        // All responses fail validation
        chatClient.EnqueueResponse("Bad output 1");
        chatClient.EnqueueResponse("Bad output 2");
        chatClient.EnqueueResponse("Bad output 3");

        validationPipeline.EnqueueResult(false, "Still invalid");
        validationPipeline.EnqueueResult(false, "Still invalid");
        validationPipeline.EnqueueResult(false, "Still invalid");

        var taskWithJson = DomainTask.Create(
            TaskDescription.From("Generate report"),
            ExpectedOutput.From("JSON"),
            outputOptions: new Domain.Task.TaskOutputOptions
            {
                OutputJson = Domain.Task.JsonSchema.From("{\"type\":\"object\"}")
            });

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, planner, chatClient,
            Array.Empty<IBaseTool>(),
            validationPipeline, parserFactory, new FakeFileSystemService());
        orchestrator.MaxOutputRetries = 2;

        var agent = CreateTestAgent();
        var context = CreateTestContext();

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(agent, taskWithJson, context, TestContext.Current.CancellationToken);

        // Assert - should still succeed (just with un-validated output)
        Assert.True(result.Success);
        // Should have logged the exhaustion
        Assert.Contains(logger.LoggedMessages, m =>
            m.Contains("Warning", StringComparison.OrdinalIgnoreCase) &&
            m.Contains("validation", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Max Iterations Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldStopAtMaxIterations_WhenToolCallsNeverEnd()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        using var chatClient = new TestChatClient();

        var tool = new TestTool("loop_tool", "A looping tool");

        // Enqueue enough function call responses to exceed max iterations
        for (int i = 0; i < 5; i++)
        {
            chatClient.EnqueueFunctionCallResponse("loop_tool",
                new Dictionary<string, object?> { { "input", $"iteration {i}" } });
        }

        var agent = CreateTestAgent(maxIterations: 3, tools: [tool]);

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, planner, chatClient,
            [tool], new FakeFileSystemService());

        var task = CreateTestTask();
        var context = CreateTestContext();

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert — max iterations exhausted: the agent never produced a final answer
        Assert.False(result.Success);
        Assert.Equal(Orkeon.Application.Interfaces.Services.AgentExitReason.MaxIterationsReached, result.ExitReason);
        Assert.Equal(3, result.IterationsUsed);
        // Max iterations (3) plus at most one recovery retry with tool_choice=none.
        Assert.True(chatClient.CallCount <= 4, "Should not exceed max iterations + recovery retry");
        Assert.Contains(logger.LoggedMessages, m =>
            m.Contains("Max iterations", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldCreateWithChatClient_WhenUsingChatClientConstructor()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        using var chatClient = new TestChatClient();

        // Act
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner, chatClient, new FakeFileSystemService());

        // Assert
        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void ShouldThrow_WhenChatClientIsNull()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new ExecutionOrchestrator(logger, llmProvider, planner, null!, new FakeFileSystemService()));
    }

    [Fact]
    public void ShouldSetMaxOutputRetries_ViaProperty()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);

        // Act
        orchestrator.MaxOutputRetries = 5;

        // Assert
        Assert.Equal(5, orchestrator.MaxOutputRetries);
    }

    [Fact]
    public void ShouldSetMaxIterations_ViaProperty()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);

        // Act
        orchestrator.MaxIterations = 50;

        // Assert
        Assert.Equal(50, orchestrator.MaxIterations);
    }

    #endregion

    #region Fallback Provider Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseLegacyProvider_WhenNoChatClientAvailable()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        llmProvider.SetDefaultResponse("Legacy provider response with important data");
        var planner = new TestAgentPlanner();

        // No chat client - just basic 3-arg constructor
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);

        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = CreateTestContext();

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Legacy provider response", result.Output);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFailure_WhenLegacyProviderThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        llmProvider.SetException(new HttpRequestException("Connection refused"));
        var planner = new TestAgentPlanner();

        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);

        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = CreateTestContext();

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.Error);
        Assert.True(result.ExecutionTime > TimeSpan.Zero);
    }

    #endregion

    #region MapExecutionContext Tests

    [Fact]
    public void ShouldAddAgentInfoToEmptyContext_WhenMapping()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);

        var agent = CreateTestAgent(role: RoleAnalyst);
        var context = CreateTestContext();

        // Act
        var mapped = orchestrator.MapExecutionContext(context, agent);

        // Assert
        Assert.Equal(2, mapped.Variables.Count); // agent_id + agent_role
        Assert.Equal(RoleAnalyst, mapped.Variables["agent_role"]);
        Assert.NotEmpty(mapped.Variables["agent_id"]);
    }

    [Fact]
    public void ShouldPreserveExistingVariables_WhenMapping()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new TestAgentPlanner();
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);

        var agent = CreateTestAgent(role: RoleWorker);
        var context = CreateTestContext(new Dictionary<string, string>
        {
            ["project"] = "Orkeon",
            ["version"] = "1.0"
        });

        // Act
        var mapped = orchestrator.MapExecutionContext(context, agent);

        // Assert
        Assert.Equal(4, mapped.Variables.Count); // 2 existing + 2 agent
        Assert.Equal("Orkeon", mapped.Variables["project"]);
        Assert.Equal("1.0", mapped.Variables["version"]);
        Assert.Equal(RoleWorker, mapped.Variables["agent_role"]);
    }

    #endregion

    #region Planning Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFallbackPlan_WhenPlannerThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var planner = new FailingPlanner();
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);

        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = CreateTestContext();

        // Act
        var plan = await orchestrator.PlanExecutionAsync(agent, task, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan.Steps);
        Assert.Equal(0.5, plan.ConfidenceScore);
    }

    private class FailingPlanner : IAgentPlanner
    {
        public System.Threading.Tasks.Task<TaskPlan> CreatePlanAsync(DomainTask task, CancellationToken ct = default)
            => throw new InvalidOperationException("Planner failure");

        public System.Threading.Tasks.Task<TaskPlan> RefinePlanAsync(TaskPlan plan, PlanFeedback feedback, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(plan);

        public System.Threading.Tasks.Task<PlanValidationResult> ValidatePlanAsync(TaskPlan plan, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(new PlanValidationResult { IsValid = true });
    }

    #endregion
}
