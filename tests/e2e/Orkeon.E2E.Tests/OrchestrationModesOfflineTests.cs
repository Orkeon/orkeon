using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.FileSystem;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.E2E.Tests;

/// <summary>
/// PUB-17 T1: one offline end-to-end kickoff per orchestration mode. The full DI
/// stack (Application + Infrastructure) executes a YAML-declared crew through
/// <see cref="ICrewOrchestrationService.KickoffAsync"/> against a scripted
/// <see cref="ILlmProvider"/> — no API key, no network. Three facts per mode:
/// the kickoff completes, it produces per-task outputs, and it actually drove
/// the LLM (the run is not a silent no-op path).
/// </summary>
public partial class OrchestrationModesOfflineTests
{
    private static string CrewYaml(string process, int tasks = 2, bool planning = false)
    {
        var taskBlocks = string.Join("\n", Enumerable.Range(1, tasks).Select(i => $"""
  step{i}:
    description: Produce part {i} of the analysis
    expected_output: Part {i} of the analysis
    agent: worker{(i % 2) + 1}
"""));
        var managerLine = process == "hierarchical" ? "\nmanagerAgent: worker1" : "";
        var planningLine = planning ? "\nplanning: true" : "";
        return $"""
name: {process}-crew
goal: Exercise the {process} orchestration mode offline
process: "{process}"{managerLine}{planningLine}
agents:
  worker1:
    role: Analyst
    goal: Analyze inputs precisely
  worker2:
    role: Writer
    goal: Write conclusions clearly
tasks:
{taskBlocks}
""";
    }

    private static Task<(CrewOutput Output, StubLlmProvider Stub, StubChatClient Chat, RecordingExecutionHook Hook)> RunAsync(
        string yaml, CancellationToken ct) => RunAsync(yaml, chatClientOverride: null, ct);

    private static async Task<(CrewOutput Output, StubLlmProvider Stub, StubChatClient Chat, RecordingExecutionHook Hook)> RunAsync(
        string yaml, IChatClient? chatClientOverride, CancellationToken ct)
    {
        var hook = new RecordingExecutionHook();
        var stub = new StubLlmProvider().RespondTo(prompt => new LlmResponse
        {
            Content = prompt.StartsWith(PlannerPromptOpening, StringComparison.Ordinal)
                ? PlanEveryTask(prompt)
                : $"[offline] answer to: {prompt[..Math.Min(40, prompt.Length)]}"
        });
        stub.RespondToChatWith(new LlmResponse { Content = "[offline] chat answer" });

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        // The host supplies its model on the three surfaces the runtime consumes — the
        // infrastructure registers none (GAP-29). The chat client is its own stub; its
        // lifetime is owned by the container (disposed with the provider).
        using var chatClient = new StubChatClient();
        services.AddSingleton<ICrewExecutionHook>(hook);
        services.AddSingleton<ILlmProvider>(stub);
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IBasicLlmProvider>(
            new Orkeon.Infrastructure.LLMs.LlmProviderAdapter(stub));
        services.AddSingleton<IChatClient>(chatClientOverride ?? chatClient);
        // Mirror the runner host: Application first (real AgentExecutionService,
        // scoped), then Infrastructure (whose stubs are TryAdd and lose).
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();

        await using var provider = services.BuildServiceProvider();

        var loader = provider.GetRequiredService<ICrewDefinitionLoader>();
        var config = await loader.LoadFromStringAsync(yaml, ct);
        var factory = provider.GetRequiredService<ICrewFactory>();
        var crew = await factory.CreateFromConfigAsync(config, ct);
        Assert.NotNull(crew);

        var orchestrator = provider.GetRequiredService<ICrewOrchestrationService>();
        var output = await orchestrator.KickoffAsync(
            crew.Id,
            new CrewInput("offline e2e", new Dictionary<string, object>()),
            ct);

        return (output, stub, chatClient, hook);
    }

    public static TheoryData<string> Modes() =>
        new("sequential", "hierarchical", "parallel", "consensual", "graph", "autonomous");

    /// <summary>
    /// GAP-32: the streaming kickoff runs the crew <see cref="ICrewOrchestrationService.KickoffAsync"/>
    /// runs, in every mode — the same tasks, agents and output — and says each task's start and end as
    /// they happen, then ends on <c>run.finished</c> with that output. It used to run a fake task per id
    /// on agents taken in turn, whatever the mode.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task The_streamed_kickoff_runs_the_crew_KickoffAsync_runs_and_says_each_tasks_start_and_end(string mode)
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = new StubLlmProvider().RespondTo(prompt => new LlmResponse { Content = $"[offline] answer to: {prompt[..Math.Min(40, prompt.Length)]}" });
        stub.RespondToChatWith(new LlmResponse { Content = "[offline] chat answer" });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        using var chatClient = new StubChatClient();
        services.AddSingleton<ILlmProvider>(stub);
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IBasicLlmProvider>(new Orkeon.Infrastructure.LLMs.LlmProviderAdapter(stub));
        services.AddSingleton<IChatClient>(chatClient);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(CrewYaml(mode), ct);
        var crew = await scope.ServiceProvider.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, ct);
        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();

        var events = new List<CrewExecutionEvent>();
        await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(crew.Id, new CrewInput("offline e2e", new Dictionary<string, object>()), ct))
            events.Add(executionEvent);
        var kicked = await orchestrator.KickoffAsync(crew.Id, new CrewInput("offline e2e", new Dictionary<string, object>()), ct);

        Assert.Equal(Orkeon.Constants.Protocol.RunEventKinds.RunFinished, events[^1].Kind);
        var streamed = events[^1].Output;
        Assert.NotNull(streamed);
        Assert.Equal(kicked.Succeeded, streamed.Succeeded);
        Assert.Equal(kicked.FinalOutput, streamed.FinalOutput);
        Assert.Equal(
            kicked.TaskOutputs.Select(o => (o.TaskId, o.Content, o.Success)),
            streamed.TaskOutputs.Select(o => (o.TaskId, o.Content, o.Success)));
        foreach (var taskId in crew.Tasks.Select(t => t.ToString()))
        {
            Assert.Single(events, e => e.Kind == Orkeon.Constants.Protocol.RunEventKinds.TaskStarted && e.TaskId == taskId);
            Assert.Single(events, e => e.Kind == Orkeon.Constants.Protocol.RunEventKinds.TaskCompleted && e.TaskId == taskId);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Kickoff_Completes_Offline(string mode)
    {
        var (output, _, _, _) = await RunAsync(CrewYaml(mode), TestContext.Current.CancellationToken);

        Assert.NotNull(output);
        Assert.False(string.IsNullOrWhiteSpace(output.FinalOutput),
            $"{mode}: expected a non-empty final output.");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Kickoff_ProducesPerTaskOutputs(string mode)
    {
        var (output, _, _, _) = await RunAsync(CrewYaml(mode), TestContext.Current.CancellationToken);

        Assert.NotEmpty(output.TaskOutputs);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Every_mode_announces_a_task_before_it_reports_it_finished(string mode)
    {
        // STUDIO-17: the run screen shows a task as in progress between these two notifications.
        // A mode that only reported completions left the screen unable to tell a working run
        // from a stalled one — so every mode has to announce the start, and announce it first.
        var (_, _, _, hook) = await RunAsync(CrewYaml(mode), TestContext.Current.CancellationToken);

        Assert.NotEmpty(hook.CompletedTasks);
        Assert.Equal(hook.CompletedTasks.Order(), hook.StartedTasks.Order());

        var timeline = hook.Timeline.ToList();
        foreach (var taskId in hook.CompletedTasks)
        {
            Assert.True(
                timeline.IndexOf($"started:{taskId}") < timeline.IndexOf($"completed:{taskId}"),
                $"{mode}: task {taskId} was reported finished before it was announced. Timeline: {string.Join(", ", timeline)}");
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Kickoff_DrivesTheLlm(string mode)
    {
        var (_, stub, chat, _) = await RunAsync(CrewYaml(mode), TestContext.Current.CancellationToken);

        var llmCalls = stub.GenerateCalls.Count + stub.ChatCalls.Count + chat.CallCount;
        Assert.True(llmCalls > 0,
            $"{mode}: the crew completed without a single LLM exchange — the mode ran a no-op path.");
    }

    /// <summary>The first words of the crew planner's prompt (GAP-31).</summary>
    private const string PlannerPromptOpening = "You are the planner of an agent crew.";

    /// <summary>A task the planning prompt numbers: its number, and the part its description asks for.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^Task (\d+)\r?\nDescription: Produce part (\d+) of the analysis", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex PlannedPart();

    /// <summary>The user prompt of a task's execution: the part its description asks for.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\ATask:\r?\nProduce part (\d+) of the analysis")]
    private static partial System.Text.RegularExpressions.Regex ExecutedPart();

    /// <summary>One plan per task the planning prompt numbers: "PLAN for part N" for "Produce part N …".</summary>
    private static string PlanEveryTask(string planningPrompt)
    {
        var plans = PlannedPart().Matches(planningPrompt)
            .Select(m => $$"""{"task":{{m.Groups[1].Value}},"plan":"PLAN for part {{m.Groups[2].Value}}"}""");
        return $$"""{"plans":[{{string.Join(",", plans)}}]}""";
    }

    /// <summary>
    /// GAP-31: with <c>planning: true</c>, the planner's plan for a task reaches that task's prompt —
    /// and only that task's — in every mode: through the one composer every mode's executions go
    /// through. Hierarchical and Autonomous used to make the plan and ignore it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Every_mode_puts_each_tasks_plan_in_its_prompt(string mode)
    {
        var (_, stub, chat, _) = await RunAsync(CrewYaml(mode, planning: true), TestContext.Current.CancellationToken);

        Assert.Single(stub.GenerateCalls, prompt => prompt.StartsWith(PlannerPromptOpening, StringComparison.Ordinal));
        foreach (var part in new[] { 1, 2 })
        {
            var prompts = chat.UserPrompts
                .Where(prompt => ExecutedPart().Match(prompt) is { Success: true } match
                    && match.Groups[1].Value == part.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            Assert.True(prompts.Count > 0, $"{mode}: task {part} never reached the model");
            Assert.All(prompts, prompt =>
            {
                Assert.Contains($"PLAN for part {part}", prompt, StringComparison.Ordinal);
                Assert.DoesNotContain($"PLAN for part {3 - part}", prompt, StringComparison.Ordinal);
            });
        }

        // A ballot is a task of its own, not the task it judges: it reads no plan.
        Assert.DoesNotContain(chat.UserPrompts, prompt =>
            prompt.Contains("casting a ballot", StringComparison.Ordinal) && prompt.Contains("PLAN for part", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Kickoff_ProducesOneOutputPerDeclaredTask(string mode)
    {
        const int declaredTasks = 2;
        var (output, _, _, _) = await RunAsync(CrewYaml(mode, declaredTasks), TestContext.Current.CancellationToken);

        // No task silently dropped, none executed twice into the result set.
        Assert.Equal(declaredTasks, output.TaskOutputs.Count);
    }

    /// <summary>The host's offline IChatClient: no network, no key.</summary>
    /// <summary>
    /// R5 of the rc.2 train: **no orchestration mode is second-class**. Before BUS-03 only
    /// the sequential strategy notified <see cref="ICrewExecutionHook"/>, so anything
    /// observing a run — Studio's screen, AUTO_SUMMARY.md — saw nothing on the five others.
    /// This is the acceptance test for that decision, and it runs on all six modes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Kickoff_NotifiesTheExecutionHook_OnEveryMode(string mode)
    {
        var (output, _, _, hook) = await RunAsync(CrewYaml(mode), TestContext.Current.CancellationToken);

        Assert.NotEmpty(output.TaskOutputs);
        Assert.NotEmpty(hook.CompletedTasks);
        Assert.True(
            hook.CrewCompletions + hook.CrewFailures > 0,
            $"mode '{mode}' never reported a crew-level outcome to the hook");
    }

    /// <summary>
    /// The half the happy-path theory above cannot see: a run that stops — cancelled, or
    /// broken by its model — must still deliver a terminal event. Before this review, only
    /// the sequential mode had the fault barrier; on the five others a Ctrl+C ended the run
    /// with the watcher's screen frozen mid-progress, and no test could tell.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_cancelled_run_still_reports_a_terminal_event(string mode)
    {
        using var cts = new CancellationTokenSource();
        using var cancellingClient = new CancelOnFirstCallChatClient(cts);

        var (_, _, _, hook) = await RunAsync(CrewYaml(mode), cancellingClient, cts.Token);

        // Terminal event, not specifically CrewFailed: the execution service converts a
        // cooperative cancellation into a failed TaskResult, so a mode whose fan-out already
        // completed (parallel, legitimately) reports Completed with failed tasks. What no
        // mode may do is go silent — the frozen-screen failure this theory exists to pin.
        Assert.True(
            hook.CrewCompletions + hook.CrewFailures > 0,
            $"mode '{mode}' went silent on cancellation — no terminal event reached the hook");
    }

    /// <summary>
    /// Cancels the shared token from inside the first LLM exchange, then refuses it — the
    /// closest offline stand-in for a user pressing Ctrl+C while the model is generating.
    /// </summary>
    private sealed class CancelOnFirstCallChatClient(CancellationTokenSource cts) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            throw new OperationCanceledException(cts.Token);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException(cts.Token);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing owned.
        }
    }

    /// <summary>Records what the orchestration strategies notify, for the test above.</summary>
    internal sealed class RecordingExecutionHook : ICrewExecutionHook
    {
        private readonly Lock _gate = new();
        private readonly List<string> _tasks = [];
        private readonly List<string> _started = [];
        private readonly List<string> _order = [];

        public IReadOnlyList<string> CompletedTasks
        {
            get { lock (_gate) { return [.. _tasks]; } }
        }

        /// <summary>Task ids whose start was announced, in order.</summary>
        public IReadOnlyList<string> StartedTasks
        {
            get { lock (_gate) { return [.. _started]; } }
        }

        /// <summary>Every task notification as "started:id" / "completed:id", in arrival order.</summary>
        public IReadOnlyList<string> Timeline
        {
            get { lock (_gate) { return [.. _order]; } }
        }

        public int CrewCompletions { get; private set; }

        public int CrewFailures { get; private set; }

        public Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct)
        {
            lock (_gate)
            {
                _started.Add(snapshot.TaskId);
                _order.Add($"started:{snapshot.TaskId}");
            }

            return Task.CompletedTask;
        }

        public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
        {
            lock (_gate)
            {
                _tasks.Add(snapshot.TaskId);
                _order.Add($"completed:{snapshot.TaskId}");
            }

            return Task.CompletedTask;
        }

        public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
        {
            lock (_gate) { CrewCompletions++; }
            return Task.CompletedTask;
        }

        public Task OnCrewFailedAsync(CrewExecutionSnapshot snapshot, Exception? ex, CancellationToken ct)
        {
            lock (_gate) { CrewFailures++; }
            return Task.CompletedTask;
        }
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _userPrompts = new();
        private int _callCount;

        public int CallCount => _callCount;

        /// <summary>The first user message of every call, in arrival order: what each execution was asked.</summary>
        public IReadOnlyList<string> UserPrompts => [.. _userPrompts];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            if (messages.FirstOrDefault(m => m.Role == ChatRole.User) is { } user)
                _userPrompts.Enqueue(user.Text);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "[offline] chat client answer")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
            // Nothing to dispose.
        }
    }
}
