using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// GAP-34 — an agent carrying its own provider (<c>Agent.Llm</c>) runs on a client the host's profile
/// registry builds over that provider, metered there. An orchestrator built by hand has no registry: it
/// cannot meter that provider, so it fails the task naming the agent — the rule of a profile it does not
/// know — instead of answering on its own default, in silence, as it used to.
/// </summary>
public sealed class AgentOwnProviderRunTests
{
    [Fact]
    public async System.Threading.Tasks.Task An_orchestrator_without_the_hosts_profiles_fails_the_task_naming_the_agent()
    {
        var own = new ScriptedFullLlmProvider();
        var @default = new ScriptedFullLlmProvider();
        using var defaultClient = new LlmProviderToChatClientAdapter(@default);
        var orchestrator = new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance, new LlmProviderAdapter(@default),
            defaultClient, [], new FakeFileSystemService());
        var agent = new AgentBuilder().Role("Reviewer").Goal("Review the change").WithLlm(own).Build();
        var task = DomainTask.Create(TaskDescription.From("Review the change"), ExpectedOutput.From("A review"));

        var result = await orchestrator.ExecuteTaskCoreAsync(
            agent, task, new SimpleExecutionContext(CrewId.Create(), [], NullMemoryScope.Instance, []),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Agent 'Reviewer'", result.Error, StringComparison.Ordinal);
        Assert.Contains("scripted-full", result.Error, StringComparison.Ordinal);
        Assert.Empty(own.ReceivedTurns);
        Assert.Empty(@default.ReceivedTurns);
    }
}
