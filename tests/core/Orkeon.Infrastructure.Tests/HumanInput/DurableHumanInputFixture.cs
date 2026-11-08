using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>The pieces a durable human-input test stands on: a store, a run, a task that asks, a clock.</summary>
internal sealed class DurableHumanInputFixture : IDisposable
{
    private readonly List<IDisposable> _scopes = [];

    public DurableHumanInputFixture(HumanInputPause pause, TimeSpan? holdTimeout = null)
    {
        Store = new InMemoryStateStore();
        Manager = new CheckpointManager(Store, NullLogger<CheckpointManager>.Instance);
        Scope = new AsyncLocalRunScope();
        Broker = new PendingInputBroker();
        Clock = new ManualTimeProvider();
        Inner = new MockHumanInputProvider();
        Options = new HumanInputPauseOptions { Pause = pause, HoldTimeout = holdTimeout };
        Provider = new DurableHumanInputProvider(
            Inner, Scope, Manager, Broker, Microsoft.Extensions.Options.Options.Create(Options), Clock, NullLogger<DurableHumanInputProvider>.Instance);
    }

    public InMemoryStateStore Store { get; }

    public CheckpointManager Manager { get; }

    public AsyncLocalRunScope Scope { get; }

    public PendingInputBroker Broker { get; }

    public ManualTimeProvider Clock { get; }

    public MockHumanInputProvider Inner { get; }

    public HumanInputPauseOptions Options { get; }

    public DurableHumanInputProvider Provider { get; }

    public ExecutionId Id { get; } = ExecutionId.New();

    /// <summary>The notices the run's observers received.</summary>
    public List<HumanInputNotice> Notices { get; } = [];

    /// <summary>Completed on the first <see cref="HumanInputNoticeKind.Needed"/> notice.</summary>
    public TaskCompletionSource Needed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RunScope? Run { get; private set; }

    /// <summary>Enters the run (a fresh one, or the same session again as a resume) and the task <paramref name="taskKey"/>.</summary>
    public void EnterRun(string taskKey = "ask#1", bool startSession = true)
    {
        if (startSession)
            Manager.StartSessionAsync(Id, "crew", CancellationToken.None).GetAwaiter().GetResult();

        Run = new RunScope(Id, "crew")
        {
            HumanInputObserver = (notice, _) =>
            {
                lock (Notices)
                    Notices.Add(notice);
                if (notice.Kind == HumanInputNoticeKind.Needed)
                    Needed.TrySetResult();
                return System.Threading.Tasks.Task.CompletedTask;
            },
        };
        _scopes.Add(Scope.Enter(Run));
        EnterTask(taskKey);
    }

    public void EnterTask(string taskKey) => _scopes.Add(TaskInputScope.Enter(taskKey));

    /// <summary>Leaves the run and the task: what a process that ended does.</summary>
    public void LeaveRun()
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
            _scopes[i].Dispose();
        _scopes.Clear();
    }

    public static HumanInputContext Ask(string prompt, string inputType = "text", params string[] options) =>
        new() { Prompt = prompt, InputType = inputType, Options = options };

    public Task<SessionState?> StateAsync() => Store.GetAsync(Id.ToString(), CancellationToken.None);

    public void Dispose() => LeaveRun();
}
