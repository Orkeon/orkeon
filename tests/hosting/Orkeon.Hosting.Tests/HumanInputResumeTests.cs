using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.HumanInput;
using Orkeon.Domain.Task;
using Orkeon.Hosting.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-23, through <see cref="RunnerExecution.RunOneShotAsync"/> (the entry point of <c>orkeon run</c>): a task asks a
/// human, the run stops with exit 75 and its checkpoint session waiting for input; <c>--resume --input</c> answers it, the
/// task that asked is replayed and gets the answer, and the tasks already done are not run again.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class HumanInputResumeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // What the agent loop does with a tool that throws: the failure is a result, not an exception.
    private sealed class AskingService(FakeAgentExecutionService inner, IHumanInputProvider human, string askOn, string prompt) : IAgentExecutionService
    {
        public List<string> Answers { get; } = [];

        public async System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        {
            var result = await inner.ExecuteTaskAsync(agent, task, context, cancellationToken).ConfigureAwait(false);
            if (task.Key != askOn)
                return result;

            try
            {
                var answer = await human.GetInputAsync(HumanInputContext.CreateTextInput(agent.Id, task.TaskId, prompt), cancellationToken).ConfigureAwait(false);
                Answers.Add(answer);
                return result with { Output = $"out:{task.Key}:{answer}" };
            }
            catch (RunSuspendedException ex)
            {
                return new TaskResult(false, "", null, [], TimeSpan.Zero, ex.Message);
            }
        }

        public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
            where TOutput : class =>
            throw new NotSupportedException();
    }

    private static void UseAsking(IServiceCollection services, FakeAgentExecutionService fake, string askOn, string prompt, out Func<AskingService?> asking)
    {
        AskingService? created = null;
        services.AddScoped<IAgentExecutionService>(sp => created = new AskingService(fake, sp.GetRequiredService<IHumanInputProvider>(), askOn, prompt));
        asking = () => created;
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldStopWithExit75AndResumeWithTheAnswer_WhenATaskAsksAHuman()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var firstPass = new FakeAgentExecutionService();
        Func<AskingService?> firstAsking = () => null;

        var stopped = await harness.RunAsync(
            crew, firstPass, configure: s => UseAsking(s, firstPass, "second", "Which colour?", out firstAsking), ct: Ct);

        Assert.Equal(75, stopped.Exit);
        Assert.Contains("--input", stopped.Stderr, StringComparison.Ordinal);
        Assert.Contains("Which colour?", stopped.Stderr, StringComparison.Ordinal);
        Assert.Equal(["first", "second"], firstPass.Calls);
        var runId = ResumeRunHarness.ResumeIdIn(stopped.Stderr);
        var state = await harness.Store().GetAsync(runId, Ct);
        Assert.Equal(SessionPhase.WaitingForInput, state!.Phase);
        Assert.Equal("Which colour?", state.PendingInput!.Prompt);
        Assert.Contains(state.TaskCheckpoints.Keys, k => k == "first#1");
        Assert.DoesNotContain(state.TaskCheckpoints.Keys, k => k.StartsWith("second", StringComparison.Ordinal));

        var secondPass = new FakeAgentExecutionService();
        Func<AskingService?> secondAsking = () => null;
        var resumed = await harness.RunAsync(
            crew, secondPass, resume: runId, input: "green",
            configure: s => UseAsking(s, secondPass, "second", "Pick a colour please", out secondAsking), ct: Ct);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        Assert.DoesNotContain("first", secondPass.Calls);
        Assert.Contains("second", secondPass.Calls);
        Assert.Contains("third", secondPass.Calls);
        Assert.Equal(["green"], secondAsking()!.Answers);
        Assert.Contains("out:second:green", secondPass.PreviousOutputsOf("third"), StringComparison.Ordinal);
        Assert.Equal(SessionPhase.Completed, (await harness.Store().GetAsync(runId, Ct))!.Phase);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldStopAgainOnTheSameQuestion_WhenResumedWithoutAnAnswer()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var pass = new FakeAgentExecutionService();
        var stopped = await harness.RunAsync(crew, pass, configure: s => UseAsking(s, pass, "second", "Which colour?", out _), ct: Ct);
        var runId = ResumeRunHarness.ResumeIdIn(stopped.Stderr);

        var again = await harness.RunAsync(
            crew, new FakeAgentExecutionService(), resume: runId, configure: s => UseAsking(s, pass, "second", "Reworded?", out _), ct: Ct);

        Assert.Equal(75, again.Exit);
        Assert.Contains("Which colour?", again.Stderr, StringComparison.Ordinal);
        Assert.Equal("Which colour?", (await harness.Store().GetAsync(runId, Ct))!.PendingInput!.Prompt);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldMaskASecretInThePromptOnStderr_AndKeepItInTheStore()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var pass = new FakeAgentExecutionService();

        var stopped = await harness.RunAsync(
            crew, pass, configure: s => UseAsking(s, pass, "second", "Use password=hunter2hunter2 for the account?", out _), ct: Ct);

        Assert.Equal(75, stopped.Exit);
        Assert.DoesNotContain("hunter2hunter2", stopped.Stderr, StringComparison.Ordinal);
        Assert.Contains("for the account?", stopped.Stderr, StringComparison.Ordinal);
        var runId = ResumeRunHarness.ResumeIdIn(stopped.Stderr);
        Assert.Contains("hunter2hunter2", (await harness.Store().GetAsync(runId, Ct))!.PendingInput!.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldBehaveAsBefore_WhenCheckpointingIsOffAndATaskAsksAHuman()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential", settings: """{ "RaggableTree": { "Enabled": false } }""");
        var pass = new FakeAgentExecutionService();
        Func<AskingService?> asking = () => null;

        var run = await harness.RunAsync(
            crew, pass, configure: s => UseAsking(s, pass, "second", "Which colour?", out asking), ct: Ct);

        // No durable layer: the host's own provider answers (the auto-approve fallback), the run goes through.
        Assert.True(run.Exit == 0, $"exit={run.Exit} stderr={run.Stderr}");
        Assert.DoesNotContain("waiting for input", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(["first", "second", "third"], pass.Calls);
        Assert.Single(asking()!.Answers);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRefuseInputWithoutResume()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");

        var refused = await harness.RunAsync(crew, new FakeAgentExecutionService(), input: "oui", ct: Ct);

        Assert.Equal(1, refused.Exit);
        Assert.Contains("--input", refused.Stderr, StringComparison.Ordinal);
        Assert.Contains("--resume", refused.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRefuseAnInput_WhenTheRunWaitsForNothing()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("second");
        var failed = await harness.RunAsync(crew, failing, ct: Ct);
        var runId = ResumeRunHarness.ResumeIdIn(failed.Stderr);

        var refused = await harness.RunAsync(crew, new FakeAgentExecutionService(), resume: runId, input: "oui", ct: Ct);

        Assert.NotEqual(0, refused.Exit);
        Assert.Contains("not waiting for an answer", refused.Stderr, StringComparison.Ordinal);
    }
}
