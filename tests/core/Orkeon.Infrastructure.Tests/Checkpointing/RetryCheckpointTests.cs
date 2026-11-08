using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

/// <summary>
/// FWK-15: the retry layer sits inside the checkpoint decorator, so only the attempt that succeeded is
/// checkpointed, and its <c>RetryCount</c> says how many attempts came before it.
/// </summary>
public sealed class RetryCheckpointTests : IDisposable
{
    private readonly InMemoryStateStore _store = new();
    private readonly AsyncLocalRunScope _scope = new();
    private readonly DomainAgent _agent = new AgentBuilder().Role("Worker").Goal("Work").Build();
    private readonly MockMemoryScope _memory = new();

    public void Dispose() => _memory.Dispose();

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldCheckpointTheSuccessfulAttemptWithItsRetryCount()
    {
        var manager = new CheckpointManager(_store, NullLogger<CheckpointManager>.Instance);
        var inner = new FakeScriptedAgentExecutionService(
            FakeScriptedAgentExecutionService.Fail(), FakeScriptedAgentExecutionService.Fail(), FakeScriptedAgentExecutionService.Ok());
        var retrying = new RetryingAgentExecutionService(inner, NullLogger<RetryingAgentExecutionService>.Instance, new FakeImmediateTimeProvider());
        var checkpointing = new CheckpointingAgentExecutionService(
            retrying, _scope, manager, NullLogger<CheckpointingAgentExecutionService>.Instance);
        var id = ExecutionId.New();
        await manager.StartSessionAsync(id, "crew@/crew/crew.yaml", TestContext.Current.CancellationToken);
        var task = new CrewTaskBuilder().WithKey("t").Description("do t").ExpectedOutput("done")
            .Retry(new TaskRetryPolicy(3)).Build();
        var context = new SimpleExecutionContext(CrewId.Create(), new Dictionary<string, string>(), _memory, [], CancellationToken.None);

        using (_scope.Enter(new RunScope(id, "crew@/crew/crew.yaml", null)))
        {
            var result = await checkpointing.ExecuteTaskAsync(_agent, task, context, TestContext.Current.CancellationToken);
            Assert.True(result.Success);
        }

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        var checkpoint = Assert.Single(state!.TaskCheckpoints).Value;
        Assert.Equal(CheckpointStatus.Completed, checkpoint.Status);
        Assert.Equal(2, checkpoint.RetryCount);
        Assert.Equal(3, inner.Calls);
    }
}
