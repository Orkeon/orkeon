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
public class OrchestrationModesOfflineTests
{
    private static string CrewYaml(string process, int tasks = 2)
    {
        var taskBlocks = string.Join("\n", Enumerable.Range(1, tasks).Select(i => $"""
  step{i}:
    description: Produce part {i} of the analysis
    expected_output: Part {i} of the analysis
    agent: worker{(i % 2) + 1}
"""));
        var managerLine = process == "hierarchical" ? "\nmanagerAgent: worker1" : "";
        return $"""
name: {process}-crew
goal: Exercise the {process} orchestration mode offline
process: "{process}"{managerLine}
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

    private static async Task<(CrewOutput Output, StubLlmProvider Stub, StubChatClient Chat, RecordingExecutionHook Hook)> RunAsync(
        string yaml, CancellationToken ct)
    {
        var hook = new RecordingExecutionHook();
        var stub = new StubLlmProvider().RespondTo(prompt => new LlmResponse
        {
            Content = $"[offline] answer to: {prompt[..Math.Min(40, prompt.Length)]}"
        });
        stub.RespondToChatWith(new LlmResponse { Content = "[offline] chat answer" });

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        // Host-supplied LLM registrations win over the TryAdd fallbacks. The chat
        // client's lifetime is owned by the container (disposed with the provider).
        using var chatClient = new StubChatClient();
        services.AddSingleton<ICrewExecutionHook>(hook);
        services.AddSingleton<ILlmProvider>(stub);
        services.AddSingleton<IChatClient>(chatClient);
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
    public async Task Kickoff_DrivesTheLlm(string mode)
    {
        var (_, stub, chat, _) = await RunAsync(CrewYaml(mode), TestContext.Current.CancellationToken);

        var llmCalls = stub.GenerateCalls.Count + stub.ChatCalls.Count + chat.CallCount;
        Assert.True(llmCalls > 0,
            $"{mode}: the crew completed without a single LLM exchange — the mode ran a no-op path.");
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

    /// <summary>Offline IChatClient so no TryAdd fallback wires a real client.</summary>
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

    /// <summary>Records what the orchestration strategies notify, for the test above.</summary>
    internal sealed class RecordingExecutionHook : ICrewExecutionHook
    {
        private readonly Lock _gate = new();
        private readonly List<string> _tasks = [];

        public IReadOnlyList<string> CompletedTasks
        {
            get { lock (_gate) { return [.. _tasks]; } }
        }

        public int CrewCompletions { get; private set; }

        public int CrewFailures { get; private set; }

        public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
        {
            lock (_gate) { _tasks.Add(snapshot.TaskId); }
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
        private int _callCount;

        public int CallCount => _callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
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
