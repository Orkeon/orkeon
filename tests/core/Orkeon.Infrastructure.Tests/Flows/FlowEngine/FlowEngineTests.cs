using Orkeon.Application.Interfaces;
using Orkeon.Application.Flow;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Flows.ValueObjects;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Flows;
using Orkeon.Infrastructure.Flows.Steps;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.MCP;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Infrastructure.Tests.Flows;

public class FlowEngineTests
{
    private static readonly string[] s_steps123 = ["step1", "step2", "step3"];
    private static readonly string[] s_steps12 = ["step1", "step2"];
    private static readonly string[] s_steps13 = ["step1", "step3"];

    private readonly MockServiceProvider _serviceProviderMock = new();
    private readonly ILogger<FlowEngine> _logger = NullLogger<FlowEngine>.Instance;

    #region Helper Methods

    private FlowEngine CreateEngine()
    {
        return new FlowEngine(_serviceProviderMock, _logger);
    }

    private static IFlowDefinition BuildSequentialDefinition(string name, params (string stepName, string stepType)[] steps)
    {
        var builder = new FlowDefinitionBuilder()
            .WithName(name)
            .AsSequential();

        foreach (var (stepName, stepType) in steps)
        {
            builder.AddStep(stepName, stepType);
        }

        return builder.Build();
    }

    private class SimpleFlowStep : IFlowStep
    {
        public string Name { get; }
        public string Description => $"Simple step: {Name}";
        private readonly Func<FlowState, CancellationToken, Task<FlowStepResult>> _execute;

        public SimpleFlowStep(string name, Func<FlowState, CancellationToken, Task<FlowStepResult>>? execute = null)
        {
            Name = name;
            _execute = execute ?? ((_, _) => Task.FromResult(
                new FlowStepResult
                {
                    Success = true,
                    Output = $"{name}_output",
                    UpdatedContext = FlowState.Empty
                }));
        }

        public Task<FlowStepResult> ExecuteAsync(FlowState context, CancellationToken cancellationToken = default)
        {
            return _execute(context, cancellationToken);
        }
    }

    private class TrackingFlowStepExecutor : IFlowStepExecutor
    {
        private readonly Dictionary<string, IFlowStep> _steps = [];
        public List<string> ExecutionOrder { get; } = [];

        public void Register(string stepId, IFlowStep step)
        {
            _steps[stepId] = step;
        }

        public IFlowStep ResolveStep(FlowStep stepDefinition)
        {
            ExecutionOrder.Add(stepDefinition.Name);
            if (_steps.TryGetValue(stepDefinition.Id, out var step))
                return step;

            // Return a default simple step
            return new SimpleFlowStep(stepDefinition.Name);
        }
    }

    private class FailingFlowStep : IFlowStep
    {
        public string Name { get; }
        public string Description => $"Failing step: {Name}";
        private int _callCount;
        private readonly int _failUntil;

        public FailingFlowStep(string name, int failUntil = int.MaxValue)
        {
            Name = name;
            _failUntil = failUntil;
        }

        public int CallCount => _callCount;

        public Task<FlowStepResult> ExecuteAsync(FlowState context, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount <= _failUntil)
            {
                return Task.FromResult(FlowStepResult.CreateFailure($"Step '{Name}' failed on attempt {_callCount}"));
            }
            return Task.FromResult(new FlowStepResult { Success = true, Output = "recovered" });
        }
    }

    #endregion

    #region Sequential Execution Tests

    [Fact]
    public async Task ShouldStepsExecuteInOrder_WhenSequential()
    {
        // Arrange
        var id1 = FlowStepId.Create();
        var id2 = FlowStepId.Create();
        var id3 = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(id1, new SimpleFlowStep("step1"));
        tracker.Register(id2, new SimpleFlowStep("step2"));
        tracker.Register(id3, new SimpleFlowStep("step3"));

        var definition = new InMemoryFlowDefinition
        {
            Name = "test_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new() { Id = id1, Name = "step1", Type = "custom" },
                new() { Id = id2, Name = "step2", Type = "custom" },
                new() { Id = id3, Name = "step3", Type = "custom" }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        // Act
        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(s_steps123, tracker.ExecutionOrder);
    }

    [Fact]
    public async Task ShouldContextPassesBetweenSteps_WhenSequential()
    {
        // Arrange
        var producerId = FlowStepId.Create();
        var consumerId = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(producerId, new SimpleFlowStep("producer", (ctx, _) =>
        {
            var updated = FlowState.Empty.Set("data", "hello_world");
            return Task.FromResult(new FlowStepResult
            {
                Success = true,
                Output = "hello_world",
                UpdatedContext = updated
            });
        }));

        string? receivedData = null;
        tracker.Register(consumerId, new SimpleFlowStep("consumer", (ctx, _) =>
        {
            receivedData = ctx.Get<string>("data");
            return Task.FromResult(new FlowStepResult { Success = true, Output = receivedData });
        }));

        var definition = new InMemoryFlowDefinition
        {
            Name = "context_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new() { Id = producerId, Name = "producer", Type = "custom" },
                new() { Id = consumerId, Name = "consumer", Type = "custom" }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        // Act
        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("hello_world", receivedData);
    }

    [Fact]
    public async Task ShouldStopOnFailure_WhenSequential()
    {
        // Arrange
        var id1 = FlowStepId.Create();
        var id2 = FlowStepId.Create();
        var id3 = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(id1, new SimpleFlowStep("step1"));
        tracker.Register(id2, new SimpleFlowStep("step2", (_, _) =>
            Task.FromResult(FlowStepResult.CreateFailure("boom"))));
        tracker.Register(id3, new SimpleFlowStep("step3"));

        var definition = new InMemoryFlowDefinition
        {
            Name = "fail_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new() { Id = id1, Name = "step1", Type = "custom", CanRetry = false },
                new() { Id = id2, Name = "step2", Type = "custom", CanRetry = false },
                new() { Id = id3, Name = "step3", Type = "custom", CanRetry = false }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        // Act
        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("boom", result.Error);
        Assert.Equal(s_steps12, tracker.ExecutionOrder);
    }

    [Fact]
    public async Task ShouldNextStepBranching_WhenSequential()
    {
        // Arrange
        var id1 = FlowStepId.Create();
        var id2 = FlowStepId.Create();
        var id3 = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(id1, new SimpleFlowStep("step1", (_, _) =>
            Task.FromResult(FlowStepResult.CreateSuccess("branching", nextStep: "step3"))));
        tracker.Register(id2, new SimpleFlowStep("step2"));
        tracker.Register(id3, new SimpleFlowStep("step3"));

        var definition = new InMemoryFlowDefinition
        {
            Name = "branch_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new() { Id = id1, Name = "step1", Type = "custom" },
                new() { Id = id2, Name = "step2", Type = "custom" },
                new() { Id = id3, Name = "step3", Type = "custom" }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        // Act
        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(s_steps13, tracker.ExecutionOrder);
    }

    #endregion

    #region Parallel Execution Tests

    [Fact]
    public async Task ShouldIndependentStepsRunConcurrently_WhenParallel()
    {
        // Arrange
        var idA = FlowStepId.Create();
        var idB = FlowStepId.Create();
        var completionOrder = new List<string>();
        var lockObj = new object();

        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(idA, new SimpleFlowStep("a", async (_, ct) =>
        {
            await Task.Delay(50, ct);
            lock (lockObj) { completionOrder.Add("a"); }
            return new FlowStepResult { Success = true, Output = "a_done" };
        }));
        tracker.Register(idB, new SimpleFlowStep("b", async (_, ct) =>
        {
            await Task.Delay(10, ct);
            lock (lockObj) { completionOrder.Add("b"); }
            return new FlowStepResult { Success = true, Output = "b_done" };
        }));

        var definition = new InMemoryFlowDefinition
        {
            Name = "parallel_flow",
            Type = FlowType.Parallel,
            Steps =
            [
                new() { Id = idA, Name = "a", Type = "custom" },
                new() { Id = idB, Name = "b", Type = "custom" }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        // Act
        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, completionOrder.Count);
    }

    [Fact]
    public async Task ShouldDependentStepsRespectOrder_WhenParallel()
    {
        // Arrange
        var idA = FlowStepId.Create();
        var idB = FlowStepId.Create();
        var idC = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        var order = new List<string>();
        var lockObj = new object();

        tracker.Register(idA, new SimpleFlowStep("a", (_, _) =>
        {
            lock (lockObj) { order.Add("a"); }
            return Task.FromResult(new FlowStepResult { Success = true, Output = "a_done" });
        }));
        tracker.Register(idB, new SimpleFlowStep("b", (_, _) =>
        {
            lock (lockObj) { order.Add("b"); }
            return Task.FromResult(new FlowStepResult { Success = true, Output = "b_done" });
        }));
        tracker.Register(idC, new SimpleFlowStep("c", (_, _) =>
        {
            lock (lockObj) { order.Add("c"); }
            return Task.FromResult(new FlowStepResult { Success = true, Output = "c_done" });
        }));

        var definition = new InMemoryFlowDefinition
        {
            Name = "dep_parallel_flow",
            Type = FlowType.Parallel,
            Steps =
            [
                new() { Id = idA, Name = "a", Type = "custom" },
                new() { Id = idB, Name = "b", Type = "custom" },
                new() { Id = idC, Name = "c", Type = "custom", Dependencies = [idA, idB] }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        // Act
        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var cIndex = order.IndexOf("c");
        var aIndex = order.IndexOf("a");
        var bIndex = order.IndexOf("b");
        Assert.True(cIndex > aIndex);
        Assert.True(cIndex > bIndex);
    }

    #endregion

    #region Conditional Execution Tests

    [Fact]
    public async Task ShouldCorrectBranchingOnTrueCondition_WhenConditional()
    {
        var checkId = FlowStepId.Create();
        var guardedId = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(checkId, new SimpleFlowStep("check", (_, _) =>
        {
            return Task.FromResult(new FlowStepResult
            {
                Success = true,
                Output = "checked",
                UpdatedContext = FlowState.Empty
            });
        }));
        tracker.Register(guardedId, new SimpleFlowStep("guarded"));

        var definition = new InMemoryFlowDefinition
        {
            Name = "conditional_flow",
            Type = FlowType.Conditional,
            Steps =
            [
                new() { Id = checkId, Name = "check", Type = "custom" },
                new()
                {
                    Id = guardedId, Name = "guarded", Type = "custom",
                    Parameters = FlowStepParameters.Empty.Set("condition", "should_run")
                }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);
        flow.State = FlowState.Empty.Set("should_run", true);

        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("guarded", tracker.ExecutionOrder);
    }

    [Fact]
    public async Task ShouldSkipStepOnFalseCondition_WhenConditional()
    {
        var checkId = FlowStepId.Create();
        var guardedId = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(checkId, new SimpleFlowStep("check"));
        tracker.Register(guardedId, new SimpleFlowStep("guarded"));

        var definition = new InMemoryFlowDefinition
        {
            Name = "conditional_flow",
            Type = FlowType.Conditional,
            Steps =
            [
                new() { Id = checkId, Name = "check", Type = "custom" },
                new()
                {
                    Id = guardedId, Name = "guarded", Type = "custom",
                    Parameters = FlowStepParameters.Empty.Set("condition", "should_run")
                }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);
        flow.State = FlowState.Empty.Set("should_run", false);

        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("check", tracker.ExecutionOrder);
        Assert.DoesNotContain("guarded", tracker.ExecutionOrder);
    }

    #endregion

    #region Loop Execution Tests

    [Fact]
    public async Task ShouldIterateUntilExitCondition_WhenLoop()
    {
        var iterations = 0;
        var iterateId = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(iterateId, new SimpleFlowStep("iterate", (ctx, _) =>
        {
            iterations++;
            var updated = FlowState.Empty;
            if (iterations >= 3)
            {
                updated = updated.Set("_exit_loop", true);
            }
            return Task.FromResult(new FlowStepResult
            {
                Success = true,
                Output = $"iteration_{iterations}",
                UpdatedContext = updated
            });
        }));

        var definition = new InMemoryFlowDefinition
        {
            Name = "loop_flow",
            Type = FlowType.Loop,
            Steps =
            [
                new() { Id = iterateId, Name = "iterate", Type = "custom" }
            ],
            Configuration = new FlowConfiguration { MaxRetries = 10 }
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, iterations);
    }

    [Fact]
    public async Task ShouldStopAtMaxIterations_WhenLoop()
    {
        var iterations = 0;
        var iterateId = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(iterateId, new SimpleFlowStep("iterate", (_, _) =>
        {
            iterations++;
            return Task.FromResult(new FlowStepResult { Success = true, Output = $"iter_{iterations}" });
        }));

        var definition = new InMemoryFlowDefinition
        {
            Name = "bounded_loop",
            Type = FlowType.Loop,
            Steps =
            [
                new() { Id = iterateId, Name = "iterate", Type = "custom" }
            ],
            Configuration = new FlowConfiguration { MaxRetries = 5 }
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(5, iterations);
    }

    #endregion

    #region Retry Tests

    [Fact]
    public async Task ShouldExponentialBackoffOnFailure_WhenRetry()
    {
        var retryStepId = FlowStepId.Create();
        var failingStep = new FailingFlowStep("retry_step", failUntil: 2);
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(retryStepId, failingStep);

        var definition = new InMemoryFlowDefinition
        {
            Name = "retry_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new()
                {
                    Id = retryStepId, Name = "retry_step", Type = "custom",
                    CanRetry = true, MaxRetries = 3
                }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, failingStep.CallCount);
    }

    [Fact]
    public async Task ShouldFailAfterMaxRetries_WhenRetry()
    {
        var alwaysFailId = FlowStepId.Create();
        var failingStep = new FailingFlowStep("always_fail", failUntil: 100);
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(alwaysFailId, failingStep);

        var definition = new InMemoryFlowDefinition
        {
            Name = "fail_retry_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new()
                {
                    Id = alwaysFailId, Name = "always_fail", Type = "custom",
                    CanRetry = true, MaxRetries = 2
                }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(3, failingStep.CallCount);
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public async Task ShouldStepTimeoutEnforced_WhenTimeout()
    {
        var slowStepId = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(slowStepId, new SimpleFlowStep("slow_step", async (_, ct) =>
        {
            await Task.Delay(5000, ct);
            return new FlowStepResult { Success = true, Output = "done" };
        }));

        var definition = new InMemoryFlowDefinition
        {
            Name = "timeout_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new()
                {
                    Id = slowStepId, Name = "slow_step", Type = "custom",
                    Timeout = TimeSpan.FromMilliseconds(100),
                    CanRetry = false
                }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);

        var result = await flow.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Error);
    }

    #endregion

    #region TopologicalSort Tests

    [Fact]
    public void ShouldCorrectDependencyGrouping_WhenTopologicalSort()
    {
        var idA = FlowStepId.Create();
        var idB = FlowStepId.Create();
        var idC = FlowStepId.Create();
        var idD = FlowStepId.Create();
        var steps = new List<FlowStep>
        {
            new() { Id = idA, Name = "a", Type = "custom" },
            new() { Id = idB, Name = "b", Type = "custom" },
            new() { Id = idC, Name = "c", Type = "custom", Dependencies = [idA, idB] },
            new() { Id = idD, Name = "d", Type = "custom", Dependencies = [idC] }
        };

        var groups = DefinitionBasedFlow.TopologicalSort(steps);

        Assert.Equal(3, groups.Count);
        Assert.Equal(2, groups[0].Count);
        Assert.Contains(groups[0], s => s.Id == idA);
        Assert.Contains(groups[0], s => s.Id == idB);
        Assert.Single(groups[1]);
        Assert.Equal(idC, groups[1][0].Id);
        Assert.Single(groups[2]);
        Assert.Equal(idD, groups[2][0].Id);
    }

    [Fact]
    public void ShouldCircularDependencyThrowsException_WhenTopologicalSort()
    {
        var idA = FlowStepId.Create();
        var idB = FlowStepId.Create();
        var idC = FlowStepId.Create();
        var steps = new List<FlowStep>
        {
            new() { Id = idA, Name = "a", Type = "custom", Dependencies = [idC] },
            new() { Id = idB, Name = "b", Type = "custom", Dependencies = [idA] },
            new() { Id = idC, Name = "c", Type = "custom", Dependencies = [idB] }
        };

        Assert.Throws<InvalidOperationException>(() => DefinitionBasedFlow.TopologicalSort(steps));
    }

    [Fact]
    public void ShouldAllIndependentStepsInOneGroup_WhenTopologicalSort()
    {
        var steps = new List<FlowStep>
        {
            new() { Id = FlowStepId.Create(), Name = "a", Type = "custom" },
            new() { Id = FlowStepId.Create(), Name = "b", Type = "custom" },
            new() { Id = FlowStepId.Create(), Name = "c", Type = "custom" }
        };

        var groups = DefinitionBasedFlow.TopologicalSort(steps);

        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
    }

    #endregion

    #region FlowDefinitionBuilder Tests

    [Fact]
    public void ShouldFluentConstruction_WhenFlowDefinitionBuilder()
    {
        var definition = new FlowDefinitionBuilder()
            .WithName("Test Flow")
            .WithDescription("A test flow")
            .AsSequential()
            .WithTimeout(TimeoutStandard)
            .WithMaxRetries(2)
            .AddStep("step1", "llm")
            .AddCrewStep("research", "research_crew")
            .AddLlmStep("summarize", "Summarize: {research.output}")
            .AddToolStep("save", "file_write")
            .Build();

        Assert.NotNull(definition.Id);
        Assert.Equal("Test Flow", definition.Name);
        Assert.Equal("A test flow", definition.Description);
        Assert.Equal(FlowType.Sequential, definition.Type);
        Assert.Equal(4, definition.Steps.Count);
        Assert.Equal("step1", definition.Steps[0].Name);
        Assert.Equal("research", definition.Steps[1].Name);
        Assert.Equal("crew", definition.Steps[1].Type);
        Assert.Equal("summarize", definition.Steps[2].Name);
        Assert.Equal("llm", definition.Steps[2].Type);
        Assert.Equal("save", definition.Steps[3].Name);
        Assert.Equal("tool", definition.Steps[3].Type);
    }

    [Fact]
    public void ShouldValidationNameRequired_WhenFlowDefinitionBuilder()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new FlowDefinitionBuilder()
                .AddStep("step1", "llm")
                .Build());
    }

    [Fact]
    public void ShouldValidationStepsRequired_WhenFlowDefinitionBuilder()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new FlowDefinitionBuilder()
                .WithName("Empty Flow")
                .Build());
    }

    [Fact]
    public void ShouldWithDependencies_WhenFlowDefinitionBuilder()
    {
        var definition = new FlowDefinitionBuilder()
            .WithName("Dependency Flow")
            .AsParallel()
            .AddStep("step1", "llm")
            .AddStep("step2", "llm", b => b.DependsOn("step1"))
            .Build();

        Assert.Equal(2, definition.Steps.Count);
        Assert.Empty(definition.Steps[0].Dependencies);
        Assert.Single(definition.Steps[1].Dependencies);
        // The dependency should reference step1's Id
        Assert.Equal(definition.Steps[0].Id, definition.Steps[1].Dependencies[0]);
    }

    [Fact]
    public void ShouldWithStepTimeout_WhenFlowDefinitionBuilder()
    {
        var definition = new FlowDefinitionBuilder()
            .WithName("Timeout Flow")
            .AddStep("step1", "llm", b => b.WithTimeout(TimeoutQuick))
            .Build();

        Assert.Equal(TimeoutQuick, definition.Steps[0].Timeout);
    }

    [Fact]
    public void ShouldWithStepRetries_WhenFlowDefinitionBuilder()
    {
        var definition = new FlowDefinitionBuilder()
            .WithName("Retry Flow")
            .AddStep("step1", "llm", b => b.WithMaxRetries(5))
            .Build();

        Assert.Equal(5, definition.Steps[0].MaxRetries);
    }

    #endregion

    #region Flow Step Tests

    [Fact]
    public async Task ShouldBranchesToTrueStep_WhenConditionalFlowStep()
    {
        var parameters = FlowStepParameters.Empty
            .Set("condition_key", "is_valid")
            .Set("true_step", "process")
            .Set("false_step", "error_handler");

        var step = new ConditionalFlowStep("check", parameters);
        var context = FlowState.Empty.Set("is_valid", true);

        var result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("process", result.NextStep);
    }

    [Fact]
    public async Task ShouldBranchesToFalseStep_WhenConditionalFlowStep()
    {
        var parameters = FlowStepParameters.Empty
            .Set("condition_key", "is_valid")
            .Set("true_step", "process")
            .Set("false_step", "error_handler");

        var step = new ConditionalFlowStep("check", parameters);
        var context = FlowState.Empty.Set("is_valid", false);

        var result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("error_handler", result.NextStep);
    }

    [Fact]
    public async Task ShouldMissingConditionKeyFails_WhenConditionalFlowStep()
    {
        var parameters = FlowStepParameters.Empty;
        var step = new ConditionalFlowStep("check", parameters);

        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("condition_key", result.Error);
    }

    [Fact]
    public async Task ShouldWaitForSpecifiedDuration_WhenDelayFlowStep()
    {
        var parameters = FlowStepParameters.Empty.Set("delay_ms", 50);
        var step = new DelayFlowStep("wait", parameters);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.True(result.Success);
        Assert.True(sw.ElapsedMilliseconds >= 40);
    }

    [Fact]
    public async Task ShouldSupportDelaySeconds_WhenDelayFlowStep()
    {
        var parameters = FlowStepParameters.Empty.Set("delay_seconds", 1);
        var step = new DelayFlowStep("wait", parameters);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await step.ExecuteAsync(FlowState.Empty, cts.Token);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ShouldCallChatClient_WhenLlmFlowStep()
    {
        // Arrange
        using var chatClient = new MockChatClient();
        chatClient.SetGetResponseResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The answer is 42")));

        var parameters = FlowStepParameters.Empty
            .Set("prompt_template", "What is {question}?");

        var step = new LlmFlowStep("ask", parameters, chatClient);
        var context = FlowState.Empty.Set("question", "the meaning of life");

        // Act
        var result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var llmOutput = Assert.IsType<LlmFlowStepOutput>(result.Output);
        Assert.Equal("The answer is 42", llmOutput.LlmResponse);
        Assert.Equal(1, chatClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ShouldMissingPromptTemplateFails_WhenLlmFlowStep()
    {
        // Arrange
        using var chatClient = new MockChatClient();
        var parameters = FlowStepParameters.Empty;
        var step = new LlmFlowStep("ask", parameters, chatClient);

        // Act
        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("prompt_template", result.Error);
    }

    [Fact]
    public async Task ShouldExecuteTool_WhenToolFlowStep()
    {
        // Arrange
        var mockTool = new InlineMockBaseTool("test_tool", "A test tool");
        var mockRegistry = new MockToolRegistry();
        mockRegistry.AddTool("test_tool", mockTool);

        var parameters = FlowStepParameters.Empty
            .Set("tool_name", "test_tool");

        var step = new ToolFlowStep("run_tool", parameters, mockRegistry);

        // Act
        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var toolOutput = Assert.IsType<ToolFlowStepOutput>(result.Output);
        Assert.Equal("mock output", toolOutput.ToolOutput);
    }

    [Fact]
    public async Task ShouldMissingToolFails_WhenToolFlowStep()
    {
        // Arrange
        var mockRegistry = new MockToolRegistry();

        var parameters = FlowStepParameters.Empty
            .Set("tool_name", "nonexistent");

        var step = new ToolFlowStep("run_tool", parameters, mockRegistry);

        // Act
        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ShouldGetInput_WhenHumanInputFlowStep()
    {
        // Arrange
        var humanInput = new MockHumanInputProvider();
        humanInput.SetInputResult("user_response");

        var parameters = FlowStepParameters.Empty
            .Set("prompt", "What should we do?");

        var step = new HumanInputFlowStep("ask_user", parameters, humanInput);

        // Act
        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var humanOutput = Assert.IsType<HumanInputFlowStepOutput>(result.Output);
        Assert.Equal("user_response", humanOutput.HumanInput);
    }

    #endregion

    #region FlowEngine Core Tests

    [Fact]
    public async Task ShouldTrackExecutionHistory_WhenFlowEngineExecuteFlowAsync()
    {
        // Arrange
        var engine = CreateEngine();
        var mockFlow = new MockFlow();
        mockFlow.Id = FlowId.Create();
        mockFlow.Name = "Test Flow";
        mockFlow.SetExecuteSuccess("done");
        var expectedFlowId = (string)mockFlow.Id;

        // Act
        await engine.ExecuteFlowAsync(mockFlow, TestContext.Current.CancellationToken);

        // Assert
        var history = engine.GetExecutionHistory();
        Assert.Single(history);
        Assert.True(history[0].Success);
        Assert.Equal(expectedFlowId, history[0].FlowId);
    }

    [Fact]
    public async Task ShouldReturnResult_WhenFlowEngineExecuteStepAsync()
    {
        // Arrange
        var engine = CreateEngine();
        var step = new SimpleFlowStep("test_step");
        var context = new Dictionary<string, object> { { "key", "value" } };

        // Act
        var result = await engine.ExecuteStepAsync(step, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("test_step_output", result.Output);
    }

    [Fact]
    public void ShouldRegisterAndGetFlows_WhenFlowEngine()
    {
        // Arrange
        var engine = CreateEngine();
        var definition = new InMemoryFlowDefinition { Name = "test_flow" };

        // Act
        engine.RegisterFlow(definition);
        var flows = engine.GetRegisteredFlows();

        // Assert
        Assert.Single(flows);
        Assert.True(flows.ContainsKey("test_flow"));
    }

    [Fact]
    public void ShouldValidateFlowValid_WhenFlowEngine()
    {
        var engine = CreateEngine();
        var definition = new InMemoryFlowDefinition
        {
            Name = "valid_flow",
            Steps =
            [
                new() { Id = FlowStepId.Create(), Name = "step1", Type = "llm" }
            ]
        };

        var result = engine.ValidateFlow(definition);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldValidateFlowMissingName_WhenFlowEngine()
    {
        var engine = CreateEngine();
        var definition = new InMemoryFlowDefinition
        {
            Name = "",
            Steps =
            [
                new() { Id = FlowStepId.Create(), Name = "step1", Type = "llm" }
            ]
        };

        var result = engine.ValidateFlow(definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldValidateFlowNoSteps_WhenFlowEngine()
    {
        var engine = CreateEngine();
        var definition = new InMemoryFlowDefinition
        {
            Name = "empty_flow",
            Steps = []
        };

        var result = engine.ValidateFlow(definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("step", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldValidateFlowUnknownStepTypeWarning_WhenFlowEngine()
    {
        var engine = CreateEngine();
        var definition = new InMemoryFlowDefinition
        {
            Name = "unknown_type_flow",
            Steps =
            [
                new() { Id = FlowStepId.Create(), Name = "step1", Type = "magic_step" }
            ]
        };

        var result = engine.ValidateFlow(definition);

        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("unknown type", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region FlowMetrics Tests

    [Fact]
    public async Task ShouldCalculateCorrectly_WhenFlowEngineGetFlowMetrics()
    {
        // Arrange
        var engine = CreateEngine();

        var successFlow = new MockFlow();
        successFlow.Id = FlowId.Create();
        successFlow.Name = "Test";
        successFlow.SetExecuteSuccess();

        var failFlow = new MockFlow();
        failFlow.Id = FlowId.Create();
        failFlow.Name = "Test";
        failFlow.SetExecuteFailure("error");

        // Act
        await engine.ExecuteFlowAsync(successFlow, TestContext.Current.CancellationToken);
        await engine.ExecuteFlowAsync(successFlow, TestContext.Current.CancellationToken);
        await engine.ExecuteFlowAsync(failFlow, TestContext.Current.CancellationToken);

        var metrics = engine.GetFlowMetrics();

        // Assert
        Assert.Equal(3, metrics.TotalExecutions);
        Assert.Equal(2, metrics.SuccessfulExecutions);
        Assert.Equal(1, metrics.FailedExecutions);
        Assert.True(metrics.LastExecution.HasValue);
    }

    [Fact]
    public async Task ShouldExecutionHistoryOrderedByCompletedAt_WhenFlowEngine()
    {
        // Arrange
        var engine = CreateEngine();

        for (int i = 0; i < 5; i++)
        {
            var flow = new MockFlow();
            flow.Id = FlowId.Create();
            flow.Name = $"Flow {i}";
            flow.SetExecuteSuccess();
            await engine.ExecuteFlowAsync(flow, TestContext.Current.CancellationToken);
        }

        // Act
        var history = engine.GetExecutionHistory(3);

        // Assert
        Assert.Equal(3, history.Count);
        for (int i = 0; i < history.Count - 1; i++)
        {
            Assert.True(history[i].CompletedAt >= history[i + 1].CompletedAt);
        }
    }

    #endregion

    #region YAML Loading Tests

    [Fact]
    public void ShouldParseCorrectly_WhenYamlLoaderLoadFromString()
    {
        // Arrange
        var yamlMock = new MockYamlSerializer();
        yamlMock.SetDeserializeResult(new YamlFlowDefinitionLoader.YamlFlowModel
        {
            Name = "research_pipeline",
            Description = "Research and summarize",
            Type = "sequential",
            Settings = new Dictionary<string, object?>
            {
                { "timeout_seconds", 300 },
                { "max_retries", 2 }
            },
            Steps =
            [
                new()
                {
                    Name = "research",
                    Type = "crew",
                    Parameters = new Dictionary<string, object>
                    {
                        { "crew_config", "research_crew" }
                    }
                },
                new()
                {
                    Name = "validate",
                    Type = "llm",
                    Parameters = new Dictionary<string, object>
                    {
                        { "prompt_template", "Validate: {research.output}" }
                    }
                }
            ]
        });

        var loader = new YamlFlowDefinitionLoader(yamlMock, new FakeFileSystemService());

        // Act
        var definition = loader.LoadFromString("yaml_content");

        // Assert
        Assert.Equal("research_pipeline", definition.Name);
        Assert.Equal("Research and summarize", definition.Description);
        Assert.Equal(FlowType.Sequential, definition.Type);
        Assert.Equal(2, definition.Steps.Count);
        Assert.Equal("research", definition.Steps[0].Name);
        Assert.Equal("crew", definition.Steps[0].Type);
        Assert.Equal("validate", definition.Steps[1].Name);
        Assert.Equal("llm", definition.Steps[1].Type);
    }

    [Fact]
    public void ShouldLoadFromStringNullThrows_WhenYamlLoader()
    {
        // Arrange
        var yamlMock = new MockYamlSerializer();
        var loader = new YamlFlowDefinitionLoader(yamlMock, new FakeFileSystemService());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => loader.LoadFromString(null!));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLoadFromFileFileNotFoundThrows_WhenYamlLoader()
    {
        // Arrange
        var yamlMock = new MockYamlSerializer();
        var loader = new YamlFlowDefinitionLoader(yamlMock, new FakeFileSystemService());

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => loader.LoadFromFileAsync("/nonexistent/path.yaml", TestContext.Current.CancellationToken));
    }

    #endregion

    #region FlowStepExecutor Tests

    [Fact]
    public void ShouldResolveConditionalStep_WhenFlowStepExecutor()
    {
        var executor = new FlowStepExecutor(_serviceProviderMock);
        var stepDef = new FlowStep
        {
            Id = FlowStepId.Create(),
            Name = "check",
            Type = "conditional",
            Parameters = FlowStepParameters.Empty.Set("condition_key", "flag")
        };

        var step = executor.ResolveStep(stepDef);

        Assert.IsType<ConditionalFlowStep>(step);
        Assert.Equal("check", step.Name);
    }

    [Fact]
    public void ShouldResolveDelayStep_WhenFlowStepExecutor()
    {
        var executor = new FlowStepExecutor(_serviceProviderMock);
        var stepDef = new FlowStep
        {
            Id = FlowStepId.Create(),
            Name = "wait",
            Type = "delay",
            Parameters = FlowStepParameters.Empty.Set("delay_ms", 100)
        };

        var step = executor.ResolveStep(stepDef);

        Assert.IsType<DelayFlowStep>(step);
    }

    [Fact]
    public void ShouldUnknownTypeThrows_WhenFlowStepExecutor()
    {
        var executor = new FlowStepExecutor(_serviceProviderMock);
        var stepDef = new FlowStep
        {
            Id = FlowStepId.Create(),
            Name = "unknown",
            Type = "nonexistent_type"
        };

        Assert.Throws<InvalidOperationException>(() => executor.ResolveStep(stepDef));
    }

    [Fact]
    public void ShouldResolveLlmStep_WhenFlowStepExecutor()
    {
        // Arrange
        var chatClient = new MockChatClient();
        var sp = new MockServiceProvider();
        sp.Register(typeof(IChatClient), chatClient);

        var executor = new FlowStepExecutor(sp);
        var stepDef = new FlowStep
        {
            Id = FlowStepId.Create(),
            Name = "ask",
            Type = "llm",
            Parameters = FlowStepParameters.Empty.Set("prompt_template", "Hello")
        };

        var step = executor.ResolveStep(stepDef);

        Assert.IsType<LlmFlowStep>(step);
    }

    [Fact]
    public void ShouldResolveToolStep_WhenFlowStepExecutor()
    {
        // Arrange
        var registryMock = new MockToolRegistry();
        var sp = new MockServiceProvider();
        sp.Register(typeof(IToolRegistry), registryMock);

        var executor = new FlowStepExecutor(sp);
        var stepDef = new FlowStep
        {
            Id = FlowStepId.Create(),
            Name = "run",
            Type = "tool",
            Parameters = FlowStepParameters.Empty.Set("tool_name", "test_tool")
        };

        var step = executor.ResolveStep(stepDef);

        Assert.IsType<ToolFlowStep>(step);
    }

    #endregion

    #region InMemoryFlowDefinition Validation Tests

    [Fact]
    public void ShouldValidateSuccessfully_WhenInMemoryFlowDefinition()
    {
        var definition = new InMemoryFlowDefinition
        {
            Name = "valid_flow",
            Steps =
            [
                new() { Id = FlowStepId.Create(), Name = "step1", Type = "llm" }
            ]
        };

        var isValid = definition.Validate(out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    [Fact]
    public void ShouldDetectCircularDependency_WhenInMemoryFlowDefinition()
    {
        var idA = FlowStepId.Create();
        var idB = FlowStepId.Create();
        var definition = new InMemoryFlowDefinition
        {
            Name = "circular_flow",
            Steps =
            [
                new() { Id = idA, Name = "a", Type = "llm", Dependencies = [idB] },
                new() { Id = idB, Name = "b", Type = "llm", Dependencies = [idA] }
            ]
        };

        var isValid = definition.Validate(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("Circular", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldDetectDuplicateStepIds_WhenInMemoryFlowDefinition()
    {
        var sharedId = FlowStepId.Create();
        var definition = new InMemoryFlowDefinition
        {
            Name = "dup_flow",
            Steps =
            [
                new() { Id = sharedId, Name = "step1", Type = "llm" },
                new() { Id = sharedId, Name = "step1_dup", Type = "llm" }
            ]
        };

        var isValid = definition.Validate(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldDetectUnknownDependency_WhenInMemoryFlowDefinition()
    {
        var definition = new InMemoryFlowDefinition
        {
            Name = "missing_dep_flow",
            Steps =
            [
                new()
                {
                    Id = FlowStepId.Create(), Name = "step1", Type = "llm",
                    Dependencies = [FlowStepId.Create()]
                }
            ]
        };

        var isValid = definition.Validate(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("unknown step", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region LlmFlowStep Template Resolution Tests

    [Fact]
    public void ShouldSubstituteSimpleKeys_WhenLlmFlowStepResolveTemplate()
    {
        var context = FlowState.Empty
            .Set("name", "World")
            .Set("topic", "AI");

        var result = LlmFlowStep.ResolveTemplate("Hello {name}, let's talk about {topic}", context);

        Assert.Equal("Hello World, let's talk about AI", result);
    }

    [Fact]
    public void ShouldSubstituteDottedKeys_WhenLlmFlowStepResolveTemplate()
    {
        var context = FlowState.Empty
            .Set("research.output", "findings");

        var result = LlmFlowStep.ResolveTemplate("Summarize: {research.output}", context);

        Assert.Equal("Summarize: findings", result);
    }

    [Fact]
    public void ShouldResolveTemplateKeepsUnresolvedPlaceholders_WhenLlmFlowStep()
    {
        var result = LlmFlowStep.ResolveTemplate("Hello {unknown}", FlowState.Empty);

        Assert.Equal("Hello {unknown}", result);
    }

    #endregion

    #region DI Registration Tests

    [Fact]
    public void ShouldRegisterServices_WhenDIAddOrkeonFlows()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IServiceProvider>(sp => sp);

        services.AddOrkeonFlows();
        var serviceProvider = services.BuildServiceProvider();

        var flowStepExecutor = serviceProvider.GetService<IFlowStepExecutor>();
        Assert.NotNull(flowStepExecutor);
        Assert.IsType<FlowStepExecutor>(flowStepExecutor);

        var flowEngine = serviceProvider.GetService<IFlowEngine>();
        Assert.NotNull(flowEngine);
        Assert.IsType<FlowEngine>(flowEngine);

        var descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(YamlFlowDefinitionLoader));
        Assert.NotNull(descriptor);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ShouldBeAbleTocellationStopsExecution_WhenFlowEngine()
    {
        var slowId = FlowStepId.Create();
        var tracker = new TrackingFlowStepExecutor();
        tracker.Register(slowId, new SimpleFlowStep("slow", async (_, ct) =>
        {
            await Task.Delay(5000, ct);
            return new FlowStepResult { Success = true };
        }));

        var definition = new InMemoryFlowDefinition
        {
            Name = "cancel_flow",
            Type = FlowType.Sequential,
            Steps =
            [
                new() { Id = slowId, Name = "slow", Type = "custom", CanRetry = false }
            ]
        };

        var flow = new DefinitionBasedFlow(definition, tracker);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await flow.ExecuteAsync(cts.Token);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
