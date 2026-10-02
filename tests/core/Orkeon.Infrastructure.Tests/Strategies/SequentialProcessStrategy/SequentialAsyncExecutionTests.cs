using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// GAP-22 — a sequential crew honours <c>asyncExecution: true</c>, with CrewAI's semantics: the task is
/// launched without being waited for and the next one starts at once; a task that depends on it waits
/// for it; the crew waits for every task it launched before it reports, and its output stays the last
/// declared task's. The flag used to be read, mapped and stored on the task, and the two "concurrent"
/// research tasks of a pipeline ran one after the other.
/// <para>
/// Overlap is proven by rendezvous, never by a stopwatch: each side signals, then waits for the other
/// to be in flight. A run that executes one task after another exhausts <see cref="Patience"/> and
/// fails the assertion — deterministically, under any machine load.
/// </para>
/// </summary>
public sealed class SequentialAsyncExecutionTests : IDisposable
{
    /// <summary>How long one side of a rendezvous waits for the other; only a run that never overlaps uses it up.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly InMemoryTaskRepository _tasks;
    private readonly InMemoryAgentRepository _agents;
    private readonly MockMemoryScope _memoryScope = new();
    private readonly MockCrewExecutionHook _hook = new();
    private readonly RecordingDomainEventDispatcher _events = new();
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<TaskResult>>> _behaviours = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SimpleExecutionContext> _contexts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _executed = new();
    private readonly FakeAgentExecutionService _execution;

    public SequentialAsyncExecutionTests()
    {
        var unitOfWork = new NullUnitOfWork();
        _tasks = new InMemoryTaskRepository(unitOfWork);
        _agents = new InMemoryAgentRepository(unitOfWork);

        // Each execution is recorded with the context it was given, then answers as the test
        // scripted it — "<name> done" by default.
        _execution = new FakeAgentExecutionService((_, task, context, ct) =>
        {
            var name = task.Description.Value;
            _contexts[name] = context;
            _executed.Enqueue(name);
            return _behaviours.TryGetValue(name, out var behaviour)
                ? behaviour(ct)
                : Task.FromResult(Done($"{name} done"));
        });
    }

    public void Dispose() => _memoryScope.Dispose();

    [Fact]
    public async Task Two_async_tasks_run_at_once_and_the_task_depending_on_both_waits_for_their_outputs()
    {
        var worker = Agent("Worker");
        var research = AsyncTask("research");
        var survey = AsyncTask("survey");
        var synthesis = SyncTask("synthesis", research, survey);
        var researchRunning = Signal();
        var surveyRunning = Signal();
        var overlapped = new ConcurrentQueue<string>();
        _behaviours["research"] = async _ =>
        {
            researchRunning.TrySetResult();
            if (await WithinPatienceAsync(surveyRunning.Task))
                overlapped.Enqueue("research");
            return Done("research findings");
        };
        _behaviours["survey"] = async _ =>
        {
            surveyRunning.TrySetResult();
            if (await WithinPatienceAsync(researchRunning.Task))
                overlapped.Enqueue("survey");
            return Done("survey findings");
        };

        var output = await RunAsync([worker], research, survey, synthesis);

        Assert.True(output.Success, output.Error);
        // Each saw the other in flight: they ran together.
        Assert.Equal(["research", "survey"], overlapped.Order(StringComparer.Ordinal));
        // The task depending on both ran once both were done, and read both outputs.
        Assert.Equal(["research findings", "survey findings"], Contents("synthesis"));
        // The outcome lists the tasks in their declared order, and ends on the last one.
        Assert.Equal([research.Id, survey.Id, synthesis.Id], output.TaskOutputs.Select(o => o.TaskId));
        Assert.Equal("synthesis done", output.Output);
    }

    [Fact]
    public async Task An_async_task_that_fails_skips_its_dependant_and_fails_the_crew_while_its_sibling_runs_to_its_end()
    {
        var worker = Agent("Worker");
        var research = AsyncTask("research");
        var survey = AsyncTask("survey");
        var synthesis = SyncTask("synthesis", research, survey);
        var surveyFailed = Signal();
        var researchOutlivedTheFailure = false;
        var researchCancelled = true;
        _behaviours["survey"] = _ =>
        {
            surveyFailed.TrySetResult();
            return Task.FromResult(Failed("rate limited"));
        };
        _behaviours["research"] = async ct =>
        {
            researchOutlivedTheFailure = await WithinPatienceAsync(surveyFailed.Task);
            researchCancelled = ct.IsCancellationRequested;
            return Done("research findings");
        };

        var output = await RunAsync([worker], research, survey, synthesis);

        Assert.False(output.Success);
        Assert.NotNull(output.Error);
        Assert.Contains(survey.Id.ToString(), output.Error, StringComparison.Ordinal);
        Assert.Contains("rate limited", output.Error, StringComparison.Ordinal);
        Assert.Contains(synthesis.Id.ToString(), output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(research.Id.ToString(), output.Error, StringComparison.Ordinal);

        // The sibling was still running when the failure happened, and nothing cancelled it.
        Assert.True(researchOutlivedTheFailure, "research was not in flight when survey failed");
        Assert.False(researchCancelled);
        Assert.Equal(TaskStatus.Completed, research.Status);
        Assert.Equal("research findings", output.TaskOutputs[0].Output);

        // The dependant never ran: skipped, cancelled with the reason.
        Assert.DoesNotContain("synthesis", _executed);
        Assert.Equal(TaskStatus.Failed, survey.Status);
        Assert.Equal(TaskStatus.Cancelled, synthesis.Status);
        Assert.Equal([research.Id, survey.Id, synthesis.Id], output.TaskOutputs.Select(o => o.TaskId));
        Assert.Equal(CrewHookStatus.Failed, Assert.Single(_hook.Failures).Status);
    }

    [Fact]
    public async Task An_async_task_nothing_depends_on_is_awaited_before_the_crew_reports_and_the_last_declared_task_gives_the_output()
    {
        var worker = Agent("Worker");
        var archive = AsyncTask("archive");
        var report = SyncTask("report");
        var reportRan = Signal();
        var archiveOutlivedTheReport = false;
        _behaviours["report"] = _ =>
        {
            reportRan.TrySetResult();
            return Task.FromResult(Done("the report"));
        };
        _behaviours["archive"] = async _ =>
        {
            archiveOutlivedTheReport = await WithinPatienceAsync(reportRan.Task);
            return Done("archived");
        };

        var output = await RunAsync([worker], archive, report);

        Assert.True(output.Success, output.Error);
        Assert.True(archiveOutlivedTheReport, "report did not run while archive was in flight");

        // Awaited, never forgotten: its output is in the outcome, its completion was dispatched,
        // and the hook heard it before the crew ended.
        Assert.Equal(["archived", "the report"], output.TaskOutputs.Select(o => o.Output));
        Assert.Equal(TaskStatus.Completed, archive.Status);
        Assert.Contains(_events.Dispatched.OfType<TaskCompletedEvent>(), e => e.TaskId == archive.Id);
        Assert.Single(_hook.Completions);

        // The hook hears each task as it ends — report, then archive, the order they finished in.
        Assert.Equal(
            [report.Id.Value.ToString(), archive.Id.Value.ToString()],
            _hook.CompletedTasks.Select(s => s.TaskId));

        // The crew's output is the last declared task's, not the last to finish.
        Assert.Equal("the report", output.Output);
    }

    [Fact]
    public async Task An_async_output_reaches_the_context_of_the_next_tasks_only_once_a_task_waited_for_it()
    {
        var worker = Agent("Worker");
        var fetch = AsyncTask("fetch");
        var draft = SyncTask("draft");
        var review = SyncTask("review");
        var publish = SyncTask("publish", fetch);
        var notify = SyncTask("notify");
        var fetched = Signal();
        _behaviours["fetch"] = _ =>
        {
            fetched.TrySetResult();
            return Task.FromResult(Done("the data"));
        };
        _behaviours["draft"] = async _ =>
        {
            await WithinPatienceAsync(fetched.Task);
            return Done("the draft");
        };

        var output = await RunAsync([worker], fetch, draft, review, publish, notify);

        Assert.True(output.Success, output.Error);
        // Launched, not awaited: the next task starts without its output.
        Assert.Empty(Contents("draft"));
        // Finished by then, but never waited for: still not in the context — whatever the timing.
        Assert.Equal(["the draft"], Contents("review"));
        // Waited for by the task that depends on it: from then on in every context, in declared order.
        Assert.Equal(["the data", "the draft", "review done"], Contents("publish"));
        Assert.Equal(["the data", "the draft", "review done", "publish done"], Contents("notify"));
    }

    [Fact]
    public async Task An_async_task_starts_when_launched_and_ends_when_awaited_its_agent_running_the_next_task_meanwhile()
    {
        var worker = Agent("Worker");
        var fetch = AsyncTask("fetch");
        var draft = SyncTask("draft");
        var draftRan = Signal();
        var agentRanBoth = false;
        _behaviours["draft"] = _ =>
        {
            agentRanBoth = worker.CurrentTasks.Contains(fetch.Id) && worker.CurrentTasks.Contains(draft.Id);
            draftRan.TrySetResult();
            return Task.FromResult(Done("the draft"));
        };
        _behaviours["fetch"] = async _ =>
        {
            await WithinPatienceAsync(draftRan.Task);
            return Done("the data");
        };

        var output = await RunAsync([worker], fetch, draft);

        Assert.True(output.Success, output.Error);
        Assert.True(agentRanBoth, "the agent was not running the async task while it ran the next one");
        // Started when launched, before the next task; ended when the crew waited for it, at the end.
        Assert.Equal([fetch.Id, draft.Id], _events.Dispatched.OfType<TaskStartedEvent>().Select(e => e.TaskId));
        Assert.Equal([draft.Id, fetch.Id], _events.Dispatched.OfType<TaskCompletedEvent>().Select(e => e.TaskId));
        Assert.Equal(TaskStatus.Completed, fetch.Status);
        Assert.Empty(worker.CurrentTasks);
    }

    [Fact]
    public async Task An_async_task_that_depends_on_another_starts_once_that_one_is_done_and_reads_its_output()
    {
        var worker = Agent("Worker");
        var fetch = AsyncTask("fetch");
        var parse = AsyncTask("parse", fetch);
        var report = SyncTask("report", parse);

        var output = await RunAsync([worker], fetch, parse, report);

        Assert.True(output.Success, output.Error);
        Assert.Equal(["fetch done"], Contents("parse"));
        Assert.Equal(["fetch done", "parse done"], Contents("report"));
        Assert.Equal("report done", output.Output);
    }

    [Fact]
    public async Task A_task_that_fails_fails_the_crew_without_cancelling_the_async_task_in_flight()
    {
        var worker = Agent("Worker");
        var archive = AsyncTask("archive");
        var draft = SyncTask("draft");
        var publish = SyncTask("publish", draft);
        var draftFailed = Signal();
        var archiveOutlivedTheFailure = false;
        var archiveCancelled = true;
        _behaviours["draft"] = _ =>
        {
            draftFailed.TrySetResult();
            return Task.FromResult(Failed("no final answer"));
        };
        _behaviours["archive"] = async ct =>
        {
            archiveOutlivedTheFailure = await WithinPatienceAsync(draftFailed.Task);
            archiveCancelled = ct.IsCancellationRequested;
            return Done("archived");
        };

        var output = await RunAsync([worker], archive, draft, publish);

        Assert.False(output.Success);
        Assert.True(archiveOutlivedTheFailure, "archive was not in flight when draft failed");
        Assert.False(archiveCancelled);
        Assert.Equal(TaskStatus.Completed, archive.Status);
        Assert.Equal(TaskStatus.Failed, draft.Status);
        Assert.Equal(TaskStatus.Cancelled, publish.Status);
        Assert.DoesNotContain(archive.Id.ToString(), output.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_cancelled_while_an_async_task_runs_waits_for_it_to_stop_then_cancels_it()
    {
        var worker = Agent("Worker");
        var fetch = AsyncTask("fetch");
        var draft = SyncTask("draft");
        using var run = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fetchRunning = Signal();
        var fetchStopped = 0;
        _behaviours["fetch"] = async ct =>
        {
            try
            {
                fetchRunning.TrySetResult();
                await Task.Delay(Patience, ct);
                return Done("the data");
            }
            finally
            {
                Interlocked.Exchange(ref fetchStopped, 1);
            }
        };
        _behaviours["draft"] = async ct =>
        {
            await WithinPatienceAsync(fetchRunning.Task);
            await run.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return Done("the draft");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunUntilCancelledAsync([worker], [fetch, draft], run.Token));

        // Nothing outlives the run: the async task had stopped before the run reported.
        Assert.Equal(1, Volatile.Read(ref fetchStopped));
        Assert.Equal(TaskStatus.Cancelled, fetch.Status);
        Assert.Equal(TaskStatus.Cancelled, draft.Status);
        var cancelled = _events.Dispatched.OfType<TaskCancelledEvent>().ToList();
        Assert.Equal(2, cancelled.Count);
        Assert.All(cancelled, e => Assert.Equal("the run was cancelled", e.Reason));
        Assert.Equal(CrewHookStatus.Canceled, Assert.Single(_hook.Failures).Status);
        Assert.Empty(worker.CurrentTasks);
    }

    [Fact]
    public async Task A_delegation_from_an_async_task_runs_in_that_tasks_context_not_in_the_one_the_run_moved_on_to()
    {
        var lead = Agent("Lead", allowDelegation: true);
        var writer = Agent("Writer");
        var intro = SyncTask("intro");
        var outline = AsyncTask("outline");
        var notes = SyncTask("notes");
        var review = SyncTask("review");
        intro.AssignTo(writer.Id);
        outline.AssignTo(lead.Id);
        notes.AssignTo(writer.Id);
        review.AssignTo(writer.Id);
        var reviewRunning = Signal();
        var delegated = Signal();
        _behaviours["outline"] = async ct =>
        {
            // The run has moved on: review runs, with notes in its context.
            await WithinPatienceAsync(reviewRunning.Task);
            var delegate_ = lead.Tools.First(t => t.Name == "delegate_work_to_coworker");
            var answer = await delegate_.CallAsync(DelegateRequest("Write the first paragraph"), ct);
            delegated.TrySetResult();
            return answer.Success ? Done("the outline") : Failed(answer.Error ?? "the delegation failed");
        };
        _behaviours["review"] = async _ =>
        {
            reviewRunning.TrySetResult();
            await WithinPatienceAsync(delegated.Task);
            return Done("the review");
        };

        var output = await RunAsync([lead, writer], intro, outline, notes, review);

        Assert.True(output.Success, output.Error);
        Assert.Equal(["intro done", "notes done"], Contents("review"));
        // The coworker works in the context of the task it serves — outline's, which holds intro only.
        Assert.Equal(["intro done"], Contents("Write the first paragraph"));
    }

    private DomainAgent Agent(string role, bool allowDelegation = false)
    {
        var agent = new AgentBuilder()
            .Role(role).Goal($"Goal of {role}").Backstory($"{role} works")
            .AllowDelegation(allowDelegation)
            .Build();
        _agents.AddAsync(agent).GetAwaiter().GetResult();
        return agent;
    }

    private CrewTask AsyncTask(string name, params CrewTask[] dependsOn) => Declare(name, asyncExecution: true, dependsOn);

    private CrewTask SyncTask(string name, params CrewTask[] dependsOn) => Declare(name, asyncExecution: false, dependsOn);

    private CrewTask Declare(string name, bool asyncExecution, CrewTask[] dependsOn)
    {
        var builder = new CrewTaskBuilder().Description(name).ExpectedOutput(name).Async(asyncExecution);
        if (dependsOn.Length > 0)
            builder.DependsOn(dependsOn);
        var task = builder.Build();
        _tasks.AddAsync(task).GetAwaiter().GetResult();
        return task;
    }

    /// <summary>The outputs a task's context carried when its agent ran it, in order.</summary>
    private IReadOnlyList<string> Contents(string task) =>
        [.. _contexts[task].PreviousOutputs.Select(o => o.Content)];

    private static TaskResult Done(string output) => new(true, output, null, [], TimeSpan.Zero);

    private static TaskResult Failed(string error) => new(false, "", null, [], TimeSpan.Zero, Error: error);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether <paramref name="signal"/> fires within <see cref="Patience"/>. Never throws.</summary>
    private static async Task<bool> WithinPatienceAsync(Task signal)
    {
        try
        {
            await signal.WaitAsync(Patience, CancellationToken.None);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static ToolCallRequest DelegateRequest(string task) =>
        new(
            ToolName: "delegate_work_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = task,
                ["context"] = "",
                ["coworker_role"] = "Writer",
                ["wait_for_result"] = true,
            });

    private SequentialProcessStrategy Strategy() =>
        new(
            new CrewStrategyDependencies(_tasks, _agents, _execution, _memoryScope, _events),
            new AgentDelegationToolsProvider(
                new MockAgentCommunicationService(), _execution, NullLogger<AgentDelegationToolsProvider>.Instance),
            NullLogger<SequentialProcessStrategy>.Instance,
            _hook);

    private Task<DomainCrewOutput> RunAsync(DomainAgent[] agents, params CrewTask[] tasks) =>
        RunUntilCancelledAsync(agents, tasks, TestContext.Current.CancellationToken);

    private async Task<DomainCrewOutput> RunUntilCancelledAsync(DomainAgent[] agents, CrewTask[] tasks, CancellationToken cancellationToken)
    {
        var builder = new CrewBuilder().Goal("Pipeline").Sequential();
        foreach (var agent in agents)
            builder.WithAgent(agent);
        foreach (var task in tasks)
            builder.WithTask(task);
        return await Strategy().ExecuteSequentialAsync(
            builder.Build(), CrewExecutionPlan.Create(), cancellationToken: cancellationToken);
    }
}
