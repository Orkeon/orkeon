using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Infrastructure.Orchestration;

namespace Orkeon.Application.Tests.Services.Security;

/// <summary>
/// FWK-23: a tool swallows what it throws into a failed answer, so a tool call that left its run waiting for a human
/// is told from the run itself: the pipeline stops the agent's loop with the suspension instead of handing the model an error.
/// </summary>
[Collection("OrkeonMeter")]
public sealed class ToolInvocationPipelineSuspensionTests
{
    private static readonly ToolInvocationCaller Writer = new("agent-1", "writer", "task-1", "crew-1");

    private static readonly PendingInputRequest Question =
        new() { RequestId = "t#1.1", Kind = PendingInputKind.Text, Prompt = "Which one?", TaskKey = "t#1" };

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldThrowTheSuspension_WhenTheToolLeftTheRunWaitingForInput()
    {
        var scope = new AsyncLocalRunScope();
        var run = new RunScope(ExecutionId.New(), "crew");
        var tool = new SpyTool("human_input", result: "Tool execution failed: waiting", succeed: false) { OnCall = () => run.Suspend(Question) };
        var pipeline = new ToolInvocationPipeline(options: new ToolInvocationPipelineOptions { RunScope = scope });

        using (scope.Enter(run))
        {
            var thrown = await Assert.ThrowsAsync<RunSuspendedException>(() => pipeline.InvokeAsync(
                new ToolInvocation(tool, new Dictionary<string, object?> { ["input"] = "x" }, Writer), TestContext.Current.CancellationToken));
            Assert.Equal("t#1.1", thrown.RequestId);
        }
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAnswerNormally_WhenTheRunIsNotSuspended()
    {
        var scope = new AsyncLocalRunScope();
        var tool = new SpyTool("echo", result: "fine");
        var pipeline = new ToolInvocationPipeline(options: new ToolInvocationPipelineOptions { RunScope = scope });

        using (scope.Enter(new RunScope(ExecutionId.New(), "crew")))
        {
            var result = await pipeline.InvokeAsync(
                new ToolInvocation(tool, new Dictionary<string, object?> { ["input"] = "x" }, Writer), TestContext.Current.CancellationToken);
            Assert.True(result.Success);
        }
    }
}
