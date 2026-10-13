using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using AppTaskResult = Orkeon.Application.Interfaces.Services.TaskResult;

namespace Orkeon.Infrastructure.Tests.Tools;

/// <summary>
/// CH-01 - a delegator A delegating through the real <see cref="DelegateWorkTool"/> to a coworker B,
/// both run by the real <see cref="ExecutionOrchestrator"/> on a manual clock. The chat client is
/// scripted by call order: call 1 is A asking to delegate, call 2 is B (waits for its token), and
/// call 3 is A again, after B returned.
/// </summary>
public sealed class DelegateWorkToolTestsFixture : IDisposable
{
    public const string DelegatorRole = "Delegator";
    public const string CoworkerRole = "Coworker";

    private readonly DelegateWorkTool _tool;
    private readonly AgentId _coworkerId = AgentId.Create();
    private readonly DomainAgent _delegator;
    private readonly DomainAgent _coworker;
    private readonly ExecutionOrchestrator _orchestrator;
    private readonly RecordingExecutionService _executions = new();

    public DelegateWorkToolTestsFixture(TimeSpan? delegatorLimit, TimeSpan? coworkerLimit, bool delegatorAnswersAfterDelegation)
    {
        Clock = new ManualTimeProvider();
        Client = new ScriptedChatClient(delegatorAnswersAfterDelegation);

        var delegatorId = AgentId.Create();
        _tool = new DelegateWorkTool(
            new DelegateWorkTestAgentCommunicationService(),
            delegatorId,
            role => role == CoworkerRole ? _coworkerId : null,
            _executions,
            id => id.Equals(_coworkerId) ? _coworker : null,
            () => NewContext(),
            new TestLogger<DelegateWorkTool>());

        _delegator = Agent(DelegatorRole, delegatorLimit, _tool);
        _coworker = Agent(CoworkerRole, coworkerLimit, tool: null);

        _orchestrator = new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance,
            new StubBasicLlmProvider(),
            Client,
            [_tool],
            new FakeFileSystemService())
        {
            TimeProvider = Clock,
        };
        _executions.Orchestrator = _orchestrator;
    }

    public ManualTimeProvider Clock { get; }

    public ScriptedChatClient Client { get; }

    /// <summary>The result of the coworker's task, once it ended.</summary>
    public AppTaskResult? CoworkerResult => _executions.Results.GetValueOrDefault(CoworkerRole);

    /// <summary>Runs the delegator's task: it delegates to the coworker, waiting for its result.</summary>
    public System.Threading.Tasks.Task<AppTaskResult> RunDelegatorAsync(CancellationToken cancellationToken) =>
        _orchestrator.ExecuteTaskCoreAsync(_delegator, NewTask("Delegate the research"), NewContext(), cancellationToken);

    public void Dispose()
    {
        _tool.Dispose();
        Client.Dispose();
    }

    private static DomainAgent Agent(string role, TimeSpan? limit, DelegateWorkTool? tool)
    {
        var builder = new AgentBuilder().Role(role).Goal("Do the work");
        if (tool is not null)
            builder.WithTool(tool);
        if (limit is { } max)
            builder.MaxExecutionTime(max);
        return builder.Build();
    }

    private static DomainTask NewTask(string description) =>
        DomainTask.Create(TaskDescription.From(description), ExpectedOutput.From("An answer"));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "NullMemoryScope (no-op Dispose) is owned by the returned execution context, which lives for the duration of the test.")]
    private static SimpleExecutionContext NewContext() =>
        new(CrewId.Create(), new Dictionary<string, string>(), new NullMemoryScope(), Array.Empty<Orkeon.Application.Execution.TaskOutput>());

    /// <summary>Runs the real orchestrator and records each agent's result.</summary>
    private sealed class RecordingExecutionService : IAgentExecutionService
    {
        public ExecutionOrchestrator? Orchestrator { get; set; }

        public ConcurrentDictionary<string, AppTaskResult> Results { get; } = new();

        public async System.Threading.Tasks.Task<AppTaskResult> ExecuteTaskAsync(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        {
            var result = await Orchestrator!.ExecuteTaskCoreAsync(agent, (DomainTask)task, context, cancellationToken);
            Results[agent.Role.Value] = result;
            return result;
        }

        public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
            where TOutput : class =>
            throw new NotSupportedException();
    }

    public sealed class ScriptedChatClient(bool delegatorAnswersAfterDelegation) : IChatClient
    {
        private int _calls;

        /// <summary>Completed when the coworker's model call is waiting.</summary>
        public System.Threading.Tasks.TaskCompletionSource CoworkerEntered { get; } =
            new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        public async System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {            switch (Interlocked.Increment(ref _calls))
            {
                case 1:
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [
                        new FunctionCallContent("call-1", "delegate_work_to_coworker", new Dictionary<string, object?>
                        {
                            ["task"] = "Research the topic",
                            ["context"] = "No extra context",
                            ["coworker_role"] = CoworkerRole,
                            ["wait_for_result"] = true,
                        }),
                    ])]);
                case 2:
                    CoworkerEntered.TrySetResult();
                    await System.Threading.Tasks.Task.Delay(Timeout.Infinite, cancellationToken);
                    break;
                default:
                    if (!delegatorAnswersAfterDelegation)
                        await System.Threading.Tasks.Task.Delay(Timeout.Infinite, cancellationToken);
                    break;
            }

            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
