using System.Collections.Concurrent;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAgentExecutionService"/>: every task answers <c>out:&lt;task key&gt;</c>, the
/// calls are counted per task key and what each task read of the earlier outputs is kept, and the
/// tasks named in <see cref="FailOn"/> fail. A runner test drives a real crew, in any process mode,
/// and reads which tasks reached the "model".
/// </summary>
public sealed class FakeAgentExecutionService : IAgentExecutionService
{
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly ConcurrentDictionary<string, string> _previousOutputs = new();

    /// <summary>The keys of the tasks that fail while they are in this set.</summary>
    public HashSet<string> FailOn { get; } = [];

    /// <summary>The tasks that fail their next calls: the key and how many calls still fail.</summary>
    public ConcurrentDictionary<string, int> FailTimes { get; } = new();

    /// <summary>The tasks that fail on one numbered call (1 is the first): the key and the call number.</summary>
    public ConcurrentDictionary<string, int> FailOnCall { get; } = new();

    private readonly ConcurrentDictionary<string, int> _callNumbers = new();

    /// <summary>The key of every task run, in call order.</summary>
    public IReadOnlyCollection<string> Calls => [.. _calls];

    /// <summary>What the task of this key read of the earlier outputs, joined with a pipe.</summary>
    public string? PreviousOutputsOf(string key) => _previousOutputs.TryGetValue(key, out var seen) ? seen : null;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var key = task.Key ?? task.TaskId.ToString();
        _calls.Enqueue(key);
        var callNumber = _callNumbers.AddOrUpdate(key, 1, static (_, n) => n + 1);
        _previousOutputs[key] = string.Join('|', context.PreviousOutputs.Select(o => o.Content));
        return System.Threading.Tasks.Task.FromResult(FailOn.Contains(key) || TakeFailure(key) || (FailOnCall.TryGetValue(key, out var failing) && failing == callNumber)
            ? new TaskResult(false, "", null, [], TimeSpan.Zero, $"{key} failed")
            : new TaskResult(true, $"out:{key}", null, [], TimeSpan.Zero));
    }

    private bool TakeFailure(string key)
    {
        lock (FailTimes)
        {
            if (!FailTimes.TryGetValue(key, out var left) || left <= 0)
                return false;
            FailTimes[key] = left - 1;
            return true;
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
