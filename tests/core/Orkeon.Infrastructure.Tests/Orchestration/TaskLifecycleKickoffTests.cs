using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// GAP-21 — through the host's own composition (<c>AddOrkeonApplication</c> +
/// <c>AddOrkeonInfrastructure</c>), a YAML crew's run reaches the <c>IDomainEventHandler&lt;T&gt;</c>
/// registrations task by task: the handlers a host registers, and the two that ship, which log. The
/// run used to raise the crew's events only, at its end.
/// </summary>
public sealed class TaskLifecycleKickoffTests
{
    private const string LaunchCrew = """
        name: launch-desk
        goal: Publish the launch notes
        process: sequential
        agents:
          writer:
            role: Writer
            goal: Write the launch notes
        tasks:
          draft:
            description: Draft the launch notes
            expected_output: The draft
            agent: writer
          review:
            description: Review the launch notes
            expected_output: The review
            agent: writer
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_reaches_the_registered_handlers_task_by_task_before_the_crews_own_events()
    {
        var recorder = new EventRecorder();
        using var logs = new RecordingLoggerProvider();
        await using var container = Host(recorder, logs);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(LaunchCrew, Ct);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("launch", new Dictionary<string, object>()), Ct);

        Assert.False(output.Succeeded);
        // GAP-32: a run with a failed task ends on CrewExecutionFailedEvent — it used to end on
        // CrewExecutionCompletedEvent (FailedTasks = 1), and the shipped failure handler saw nothing.
        Assert.Equal(
            [nameof(TaskStartedEvent), nameof(TaskCompletedEvent), nameof(AgentCompletedTaskEvent),
             nameof(TaskStartedEvent), nameof(TaskFailedEvent), nameof(AgentFailedTaskEvent),
             nameof(CrewExecutionFailedEvent)],
            recorder.Names);
        var failed = Assert.IsType<CrewExecutionFailedEvent>(recorder.Events[^1]);
        Assert.Contains(crew.Tasks[1].ToString(), failed.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(crew.Tasks[0].ToString(), failed.Reason, StringComparison.Ordinal);
        Assert.Equal(output.Error, failed.Reason);

        // The repository of the run's scope says what became of each task.
        var tasks = sp.GetRequiredService<ITaskRepository>();
        var draft = await tasks.GetByIdAsync(crew.Tasks[0], Ct);
        var review = await tasks.GetByIdAsync(crew.Tasks[1], Ct);
        Assert.Equal(TaskStatus.Completed, draft?.Status);
        Assert.NotNull(draft?.CompletedAt);
        Assert.Equal(TaskStatus.Failed, review?.Status);
        Assert.NotNull(review?.StartedAt);

        // The two shipped handlers log as the run goes.
        Assert.Contains(logs.Entries, e => e.Category.EndsWith(".AgentCompletedTaskHandler", StringComparison.Ordinal)
            && e.Message.Contains(crew.Tasks[0].ToString(), StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Category.EndsWith(".AgentFailedTaskHandler", StringComparison.Ordinal)
            && e.Level == LogLevel.Warning
            && e.Message.Contains(crew.Tasks[1].ToString(), StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Category.EndsWith(".CrewExecutionFailedHandler", StringComparison.Ordinal)
            && e.Level == LogLevel.Warning
            && e.Message.Contains(crew.Tasks[1].ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, e => e.Category.EndsWith(".CrewExecutionCompletedHandler", StringComparison.Ordinal));
    }

    private static ServiceProvider Host(EventRecorder recorder, RecordingLoggerProvider logs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => Vendor(), LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        services.AddSingleton(recorder);
        services.AddScoped<IDomainEventHandler<TaskStartedEvent>, RecordingHandler>();
        services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, RecordingHandler>();
        services.AddScoped<IDomainEventHandler<TaskFailedEvent>, RecordingHandler>();
        services.AddScoped<IDomainEventHandler<AgentCompletedTaskEvent>, RecordingHandler>();
        services.AddScoped<IDomainEventHandler<AgentFailedTaskEvent>, RecordingHandler>();
        services.AddScoped<IDomainEventHandler<CrewExecutionCompletedEvent>, RecordingHandler>();
        services.AddScoped<IDomainEventHandler<CrewExecutionFailedEvent>, RecordingHandler>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A model that drafts, and answers nothing to the review: that task fails.</summary>
    private static MockLlmProvider Vendor()
    {
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((messages, _) => new LlmResponse
        {
            Content = messages.Any(m => m.Content.Contains("Review the launch notes", StringComparison.Ordinal))
                ? string.Empty
                : "The launch notes, drafted.",
            PromptTokens = 3,
            CompletionTokens = 1,
            TokensUsed = 4,
        });
        return vendor;
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
        IDomainEventHandler<TaskFailedEvent>,
        IDomainEventHandler<AgentCompletedTaskEvent>,
        IDomainEventHandler<AgentFailedTaskEvent>,
        IDomainEventHandler<CrewExecutionCompletedEvent>,
        IDomainEventHandler<CrewExecutionFailedEvent>
    {
        public Task HandleAsync(TaskStartedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(TaskCompletedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(TaskFailedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(AgentCompletedTaskEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(AgentFailedTaskEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
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
