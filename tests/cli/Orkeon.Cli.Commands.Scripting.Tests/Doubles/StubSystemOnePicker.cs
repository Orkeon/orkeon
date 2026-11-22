using System.Collections.Concurrent;
using Orkeon.Application.Crew.Routing;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>An <see cref="ISystemOnePicker"/> that answers every pick with <see cref="Answer"/> and keeps the requests.</summary>
internal sealed class StubSystemOnePicker : ISystemOnePicker
{
    /// <summary>The answer given to every pick.</summary>
    public SystemOnePickResponse Answer { get; set; } = new()
    {
        Effort = new SystemOnePickAnswer { Value = "low", Reason = SystemOnePickReasons.Model, Picked = "low", Probability = 0.75 },
    };

    /// <summary>The requests received, oldest first.</summary>
    public ConcurrentQueue<SystemOnePickRequest> Requests { get; } = new();

    /// <summary>Run inside each pick with its token, before the answer (null: nothing); an exception it throws is the pick's.</summary>
    public Action<CancellationToken>? OnPick { get; set; }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<SystemOnePickResponse> PickAsync(SystemOnePickRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        OnPick?.Invoke(cancellationToken);
        return System.Threading.Tasks.Task.FromResult(Answer);
    }
}
