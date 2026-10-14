using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Telemetry;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Telemetry;
using Orkeon.Tests.Shared.Timing;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// A class that runs alone: its first fact reads that no activity exists while no listener hears the
/// Orkeon sources, and a listener another class attaches would be heard process-wide.
/// </summary>
[CollectionDefinition("RunTraceKickoff", DisableParallelization = true)]
public sealed class RunTraceKickoffCollection;

/// <summary>
/// CH-06 / CH-07 - through the host's own composition, a crew run is one <c>crew.run</c> span with one
/// <c>task.run</c> span per task, in error with its reason when a task or the run fails or is cancelled,
/// and carrying no task content.
/// </summary>
[Collection("RunTraceKickoff")]
public sealed class RunTraceKickoffTests
{
    private const string TwoTasks = """
        name: trace-desk
        goal: Publish the notes
        process: sequential
        agents:
          writer:
            role: Writer
            goal: Write the notes
        tasks:
          draft:
            description: Draft the notes
            expected_output: The draft
            agent: writer
          review:
            description: Review the notes
            expected_output: The review
            agent: writer
        """;

    private const string FailingChain = """
        name: trace-desk
        goal: Publish the notes
        process: sequential
        agents:
          writer:
            role: Writer
            goal: Write the notes
        tasks:
          draft:
            description: Draft the notes
            expected_output: The draft
            agent: writer
          review:
            description: Review the notes
            expected_output: The review
            agent: writer
          publish:
            description: Publish the notes
            expected_output: The publication
            agent: writer
            dependencies: [review]
        """;

    private const string ParallelCrew = """
        name: trace-wave
        goal: Publish the notes
        process: parallel
        agents:
          writer:
            role: Writer
            goal: Write the notes
          editor:
            role: Editor
            goal: Edit the notes
        tasks:
          alpha:
            description: Alpha job
            expected_output: The alpha
            agent: writer
          beta:
            description: Beta job
            expected_output: The beta
            agent: editor
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "CH-06")]
    public async Task ShouldCreateNoActivity_WhenRunHasNoListener()
    {
        var seen = new List<Activity?>();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) =>
        {
            lock (seen)
                seen.Add(Activity.Current);
            return Answer("The notes.");
        });
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();

        var (crew, output) = await RunAsync(scope.ServiceProvider, TwoTasks);

        Assert.True(output.Succeeded);
        Assert.NotEmpty(seen);
        Assert.All(seen, current => Assert.Null(current));
        Assert.NotEmpty(crew.Tasks);
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public async Task ShouldGiveSiblingCrewRuns_WhenKickoffForEachRunsTwoInputs()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) => Answer("The notes."));
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var crew = await CreateCrewAsync(sp, TwoTasks);

        var batch = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffForEachAsync(
            crew.Id,
            [new CrewInput("one", new Dictionary<string, object>()), new CrewInput("two", new Dictionary<string, object>())],
            Ct);

        Assert.Equal(2, batch.Results.Count);
        var spans = recorder.Snapshot(root.TraceId);
        var runs = spans.Where(s => s.OperationName == "crew.run").ToList();
        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(root.SpanId, run.ParentSpanId));
        Assert.All(runs, run => Assert.Equal(2, spans.Count(s => s.OperationName == "task.run" && s.ParentSpanId == run.SpanId)));
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldMarkTaskFailedAndCrewInError_WhenTaskFailsAndDependentIsSkipped()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((messages, _) => Answer(
            messages.Any(m => m.Content.Contains("Review the notes", StringComparison.Ordinal)) ? string.Empty : "The notes."));
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();

        var (crew, output) = await RunAsync(scope.ServiceProvider, FailingChain);

        Assert.False(output.Succeeded);
        var spans = recorder.Snapshot(root.TraceId);
        var run = Assert.Single(spans, s => s.OperationName == "crew.run");
        Assert.Equal(ActivityStatusCode.Error, run.Status);
        var draft = TaskSpan(spans, crew.Tasks[0].ToString());
        var review = TaskSpan(spans, crew.Tasks[1].ToString());
        var publish = TaskSpan(spans, crew.Tasks[2].ToString());
        Assert.NotEqual(ActivityStatusCode.Error, draft.Status);
        Assert.Equal(ActivityStatusCode.Error, review.Status);
        Assert.False(string.IsNullOrWhiteSpace(review.StatusDescription));
        Assert.Contains(review.StatusDescription!, output.Error, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(review.GetTagItem("error.type")?.ToString()));
        Assert.Equal("cancelled", publish.GetTagItem("orkeon.task.status")?.ToString());
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldMarkTaskCancelledAndCrewInError_WhenRunIsCancelledDuringTask()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var crew = await CreateCrewAsync(sp, TwoTasks);

        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("cancel", new Dictionary<string, object>()), cts.Token);

        Assert.False(output.Succeeded);
        var spans = recorder.Snapshot(root.TraceId);
        var run = Assert.Single(spans, s => s.OperationName == "crew.run");
        Assert.Equal(ActivityStatusCode.Error, run.Status);
        var draft = TaskSpan(spans, crew.Tasks[0].ToString());
        Assert.Equal("cancelled", draft.GetTagItem("orkeon.task.status")?.ToString());
        Assert.Equal(ActivityStatusCode.Error, draft.Status);
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public async Task ShouldEndShorterTaskFirst_WhenParallelWaveHoldsOneTask()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        var alphaId = string.Empty;
        DateTimeOffset alphaEnd = default;
        DateTimeOffset betaEnd = default;
        var alphaOnce = 0;
        var betaOnce = 0;
        // Beta moves the clock once alpha's task completion callback has fired: it follows the
        // execution's end reading, so alpha's end cannot catch beta's advance.
        var hook = new CompletionHook();
        bool AlphaCompleted() => hook.Completed(alphaId);
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((messages, _) =>
        {
            if (messages.Any(m => m.Content.Contains("Alpha job", StringComparison.Ordinal)))
            {
                if (Interlocked.Exchange(ref alphaOnce, 1) == 0)
                {
                    clock.Advance(TimeSpan.FromSeconds(1));
                    alphaEnd = clock.GetUtcNow();
                }
            }
            else if (Interlocked.Exchange(ref betaOnce, 1) == 0)
            {
                SpinWait.SpinUntil(AlphaCompleted, TimeSpan.FromSeconds(30));
                clock.Advance(TimeSpan.FromSeconds(4));
                betaEnd = clock.GetUtcNow();
            }

            return Answer("The notes.");
        });
        await using var container = Host(vendor, clock, hook);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var crew = await CreateCrewAsync(sp, ParallelCrew);
        alphaId = crew.Tasks[0].ToString();

        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("trace", new Dictionary<string, object>()), Ct);

        Assert.True(output.Succeeded);
        var spans = recorder.Snapshot(root.TraceId);
        var alpha = TaskSpan(spans, crew.Tasks[0].ToString());
        var beta = TaskSpan(spans, crew.Tasks[1].ToString());
        var alphaSpanEnd = new DateTimeOffset(alpha.StartTimeUtc + alpha.Duration, TimeSpan.Zero);
        var betaSpanEnd = new DateTimeOffset(beta.StartTimeUtc + beta.Duration, TimeSpan.Zero);
        Assert.True(alphaSpanEnd < betaSpanEnd);
        Assert.Equal(alphaEnd, alphaSpanEnd);
        Assert.Equal(betaEnd, betaSpanEnd);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldWriteNoTaskContent_WhenRunIsTraced()
    {
        const string Description = "Draft the zebra-quartz notes";
        const string Answer1 = "ember-742 is the answer text";
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) => Answer(Answer1));
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var yaml = TwoTasks.Replace("Draft the notes", Description, StringComparison.Ordinal);

        var (_, output) = await RunAsync(scope.ServiceProvider, yaml);

        Assert.True(output.Succeeded);
        var spans = recorder.Snapshot(root.TraceId);
        var traced = spans.Where(s => s.OperationName is "crew.run" or "task.run").ToList();
        Assert.Contains(traced, s => s.OperationName == "crew.run");
        Assert.Equal(2, traced.Count(s => s.OperationName == "task.run"));
        foreach (var span in traced)
        {
            var values = span.TagObjects.Select(t => t.Value?.ToString() ?? string.Empty)
                .Concat(span.Events.SelectMany(e => e.Tags.Select(t => t.Value?.ToString() ?? string.Empty)))
                .Concat(span.Events.Select(e => e.Name))
                .Append(span.StatusDescription ?? string.Empty);
            Assert.All(values, value =>
            {
                Assert.DoesNotContain("zebra-quartz", value, StringComparison.Ordinal);
                Assert.DoesNotContain("ember-742", value, StringComparison.Ordinal);
                Assert.DoesNotContain("Review the notes", value, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldTraceManagerCallsAsChatUnderCrewRun_WhenProcessIsHierarchical()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) => Answer("The notes."));
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var yaml = TwoTasks.Replace("process: sequential", "process: hierarchical\nmanagerAgent: lead", StringComparison.Ordinal)
            .Replace("agents:\n", "agents:\n  lead:\n    role: Lead\n    goal: Lead the team\n", StringComparison.Ordinal);

        var crew = await CreateCrewAsync(scope.ServiceProvider, yaml);
        await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("trace", new Dictionary<string, object>()), Ct);

        var spans = recorder.Snapshot(root.TraceId);
        var run = Assert.Single(spans, s => s.OperationName == "crew.run");
        var managerChats = spans.Where(s => s.OperationName.StartsWith("chat", StringComparison.Ordinal)
            && s.ParentSpanId == run.SpanId
            && s.GetTagItem("orkeon.llm.operation")?.ToString() == "manager").ToList();
        Assert.NotEmpty(managerChats);
        Assert.All(managerChats, chat => Assert.Equal(ActivityKind.Client, chat.Kind));
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldTracePlanningCallBeforeFirstTask_WhenPlanningIsOn()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetGenerateResult(new LlmResponse
        {
            Content = """{"plans":[{"task":1,"plan":"Write it"},{"task":2,"plan":"Check it"}]}""",
            PromptTokens = 3,
            CompletionTokens = 1,
            TokensUsed = 4,
        });
        vendor.SetChatFunc((_, _) => Answer("The notes."));
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();
        var yaml = TwoTasks.Replace("process: sequential", "process: sequential\nplanning: true", StringComparison.Ordinal);

        await RunAsync(scope.ServiceProvider, yaml);

        var spans = recorder.Snapshot(root.TraceId);
        var run = Assert.Single(spans, s => s.OperationName == "crew.run");
        var planning = Assert.Single(spans, s => s.OperationName.StartsWith("chat", StringComparison.Ordinal)
            && s.ParentSpanId == run.SpanId
            && s.GetTagItem("orkeon.llm.operation")?.ToString() == "planning");
        var firstTaskStart = spans.Where(s => s.OperationName == "task.run").Min(s => s.StartTimeUtc);
        Assert.True(planning.StartTimeUtc <= firstTaskStart);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldKeepOneChatPerAgentTurn_WhenAgentCallsTheModel()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = StartRoot();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) => Answer("The notes."));
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();

        var (_, output) = await RunAsync(scope.ServiceProvider, TwoTasks);

        Assert.True(output.Succeeded);
        var chats = recorder.Snapshot(root.TraceId)
            .Where(s => s.OperationName.StartsWith("chat", StringComparison.Ordinal)).ToList();
        Assert.Equal(vendor.ChatCallCount, chats.Count);
        Assert.All(chats, chat => Assert.DoesNotContain(chats, other => other.SpanId == chat.ParentSpanId));
    }

#pragma warning disable CA2000 // the test disposes the root it takes
    private static Activity StartRoot()
    {
        var root = new Activity("test.root");
        root.Start();
        return root;
    }

    private static Activity TaskSpan(IReadOnlyList<Activity> spans, string taskId) =>
        Assert.Single(spans, s => s.OperationName == "task.run" && s.GetTagItem("orkeon.task.id")?.ToString() == taskId);

    private static LlmResponse Answer(string content) => new()
    {
        Content = content,
        PromptTokens = 3,
        CompletionTokens = 1,
        TokensUsed = 4,
    };

    private static async Task<DomainCrew> CreateCrewAsync(IServiceProvider sp, string yaml)
    {
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        return await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
    }

    private static async Task<(DomainCrew Crew, CrewOutput Output)> RunAsync(IServiceProvider sp, string yaml)
    {
        var crew = await CreateCrewAsync(sp, yaml);
        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("trace", new Dictionary<string, object>()), Ct);
        return (crew, output);
    }

    private static ServiceProvider Host(MockLlmProvider vendor, TimeProvider? clock = null, ICrewExecutionHook? hook = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        if (clock is not null)
            services.AddSingleton(clock);
        if (hook is not null)
            services.AddSingleton(hook);
        services.AddOrkeonLlmProvider(_ => vendor, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class CompletionHook : ICrewExecutionHook
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _done = new();

        public bool Completed(string taskId) => _done.ContainsKey(taskId);

        public System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
        {
            _done[snapshot.TaskId] = true;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;
    }
}
