using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>FWK-06: the forge's assistant calls its tools as the operator, outside the permission gate.</summary>
public sealed class GateExemptToolInvocationPipelineTests
{
    private sealed class StubToolInvocationPipeline : IToolInvocationPipeline
    {
        public List<ToolInvocationCaller> Callers { get; } = [];

        public System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(
            ToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Callers.Add(invocation.Caller);
            return System.Threading.Tasks.Task.FromResult(new ToolInvocationResult { ConversationText = "ok", RawText = "ok", Success = true });
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldMarkTheCallerExempt_WhenTheForgeCallsATool()
    {
        var inner = new StubToolInvocationPipeline();
        var pipeline = new GateExemptToolInvocationPipeline(inner);
        var caller = new ToolInvocationCaller("forge", "forge") { PermissionMode = "plan" };

        await pipeline.InvokeAsync(new ToolInvocation(null!, new Dictionary<string, object?>(), caller), TestContext.Current.CancellationToken);

        var seen = Assert.Single(inner.Callers);
        Assert.True(seen.GateExempt);
        Assert.Equal("plan", seen.PermissionMode);
    }
}
