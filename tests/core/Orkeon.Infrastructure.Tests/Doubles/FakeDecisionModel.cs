using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

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

    /// <summary>When set, answers instead of <see cref="Respond"/> and receives the cancellation token.</summary>
    public Func<DecisionRequest, CancellationToken, Task<DecisionResponse>>? RespondAsync { get; set; }

    /// <summary>Thrown by <see cref="DecideAsync"/> when set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>The models <see cref="ListModelsAsync"/> returns.</summary>
    public ImmutableList<DecisionModelDto> Models { get; set; } = [];

    /// <summary>The requests received so far, oldest first.</summary>
    public IReadOnlyList<DecisionRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    /// <inheritdoc />
    public string Name { get; set; } = "fake";

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _requests.Add(request);
        if (RespondAsync is not null)
            return RespondAsync(request, cancellationToken);
        return Failure is null ? Task.FromResult(Respond(request)) : Task.FromException<DecisionResponse>(Failure);
    }

    /// <inheritdoc />
    public Task<ImmutableList<DecisionModelDto>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Models);
}
