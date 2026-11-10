using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// A call the host's cost budget refused is no failed call: the task ends
/// <see cref="AgentExitReason.BudgetExhausted"/> with the refusal's message, and nothing asks the provider again.
/// </summary>
public sealed class BudgetRefusalExitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Refusal = "Token budget spent for crew 'c': 120 of 100 tokens";

    /// <summary>A chat client the budget refuses, the way the metered provider under it does.</summary>
    private sealed class RefusedChatClient(bool wrapped) : IChatClient
    {
        public int Calls { get; private set; }

        public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var spent = new BudgetExhaustedException(BudgetDimension.Tokens, Refusal);
            throw wrapped ? new InvalidOperationException("adapter failure", spent) : spent;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static readonly SpyTool Lookup = new("lookup", result: "found");

    private static DomainAgent Agent() =>
        new AgentBuilder().Role("Researcher").Goal("Answer the question").WithTool(Lookup).Build();

    private static DomainTask NewTask() =>
        DomainTask.Create(TaskDescription.From("Answer the question"), ExpectedOutput.From("An answer"));

    private static SimpleExecutionContext Context() =>
        new(CrewId.From(Guid.NewGuid()), [], NullMemoryScope.Instance, []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task ShouldEndBudgetExhausted_WhenTheBudgetRefusesTheCall(bool wrapped)
    {
        using var client = new RefusedChatClient(wrapped);
        var orchestrator = new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance, new ScriptedBasicLlmProvider(), client, [Lookup], new FakeFileSystemService());

        var result = await orchestrator.ExecuteTaskCoreAsync(Agent(), NewTask(), Context(), Ct)
            .WaitAsync(Polling.DefaultTimeout, Ct);

        Assert.False(result.Success);
        Assert.Equal(AgentExitReason.BudgetExhausted, result.ExitReason);
        Assert.Contains(Refusal, result.Error, StringComparison.Ordinal);
        Assert.Equal(1, client.Calls);
    }
}
