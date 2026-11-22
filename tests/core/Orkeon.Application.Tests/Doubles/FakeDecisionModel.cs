using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// An <see cref="IDecisionModel"/> that answers from <see cref="Respond"/> and keeps every
/// request it receives, in order. <see cref="Failure"/>, when set, is thrown instead.
/// </summary>
public sealed class FakeDecisionModel : IDecisionModel
{
    private readonly Lock _gate = new();
    private readonly List<DecisionRequest> _requests = [];

    /// <summary>Builds the response to a request; the default answers nothing.</summary>
    public Func<DecisionRequest, DecisionResponse> Respond { get; set; } =
        _ => new DecisionResponse { Model = "fake", Answers = ImmutableDictionary<string, DecisionAnswer>.Empty };

    /// <summary>Thrown by <see cref="DecideAsync"/> when set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Run by <see cref="DecideAsync"/> once the request is kept, before it answers: cancels a caller's token, say.</summary>
    public Action<DecisionRequest>? OnDecide { get; set; }

    /// <summary>Awaited by <see cref="DecideAsync"/>, once the request is kept, before it answers: holds every call open until it completes.</summary>
    public System.Threading.Tasks.Task? Hold { get; set; }

    /// <summary>The requests received so far, oldest first.</summary>
    public IReadOnlyList<DecisionRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    /// <inheritdoc />
    public string Name { get; set; } = "fake";

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _requests.Add(request);
        OnDecide?.Invoke(request);
        if (Hold is { } hold)
            return AnswerOnceReleasedAsync(hold, request);
        return Failure is null ? System.Threading.Tasks.Task.FromResult(Respond(request)) : System.Threading.Tasks.Task.FromException<DecisionResponse>(Failure);
    }

    private async System.Threading.Tasks.Task<DecisionResponse> AnswerOnceReleasedAsync(System.Threading.Tasks.Task hold, DecisionRequest request)
    {
        await hold.ConfigureAwait(false);
        return Failure is null ? Respond(request) : throw Failure;
    }
}
