using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.Events;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Tests.Shared.FileSystem;
using CrewStatus = Orkeon.Domain.Crew.ValueObjects.CrewStatus;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using ExecutionStatus = Orkeon.Domain.Crew.ExecutionStatus;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// GAP-32 — the streaming kickoff runs the crew <c>KickoffAsync</c> runs, through the host's own
/// composition (<c>AddOrkeonApplication</c> + <c>AddOrkeonInfrastructure</c> + a streaming provider
/// registered by <c>AddOrkeonLlmProvider</c>), and says what happens as it goes: each task's start,
/// its tool calls and the model's text, its end, then <c>run.finished</c> with the run's output. It
/// used to run a fake task per id — the GUID as its description — on agents taken in turn, with
/// neither the crew's mode, memory, knowledge nor plan, and said no result.
/// </summary>
public sealed class StreamingKickoffTests
{
    private const string LaunchCrew = """
        name: launch-desk
        goal: Publish the launch notes
        process: PROCESS
        agents:
          writer:
            role: Writer
            goal: Write the launch notes
          editor:
            role: Editor
            goal: Edit the launch notes
        tasks:
          draft:
            description: Draft the launch notes
            expected_output: The draft
            agent: writer
          review:
            description: Review the launch notes
            expected_output: The review
            agent: editorDEPENDENCIES
        """;

    private static string Launch(string process = "sequential", bool reviewDependsOnDraft = true) =>
        LaunchCrew
            .Replace("PROCESS", process, StringComparison.Ordinal)
            .Replace("DEPENDENCIES", reviewDependsOnDraft ? "\n    dependencies: [draft]" : string.Empty, StringComparison.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool Asks(LlmMessage[] messages, string words) =>
        messages.Any(m => m.Role == "user" && m.Content.Contains(words, StringComparison.Ordinal));

    private static LlmResponse Final(string content) =>
        new() { Content = content, PromptTokens = 10, CompletionTokens = 4, TokensUsed = 14 };

    /// <summary>Each task its own answer, in chunks.</summary>
    private static MockStreamingLlmProvider.StreamedTurn Turn(LlmMessage[] messages) =>
        Asks(messages, "Review the launch notes")
            ? new(["The review: ", "ship it."], Final("The review: ship it."))
            : new(["The draft: ", "v1 is ", "out."], Final("The draft: v1 is out."));

    private static MockStreamingLlmProvider Vendor()
    {
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true, Name = "vendor" };
        vendor.SetChatStreamingFunc((messages, _) => Turn(messages));
        vendor.SetChatFunc((messages, _) => Turn(messages).Final);
        return vendor;
    }

    private static ServiceProvider Host(
        MockStreamingLlmProvider vendor,
        RecordingLoggerProvider? logs = null,
        Action<IServiceCollection>? configure = null,
        Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddLogging(builder =>
        {
            if (logs is not null)
                builder.AddProvider(logs).SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => vendor, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<DomainCrew> LoadAsync(IServiceProvider sp, string yaml)
    {
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        return await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
    }

    private static async Task<List<CrewExecutionEvent>> StreamAsync(IServiceProvider sp, DomainCrew crew)
    {
        var events = new List<CrewExecutionEvent>();
        await foreach (var executionEvent in sp.GetRequiredService<ICrewOrchestrationService>()
            .KickoffStreamingAsync(crew.Id, CrewInput.Empty("launch"), Ct))
        {
            events.Add(executionEvent);
        }

        return events;
    }

    private static string Prompt(LlmMessage[] messages) => string.Join("\n", messages.Select(m => m.Content));

    [Fact]
    public async Task Each_task_is_asked_with_its_own_words_its_declared_agent_and_the_output_it_depends_on()
    {
        var vendor = Vendor();
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var crew = await LoadAsync(scope.ServiceProvider, Launch());

        await StreamAsync(scope.ServiceProvider, crew);

        var calls = vendor.ChatStreamingMessages;
        Assert.Equal(2, calls.Count);
        Assert.Contains("You are Writer.", calls[0][0].Content, StringComparison.Ordinal);
        Assert.True(Asks(calls[0], "Draft the launch notes"));
        Assert.Contains("You are Editor.", calls[1][0].Content, StringComparison.Ordinal);
        Assert.True(Asks(calls[1], "Review the launch notes"));
        Assert.Contains("The draft: v1 is out.", Prompt(calls[1]), StringComparison.Ordinal);
        Assert.DoesNotContain(crew.Tasks[0].ToString(), Prompt(calls[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_task_streams_its_start_its_text_and_its_end_then_the_run_finishes_on_the_output_KickoffAsync_returns()
    {
        var vendor = Vendor();
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var crew = await LoadAsync(scope.ServiceProvider, Launch());
        var draft = crew.Tasks[0].ToString();
        var review = crew.Tasks[1].ToString();

        var events = await StreamAsync(scope.ServiceProvider, crew);

        Assert.Equal(
            [RunEventKinds.TaskStarted, RunEventKinds.LlmDelta, RunEventKinds.LlmDelta, RunEventKinds.LlmDelta, RunEventKinds.TaskCompleted,
             RunEventKinds.TaskStarted, RunEventKinds.LlmDelta, RunEventKinds.LlmDelta, RunEventKinds.TaskCompleted,
             RunEventKinds.RunFinished],
            events.Select(e => e.Kind));
        Assert.All(events.Take(5), e => Assert.Equal(draft, e.TaskId));
        Assert.All(events.Skip(5).Take(4), e => Assert.Equal(review, e.TaskId));
        Assert.All(events.Take(5), e => Assert.Equal("Writer", e.AgentRole));
        Assert.All(events.Skip(5).Take(4), e => Assert.Equal("Editor", e.AgentRole));
        Assert.Equal(["The draft: ", "v1 is ", "out."], events.Take(5).Where(e => e.Kind == RunEventKinds.LlmDelta).Select(e => e.Text));
        Assert.True(events[4].Success);
        Assert.Equal(14, events[4].Tokens);

        // The deltas add up to each task's output, and the run's output is KickoffAsync's.
        var streamed = events[^1].Output;
        Assert.NotNull(streamed);
        Assert.True(streamed.Succeeded, streamed.Error);
        Assert.Equal("The draft: v1 is out.", streamed.TaskOutputs.Single(o => o.TaskId == draft).Content);
        Assert.Equal("The review: ship it.", streamed.TaskOutputs.Single(o => o.TaskId == review).Content);

        var kicked = await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>()
            .KickoffAsync(crew.Id, CrewInput.Empty("launch"), Ct);
        Assert.Equal(kicked.Succeeded, streamed.Succeeded);
        Assert.Equal(kicked.FinalOutput, streamed.FinalOutput);
        Assert.Equal(kicked.TokensUsed, streamed.TokensUsed);
        Assert.Equal(
            kicked.TaskOutputs.Select(o => (o.TaskId, o.Content, o.Success)),
            streamed.TaskOutputs.Select(o => (o.TaskId, o.Content, o.Success)));
    }

    [Fact]
    public async Task KickoffAsync_never_streams_a_turn()
    {
        var vendor = Vendor();
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var crew = await LoadAsync(scope.ServiceProvider, Launch());

        var output = await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>()
            .KickoffAsync(crew.Id, CrewInput.Empty("launch"), Ct);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(2, vendor.ChatCallCount);
        Assert.Equal(0, vendor.ChatStreamingCallCount);
    }

    [Fact]
    public async Task The_streamed_run_moves_its_tasks_and_dispatches_the_crews_events()
    {
        // The rewrite of The_granular_streaming_path_dispatches_the_queued_events_too: the granular
        // path never started the crew, never moved a task, and dispatched the construction events alone.
        var recorder = new EventRecorder();
        await using var container = Host(Vendor(), configure: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IDomainEventHandler<TaskStartedEvent>, RecordingHandler>();
            services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, RecordingHandler>();
            services.AddScoped<IDomainEventHandler<CrewExecutionStartedEvent>, RecordingHandler>();
            services.AddScoped<IDomainEventHandler<CrewExecutionCompletedEvent>, RecordingHandler>();
        });
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var crew = await LoadAsync(sp, Launch());

        await StreamAsync(sp, crew);

        Assert.Equal(
            [nameof(TaskStartedEvent), nameof(TaskCompletedEvent), nameof(TaskStartedEvent), nameof(TaskCompletedEvent),
             nameof(CrewExecutionStartedEvent), nameof(CrewExecutionCompletedEvent)],
            recorder.Names);
        var tasks = sp.GetRequiredService<ITaskRepository>();
        Assert.Equal(TaskStatus.Completed, (await tasks.GetByIdAsync(crew.Tasks[0], Ct))?.Status);
        Assert.Equal(TaskStatus.Completed, (await tasks.GetByIdAsync(crew.Tasks[1], Ct))?.Status);
        Assert.Equal(ExecutionStatus.Succeeded, Assert.Single(crew.Executions).Status);
    }

    [Fact]
    public async Task A_streamed_crew_with_memory_recalls_its_first_run_in_its_second_without_a_warning()
    {
        const string rememberingCrew = """
            name: news-desk
            goal: Publish the AI news summary
            process: sequential
            memory: true
            agents:
              analyst:
                role: Analyst
                goal: Summarize the news
            tasks:
              summary:
                description: Summarize this week's AI news for the newsletter
                expected_output: Five bullet points about the AI news
                agent: analyst
            """;
        var answers = new ConcurrentQueue<string>(["Week 39 AI news: chips, regulation and open models.", "Week 40 AI news: agents everywhere."]);
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true };
        vendor.SetChatStreamingFunc((_, _) =>
        {
            answers.TryDequeue(out var answer);
            return new([answer!], Final(answer!));
        });
        var embedder = new MockEmbeddingProvider();
        embedder.SetEmbeddingFunc(BagOfWords);
        using var logs = new RecordingLoggerProvider();
        await using var container = Host(vendor, logs, services => services.AddSingleton<IEmbeddingProvider>(embedder),
            new Dictionary<string, string?> { ["Orkeon:CrewMemory:MinScore"] = "0.3" });

        await using (var first = container.CreateAsyncScope())
            await StreamAsync(first.ServiceProvider, await LoadAsync(first.ServiceProvider, rememberingCrew));
        await using (var second = container.CreateAsyncScope())
            await StreamAsync(second.ServiceProvider, await LoadAsync(second.ServiceProvider, rememberingCrew));

        var calls = vendor.ChatStreamingMessages;
        Assert.DoesNotContain(PromptDefaults.MemoriesHeader, Prompt(calls[0]), StringComparison.Ordinal);
        Assert.Contains(PromptDefaults.MemoriesHeader, Prompt(calls[^1]), StringComparison.Ordinal);
        Assert.Contains("Week 39 AI news: chips, regulation and open models.", Prompt(calls[^1]), StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("streamed run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_streamed_agents_knowledge_reaches_its_prompt()
    {
        const string knowledgeCrew = """
            name: support-desk
            goal: Answer from the FAQ
            process: sequential
            agents:
              support:
                role: Support agent
                goal: Answer questions from the knowledge base
                knowledge: [faq]
            tasks:
              answer:
                description: How many days do customers have to request a refund?
                expected_output: A grounded answer
                agent: support
            """;
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true };
        vendor.SetChatStreamingFunc((_, _) => new(["Thirty days."], Final("Thirty days.")));
        await using var container = Host(vendor, configure: services =>
            services.AddSingleton<IKnowledgeContextAugmenter>(new FixedKnowledge("[1] Refund policy: a full refund within 30 days of purchase.")));
        await using var scope = container.CreateAsyncScope();

        var events = await StreamAsync(scope.ServiceProvider, await LoadAsync(scope.ServiceProvider, knowledgeCrew));

        Assert.True(events[^1].Output!.Succeeded, events[^1].Output!.Error);
        Assert.Contains("[1] Refund policy: a full refund within 30 days of purchase.", Prompt(vendor.ChatStreamingMessages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_streamed_crew_that_plans_reads_each_tasks_plan_in_its_prompt_without_a_warning()
    {
        var vendor = Vendor();
        vendor.SetGenerateResult(new LlmResponse
        {
            Content = """{"plans":[{"task":1,"plan":"1. PLAN for the draft"},{"task":2,"plan":"1. PLAN for the review"}]}""",
        });
        using var logs = new RecordingLoggerProvider();
        await using var container = Host(vendor, logs);
        await using var scope = container.CreateAsyncScope();
        var crew = await LoadAsync(scope.ServiceProvider, Launch().Replace("process: sequential", "process: sequential\nplanning: true", StringComparison.Ordinal));

        var events = await StreamAsync(scope.ServiceProvider, crew);

        Assert.True(events[^1].Output!.Succeeded, events[^1].Output!.Error);
        Assert.Equal(1, vendor.GenerateCallCount);
        var calls = vendor.ChatStreamingMessages;
        Assert.Contains("PLAN for the draft", Prompt(calls[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("PLAN for the review", Prompt(calls[0]), StringComparison.Ordinal);
        Assert.Contains("PLAN for the review", Prompt(calls[1]), StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("streamed run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tool_call_streams_its_name_between_its_tasks_start_and_end_and_never_its_arguments()
    {
        const string toolCrew = """
            name: lookup-desk
            goal: Look things up
            process: sequential
            agents:
              researcher:
                role: Researcher
                goal: Look things up
                tools: [lookup]
            tasks:
              find:
                description: Find the release date
                expected_output: The date
                agent: researcher
            """;
        var tool = new RecordingTool("lookup", "2026-10-02");
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true };
        vendor.SetChatStreamingFunc((messages, _) => messages.Any(m => m.Role == "tool")
            ? new(["Released ", "on 2026-10-02."], Final("Released on 2026-10-02."))
            : new([], new LlmResponse
            {
                Content = string.Empty,
                RawResponseBody = """{"choices":[{"message":{"role":"assistant","content":"","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{\"secret\":\"hunter2\"}"}}]}}]}""",
            }));
        await using var container = Host(vendor, configure: services => services.AddSingleton<IBaseTool>(tool));
        await using var scope = container.CreateAsyncScope();

        var events = await StreamAsync(scope.ServiceProvider, await LoadAsync(scope.ServiceProvider, toolCrew));

        // A streamed turn that asks for a tool runs it: the streaming path keeps the tools.
        Assert.Equal(1, tool.Calls);
        Assert.Equal(
            [RunEventKinds.TaskStarted, RunEventKinds.ToolCalled, RunEventKinds.ToolReturned,
             RunEventKinds.LlmDelta, RunEventKinds.LlmDelta, RunEventKinds.TaskCompleted, RunEventKinds.RunFinished],
            events.Select(e => e.Kind));
        Assert.Equal("lookup", events[1].ToolName);
        Assert.Equal("lookup", events[2].ToolName);
        Assert.True(events[2].Success);
        Assert.Equal("Researcher", events[1].AgentRole);
        Assert.DoesNotContain(events, e =>
            $"{e.Text}{e.Message}{e.ToolName}".Contains("hunter2", StringComparison.Ordinal));
        Assert.Equal("Released on 2026-10-02.", events[^1].Output!.FinalOutput);
    }

    [Fact]
    public async Task Leaving_the_stream_cancels_the_run_which_ends_failed_by_cancellation_with_its_task_cancelled()
    {
        // The first turn waits until it is cancelled: the consumer leaves while the task runs.
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true };
        vendor.SetChatStreamingHandler(WaitForCancellation);
        var recorder = new EventRecorder();
        await using var container = Host(vendor, configure: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IDomainEventHandler<CrewExecutionFailedEvent>, RecordingHandler>();
            services.AddScoped<IDomainEventHandler<CrewExecutionCompletedEvent>, RecordingHandler>();
        });
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var crew = await LoadAsync(sp, Launch());

        await foreach (var executionEvent in sp.GetRequiredService<ICrewOrchestrationService>()
            .KickoffStreamingAsync(crew.Id, CrewInput.Empty("launch"), Ct))
        {
            if (executionEvent.Kind == RunEventKinds.TaskStarted)
                break;
        }

        // The iterator waited for the run's end: nothing more was asked of the model, the review
        // never started, the draft was cancelled and the crew failed by cancellation.
        Assert.True(vendor.ChatStreamingCallCount <= 1);
        Assert.DoesNotContain(vendor.ChatStreamingMessages, messages => Asks(messages, "Review the launch notes"));
        var tasks = sp.GetRequiredService<ITaskRepository>();
        Assert.Equal(TaskStatus.Cancelled, (await tasks.GetByIdAsync(crew.Tasks[0], Ct))?.Status);
        Assert.Equal(TaskStatus.Pending, (await tasks.GetByIdAsync(crew.Tasks[1], Ct))?.Status);
        Assert.Equal(CrewStatus.Failed, crew.Status);
        Assert.Equal(ExecutionStatus.Failed, Assert.Single(crew.Executions).Status);
        var failed = Assert.IsType<CrewExecutionFailedEvent>(Assert.Single(recorder.Events));
        Assert.IsType<OperationCanceledException>(failed.Exception, exactMatch: false);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> WaitForCancellation(
        LlmMessage[] messages, LlmConfig? config, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield return LlmStreamEvent.Complete(Final("never"));
    }

    [Fact]
    public async Task Leaving_the_stream_while_the_model_writes_cancels_the_task_it_was_answering()
    {
        // The first turn writes a word, then waits until it is cancelled: the consumer leaves on that
        // word, while the model is answering. The agent loop hands the cancelled call back as a failed
        // result; the run records the task cancelled, not failed.
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true };
        vendor.SetChatStreamingHandler(WriteThenWaitForCancellation);
        var recorder = new EventRecorder();
        await using var container = Host(vendor, configure: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IDomainEventHandler<CrewExecutionFailedEvent>, RecordingHandler>();
            services.AddScoped<IDomainEventHandler<CrewExecutionCompletedEvent>, RecordingHandler>();
        });
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var crew = await LoadAsync(sp, Launch());

        await foreach (var executionEvent in sp.GetRequiredService<ICrewOrchestrationService>()
            .KickoffStreamingAsync(crew.Id, CrewInput.Empty("launch"), Ct))
        {
            if (executionEvent.Kind == RunEventKinds.LlmDelta)
                break;
        }

        Assert.Equal(1, vendor.ChatStreamingCallCount);
        var tasks = sp.GetRequiredService<ITaskRepository>();
        Assert.Equal(TaskStatus.Cancelled, (await tasks.GetByIdAsync(crew.Tasks[0], Ct))?.Status);
        Assert.Equal(TaskStatus.Pending, (await tasks.GetByIdAsync(crew.Tasks[1], Ct))?.Status);
        Assert.Equal(CrewStatus.Failed, crew.Status);
        var failed = Assert.IsType<CrewExecutionFailedEvent>(Assert.Single(recorder.Events));
        Assert.IsType<OperationCanceledException>(failed.Exception, exactMatch: false);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> WriteThenWaitForCancellation(
        LlmMessage[] messages, LlmConfig? config, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return LlmStreamEvent.Content("Draft ");
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield return LlmStreamEvent.Complete(Final("never"));
    }

    [Fact]
    public async Task A_crew_run_by_a_tool_of_a_streamed_task_writes_nothing_into_the_stream()
    {
        const string outerCrew = """
            name: outer-desk
            goal: Delegate the inner work
            process: sequential
            agents:
              lead:
                role: Lead
                goal: Delegate
                tools: [run_inner]
            tasks:
              delegate:
                description: Run the inner crew
                expected_output: Its answer
                agent: lead
            """;
        const string innerCrew = """
            name: inner-desk
            goal: Do the inner work
            process: sequential
            agents:
              worker:
                role: Worker
                goal: Work
            tasks:
              work:
                description: Do the inner work
                expected_output: The work
                agent: worker
            """;
        var tool = new RecordingTool("run_inner", "unused");
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true };
        vendor.SetChatStreamingFunc((messages, _) => messages.Any(m => m.Role == "tool")
            ? new(["Inner ", "done."], Final("Inner done."))
            : new([], new LlmResponse
            {
                Content = string.Empty,
                RawResponseBody = """{"choices":[{"message":{"role":"assistant","content":"","tool_calls":[{"id":"call_1","type":"function","function":{"name":"run_inner","arguments":"{}"}}]}}]}""",
            }));
        vendor.SetChatFunc((_, _) => Final("The inner work, done."));
        await using var container = Host(vendor, configure: services => services.AddSingleton<IBaseTool>(tool));
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var outer = await LoadAsync(sp, outerCrew);
        var inner = await LoadAsync(sp, innerCrew);
        CrewOutput? innerOutput = null;
        tool.OnCall = async ct =>
        {
            innerOutput = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(inner.Id, CrewInput.Empty("inner"), ct);
            return innerOutput.FinalOutput;
        };

        var events = await StreamAsync(sp, outer);

        Assert.NotNull(innerOutput);
        Assert.True(innerOutput.Succeeded, innerOutput.Error);
        Assert.DoesNotContain(events, e => e.TaskId == inner.Tasks[0].ToString());
        Assert.Single(events, e => e.Kind == RunEventKinds.TaskStarted);
        Assert.Single(events, e => e.Kind == RunEventKinds.RunFinished);
        // The inner turn was not streamed: the stream it would have written to is not its own.
        Assert.Equal(1, vendor.ChatCallCount);
        Assert.Equal(2, vendor.ChatStreamingCallCount);
    }

    [Fact]
    public async Task A_parallel_crews_tasks_stream_at_once_each_in_its_order_and_the_run_finishes_last()
    {
        // A rendezvous, not a clock: each task's turn waits until both have asked, so the two run at
        // once and their events interleave.
        var arrived = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vendor = new MockStreamingLlmProvider { SupportsStreaming = true };
        vendor.SetChatStreamingHandler((messages, _, ct) => Rendezvous(messages, ct));
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var crew = await LoadAsync(scope.ServiceProvider, Launch("parallel", reviewDependsOnDraft: false));

        var events = await StreamAsync(scope.ServiceProvider, crew);

        Assert.Equal(RunEventKinds.RunFinished, events[^1].Kind);
        Assert.True(events[^1].Output!.Succeeded, events[^1].Output!.Error);
        foreach (var taskId in crew.Tasks.Select(t => t.ToString()))
        {
            var own = events.Select((e, index) => (e, index)).Where(x => x.e.TaskId == taskId).ToList();
            Assert.Equal(RunEventKinds.TaskStarted, own[0].e.Kind);
            Assert.Equal(RunEventKinds.TaskCompleted, own[^1].e.Kind);
            Assert.All(own.Skip(1).SkipLast(1), x => Assert.Equal(RunEventKinds.LlmDelta, x.e.Kind));
            Assert.Equal(2, own.Count(x => x.e.Kind == RunEventKinds.LlmDelta));
        }

        var firstEnd = events.FindIndex(e => e.Kind == RunEventKinds.TaskCompleted);
        Assert.Equal(2, events.Take(firstEnd).Count(e => e.Kind == RunEventKinds.TaskStarted));

        async IAsyncEnumerable<LlmStreamEvent> Rendezvous(LlmMessage[] messages, [EnumeratorCancellation] CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrived) == 2)
                both.TrySetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            var turn = Asks(messages, "Review the launch notes")
                ? new MockStreamingLlmProvider.StreamedTurn(["Reviewed", "."], Final("Reviewed."))
                : new MockStreamingLlmProvider.StreamedTurn(["Drafted", "."], Final("Drafted."));
            foreach (var chunk in turn.Chunks)
            {
                yield return LlmStreamEvent.Content(chunk);
                await Task.Yield();
            }

            yield return LlmStreamEvent.Complete(turn.Final);
        }
    }

    private static float[] BagOfWords(string text)
    {
        var vector = new float[256];
        foreach (var word in text.ToLowerInvariant().Split([' ', '.', ',', ':', '\n', '\r', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            var slot = 0;
            foreach (var c in word)
                slot = (slot * 31 + c) & 0xFF;
            vector[slot] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        return norm > 0 ? [.. vector.Select(v => v / norm)] : vector;
    }

    /// <summary>A knowledge base that always retrieves the same excerpt.</summary>
    private sealed class FixedKnowledge(string block) : IKnowledgeContextAugmenter
    {
        public Task<KnowledgeContextBlock?> BuildContextAsync(
            IReadOnlyList<KnowledgeAttachment> attachments, string taskInput, CancellationToken cancellationToken = default) =>
            Task.FromResult<KnowledgeContextBlock?>(new KnowledgeContextBlock { Text = block });
    }

    /// <summary>A tool that counts its calls and answers, or runs what the test gives it.</summary>
    private sealed class RecordingTool(string name, string result) : IBaseTool
    {
        private int _calls;

        public Func<CancellationToken, Task<string>>? OnCall { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public string Name { get; } = name;

        public string Description => Name;

        public ToolSchema Schema => new(Name, Name, new Dictionary<string, ParameterSchema>());

        public async Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var answer = OnCall is null ? result : await OnCall(cancellationToken);
            return new ToolCallResponse(true, answer, null);
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool ValidateInput(string input) => true;
    }

    private sealed class EventRecorder
    {
        private readonly ConcurrentQueue<DomainEvent> _events = new();

        public IReadOnlyList<DomainEvent> Events => [.. _events];

        public IReadOnlyList<string> Names => [.. _events.Select(e => e.GetType().Name)];

        public void Record(DomainEvent domainEvent) => _events.Enqueue(domainEvent);
    }

    private sealed class RecordingHandler(EventRecorder recorder) :
        IDomainEventHandler<TaskStartedEvent>,
        IDomainEventHandler<TaskCompletedEvent>,
        IDomainEventHandler<CrewExecutionStartedEvent>,
        IDomainEventHandler<CrewExecutionCompletedEvent>,
        IDomainEventHandler<CrewExecutionFailedEvent>
    {
        public Task HandleAsync(TaskStartedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(TaskCompletedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(CrewExecutionStartedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(CrewExecutionCompletedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(CrewExecutionFailedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);

        private Task Record(DomainEvent domainEvent)
        {
            recorder.Record(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed record LogLine(string Category, LogLevel Level, string Message);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogLine> _entries = new();

        public IReadOnlyList<LogLine> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<LogLine> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogLine(category, logLevel, formatter(state, exception)));
        }
    }
}
