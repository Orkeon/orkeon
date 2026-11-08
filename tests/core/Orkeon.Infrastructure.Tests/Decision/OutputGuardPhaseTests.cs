using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.Decision;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Decision;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Decision;

/// <summary>
/// FWK-19 through the real pipeline: a refusal of the output phase never makes a profile fail over
/// (FWK-05), and a judge that cannot answer, fail-closed, gives a task a readable error and one
/// <c>guardian.judge.unavailable</c> event.
/// </summary>
public class OutputGuardPhaseTests
{
    private const string Secret = "The secret plan is X.";

    private sealed class StubGuardian(GuardResult verdict) : IGuardian
    {
        public System.Threading.Tasks.Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(verdict);
    }

    private static DomainAgent BuildAgent() => new AgentBuilder().Role("Researcher").Goal("Research").MaxIterations(5).Build();

    private static DomainTask BuildTask() =>
        DomainTask.Create(TaskDescription.From("Summarise the page"), ExpectedOutput.From("A summary"));

    private static SimpleExecutionContext BuildContext() =>
        new(CrewId.From(Guid.NewGuid()), [], NullMemoryScope.Instance, []);

    private static GuardianPipeline Pipeline(IGuardian guard, MockAuditLogger audit)
    {
        var pipeline = new GuardianPipeline(
            new GuardianPolicyEngine(new GuardianPolicy()), NullLogger<GuardianPipeline>.Instance, audit);
        pipeline.AddGuard(GuardPhase.Output, guard);
        return pipeline;
    }

    private static ExecutionOrchestrator Orchestrator(ILlmProvider provider, LlmProviderToChatClientAdapter client, IGuardianPipeline guardian) =>
        new(NullLogger<ExecutionOrchestrator>.Instance, new LlmProviderAdapter(provider), client, [], new FakeFileSystemService())
        {
            Guardian = guardian,
        };

    [Fact]
    public async Task ARefusedOutput_NeverMakesAProfileFailOver()
    {
        var primary = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = Secret } };
        var secondary = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from-b" } };
        var observer = new FakeLlmFallbackObserver();
        var chain = new FallbackLlmProvider(
            "main", [new FallbackLink("a", primary), new FallbackLink("b", secondary)], observer, NullLogger.Instance, null, TimeProvider.System);
        var audit = new MockAuditLogger();
        var refusal = GuardResult.Block("policy", [new GuardViolation("Stub", GuardPhase.Output, "policy", GuardThreatSeverity.High, DateTime.UtcNow)]);
        using var client = new LlmProviderToChatClientAdapter(chain);
        var orchestrator = Orchestrator(chain, client, Pipeline(new StubGuardian(refusal), audit));

        var result = await orchestrator.ExecuteTaskCoreAsync(BuildAgent(), BuildTask(), BuildContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(AgentExitReason.GuardianBlocked, result.ExitReason);
        Assert.Equal(0, secondary.Calls);
        Assert.Empty(observer.Taken);
    }

    [Fact]
    public async Task AJudgeThatCannotAnswer_FailsTheTaskClosed_AndAuditsItOnce()
    {
        var provider = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = Secret } };
        var audit = new MockAuditLogger();
        var model = new FakeDecisionModel { Failure = new DecisionModelException("529") };
        var judge = new LlmJudgeGuard(
            model, Options.Create(new GuardianLlmOptions { Enabled = true }), NullLogger<LlmJudgeGuard>.Instance, audit);
        using var client = new LlmProviderToChatClientAdapter(provider);
        var orchestrator = Orchestrator(provider, client, Pipeline(judge, audit));

        var result = await orchestrator.ExecuteTaskCoreAsync(BuildAgent(), BuildTask(), BuildContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("Blocked by Guardian (output): the decision model could not judge the output", result.Error, StringComparison.Ordinal);
        Assert.Single(audit.AllLoggedEvents, e => e.Details["threatType"] == LlmJudgeGuard.UnavailableThreatType);
    }
}
