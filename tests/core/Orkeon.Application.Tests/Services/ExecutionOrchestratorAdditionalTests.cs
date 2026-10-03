using DomainAgent = Orkeon.Domain.Agent.Agent;
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
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Application.Tests.Doubles;

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

    private class TestTool : Domain.Tools.IBaseTool
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

        public void EnqueueResponse(Microsoft.Extensions.AI.ChatResponse response) => _responses.Enqueue(response);

        public void EnqueueFunctionCallResponse(string name, IDictionary<string, object?>? args = null)
        {
            var fc = new Microsoft.Extensions.AI.FunctionCallContent(Guid.NewGuid().ToString(), name, args);
            var msg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, [fc]);
            _responses.Enqueue(new Microsoft.Extensions.AI.ChatResponse([msg]));
        }

        /// <summary>The next call fails the way the adapter fails a call the provider never answered (LLM-11).</summary>
        public Exception? NextFailure { get; set; }

        public System.Threading.Tasks.Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (NextFailure is { } failure)
            {
                NextFailure = null;
                return System.Threading.Tasks.Task.FromException<Microsoft.Extensions.AI.ChatResponse>(failure);
            }
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
            string provider, CancellationToken ct = default)
        {
            AcquireCount++;
            if (_shouldAcquire)
            {
                var lease = new TrackingLease(() => DisposedLeaseCount++);
                return System.Threading.Tasks.Task.FromResult(RateLimitAcquisition.Acquired(lease));
            }
            // Retried after a millisecond: the entrance retries a refusal on its RetryAfter.
            return System.Threading.Tasks.Task.FromResult(
                RateLimitAcquisition.Denied(_denialReason, TimeSpan.FromMilliseconds(1)));
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
            tools: tools?.Cast<Domain.Tools.IBaseTool>().ToList());
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

    // GAP-38: the host's RateLimiting is applied once, at the entrance of each provider — the
    // orchestrator takes no limiter and its gate no lease. An agent's turn takes its lease where the
    // call meets the provider, releases it when the call ends, and a refusal fails the turn.

    [Fact]
    public async System.Threading.Tasks.Task An_agent_turn_takes_and_releases_its_lease_at_the_providers_entrance()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var rateLimiter = new TestRateLimiter();
        var provider = new ScriptedFullLlmProvider();
        provider.EnqueueText("Task completed successfully");
        using var chatClient = new Orkeon.Infrastructure.LLMs.Adapters.LlmProviderToChatClientAdapter(
            Orkeon.Infrastructure.LLMs.RateLimitedLlmProvider.Wrap(provider, rateLimiter));

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, chatClient,
            Array.Empty<IBaseTool>(),
            new TestOutputValidationPipeline(), new TestOutputParserFactory(), new FakeFileSystemService());

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(CreateTestAgent(), CreateTestTask(), CreateTestContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, rateLimiter.AcquireCount);
        Assert.Equal(1, rateLimiter.DisposedLeaseCount); // Lease was properly disposed
    }

    [Fact]
    public async System.Threading.Tasks.Task A_turn_the_hosts_limiter_refuses_fails_as_a_failed_call_naming_the_reason()
    {
        // Arrange — refused on the first try and on each of the five retries the entrance makes.
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var rateLimiter = new TestRateLimiter();
        rateLimiter.SetDenied("Quota exhausted for this provider");
        var provider = new ScriptedFullLlmProvider();
        using var chatClient = new Orkeon.Infrastructure.LLMs.Adapters.LlmProviderToChatClientAdapter(
            Orkeon.Infrastructure.LLMs.RateLimitedLlmProvider.Wrap(provider, rateLimiter));

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, chatClient,
            Array.Empty<IBaseTool>(),
            new TestOutputValidationPipeline(), new TestOutputParserFactory(), new FakeFileSystemService());

        // Act
        var result = await orchestrator.ExecuteTaskCoreAsync(CreateTestAgent(), CreateTestTask(), CreateTestContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(Orkeon.Application.Interfaces.Services.AgentExitReason.LlmCallFailed, result.ExitReason);
        Assert.Contains("Quota exhausted for this provider", result.Error, StringComparison.Ordinal);
        Assert.Equal(6, rateLimiter.AcquireCount);
        Assert.Empty(provider.ReceivedTurns);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReleaseRateLimitLease_EvenWhenExceptionOccurs()
    {
        // Arrange — the lease covers the provider call only: a call that throws still releases it,
        // and nothing is held across a tool (delegate_work runs another agent's turn).
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var rateLimiter = new TestRateLimiter();
        using var chatClient = new Orkeon.Infrastructure.LLMs.Adapters.LlmProviderToChatClientAdapter(
            Orkeon.Infrastructure.LLMs.RateLimitedLlmProvider.Wrap(new ThrowingLlmProvider(new InvalidOperationException("LLM failure")), rateLimiter));

        var fullOrchestrator = new ExecutionOrchestrator(
            logger, llmProvider, chatClient,
            Array.Empty<IBaseTool>(),
            new TestOutputValidationPipeline(), new TestOutputParserFactory(), new FakeFileSystemService());

        // Act
        var result = await fullOrchestrator.ExecuteTaskCoreAsync(CreateTestAgent(), CreateTestTask(), CreateTestContext(), CancellationToken.None);

        // Assert - lease should be disposed even when the LLM call throws
        Assert.False(result.Success);
        Assert.Equal(1, rateLimiter.AcquireCount);
        Assert.Equal(1, rateLimiter.DisposedLeaseCount);
    }

    /// <summary>A provider whose every call fails with the exception it was given.</summary>
    private sealed class ThrowingLlmProvider(Exception exception) : Domain.SharedKernel.ILlmProvider
    {
        public string Name => "throwing";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromException<LlmResponse>(exception);

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromException<LlmResponse>(exception);
    }

    #endregion

    #region Output Validation Pipeline Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRetryWithCorrectionPrompt_WhenValidationFails()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
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
            logger, llmProvider, chatClient,
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
    public async System.Threading.Tasks.Task ShouldFailWithTheProvidersReason_AndAskNothingMore_WhenTheCallFailed()
    {
        // LLM-11: a call the provider never answered is a failed task with the provider's
        // sentence — no tool-free retry in the loop, no validation correction round here.
        const string timeout = "Kimi did not answer within Llm:TimeoutSeconds = 180 s: the HTTP timeout elapsed before any response arrived";
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        using var chatClient = new TestChatClient { NextFailure = new HttpRequestException(timeout) };
        var validationPipeline = new TestOutputValidationPipeline();
        validationPipeline.EnqueueResult(false, "empty is not a JSON object");
        var parserFactory = new TestOutputParserFactory();

        var taskWithJson = DomainTask.Create(
            TaskDescription.From("Score the contacts as JSON"),
            ExpectedOutput.From("JSON"),
            outputOptions: new Domain.Task.TaskOutputOptions
            {
                OutputJson = Domain.Task.JsonSchema.From("{\"type\":\"object\"}")
            });

        var orchestrator = new ExecutionOrchestrator(
            logger, llmProvider, chatClient,
            Array.Empty<IBaseTool>(),
            validationPipeline, parserFactory, new FakeFileSystemService());

        var result = await orchestrator.ExecuteTaskCoreAsync(CreateTestAgent(), taskWithJson, CreateTestContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(Orkeon.Application.Interfaces.Services.AgentExitReason.LlmCallFailed, result.ExitReason);
        Assert.Equal(timeout, result.Error);
        Assert.Equal(timeout, result.LastError);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal(1, chatClient.CallCount);   // no retry of any kind
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStopAfterMaxOutputRetries_WhenValidationKeepsFailing()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
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
            logger, llmProvider, chatClient,
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
            logger, llmProvider, chatClient,
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
        using var chatClient = new TestChatClient();

        // Act
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider, chatClient, new FakeFileSystemService());

        // Assert
        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void ShouldThrow_WhenChatClientIsNull()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new ExecutionOrchestrator(logger, llmProvider, null!, new FakeFileSystemService()));
    }

    [Fact]
    public void ShouldSetMaxOutputRetries_ViaProperty()
    {
        // Arrange
        var logger = new TestLogger();
        var llmProvider = new TestLlmProvider();
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider);

        // Act
        orchestrator.MaxOutputRetries = 5;

        // Assert
        Assert.Equal(5, orchestrator.MaxOutputRetries);
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

        // No chat client - just the basic constructor
        var orchestrator = new ExecutionOrchestrator(logger, llmProvider);

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

        var orchestrator = new ExecutionOrchestrator(logger, llmProvider);

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

    #region Usage Metering Tests (STUDIO-29, STUDIO-42)

    [Fact]
    public async System.Threading.Tasks.Task Every_call_of_a_task_is_metered_for_the_crew_it_runs_in_the_correction_round_included()
    {
        // The execution context knows which crew the task runs for, and the task's scope says
        // so: the usage event used to leave the crew empty, so the wire's cost.updated could
        // not say which run it was metering. The validation round goes through the same
        // provider and the same scope — metered once, for the same crew.
        var sink = new MockLlmUsageSink();
        var provider = new ScriptedFullLlmProvider();
        provider.Enqueue(new LlmResponse { Content = "Invalid JSON output", PromptTokens = 90, CompletionTokens = 10, TokensUsed = 100 });
        provider.Enqueue(new LlmResponse { Content = "{\"valid\": true}", PromptTokens = 90, CompletionTokens = 10, TokensUsed = 100 });
        var metered = Orkeon.Infrastructure.LLMs.MeteredLlmProvider.Wrap(provider, sink);
        using var chatClient = new Orkeon.Infrastructure.LLMs.Adapters.LlmProviderToChatClientAdapter(metered);
        var validationPipeline = new TestOutputValidationPipeline();
        validationPipeline.EnqueueResult(false, "Invalid JSON format", "Wrap output in JSON braces");
        validationPipeline.EnqueueResult(true);
        var taskWithJson = DomainTask.Create(
            TaskDescription.From("Generate JSON report"),
            ExpectedOutput.From("JSON report"),
            outputOptions: new Domain.Task.TaskOutputOptions
            {
                OutputJson = Domain.Task.JsonSchema.From("{\"type\":\"object\"}")
            });
        var orchestrator = new ExecutionOrchestrator(
            new TestLogger(), new Orkeon.Infrastructure.LLMs.LlmProviderAdapter(metered), chatClient,
            Array.Empty<IBaseTool>(), validationPipeline, new TestOutputParserFactory(),
            fullProvider: null, toolCallingStrategy: null, deliverableResolverFactory: null,
            new FakeFileSystemService());
        var context = CreateTestContext();

        var result = await orchestrator.ExecuteTaskCoreAsync(CreateTestAgent(), taskWithJson, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, provider.ReceivedTurns.Count);
        Assert.Equal(provider.ReceivedTurns.Count, sink.Recorded.Count);
        Assert.All(sink.Recorded, usage =>
        {
            Assert.Equal(context.CrewId.ToString(), usage.CrewId);
            Assert.Equal("scripted-full", usage.Provider);
            Assert.Equal("Test Agent", usage.AgentId);
            Assert.Equal(taskWithJson.Id.ToString(), usage.TaskId);
            Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        });
    }

    #endregion
}
