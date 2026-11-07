using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAgentExecutionService"/> answering through both overloads: the untyped
/// one with <c>out:&lt;key&gt;</c>, the typed one with <see cref="Structured"/>'s answer. It counts the
/// calls and throws for the keys in <see cref="ThrowOn"/>, or reports a failure for those in
/// <see cref="FailOn"/>.
/// </summary>
public sealed class FakeTypedAgentExecutionService : IAgentExecutionService
{
    private int _calls;

    /// <summary>How many calls reached the service.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>The keys whose execution throws.</summary>
    public HashSet<string> ThrowOn { get; } = [];

    /// <summary>The keys whose execution returns an unsuccessful result.</summary>
    public HashSet<string> FailOn { get; } = [];

    /// <summary>The structured answer of a typed call, by task key.</summary>
    public Func<string, object> Structured { get; set; } = key => new Answer(key);

    /// <summary>The structured output of an untyped call, by task key; null: none.</summary>
    public Func<string, object?> UntypedStructured { get; set; } = _ => null;

    /// <summary>A structured answer.</summary>
    public sealed record Answer(string Text);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        var key = task.Key!;
        if (ThrowOn.Contains(key))
            throw new InvalidOperationException($"{key} blew up");
        return System.Threading.Tasks.Task.FromResult(FailOn.Contains(key)
            ? new TaskResult(false, "", null, [], TimeSpan.Zero, $"{key} failed")
            : new TaskResult(true, $"out:{key}", UntypedStructured(key), [], TimeSpan.FromSeconds(2)) { TokensUsed = 42 });
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class
    {
        Interlocked.Increment(ref _calls);
        var key = task.Key!;
        if (ThrowOn.Contains(key))
            throw new InvalidOperationException($"{key} blew up");
        return System.Threading.Tasks.Task.FromResult(
            new TaskResult<TOutput>(true, $"raw:{key}", Structured(key) as TOutput, [], TimeSpan.Zero)
            {
                TokensUsed = 7,
                PromptTokens = 4,
                CompletionTokens = 3,
                IterationsUsed = 2,
            });
    }
}
