using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>An <see cref="IDecisionModel"/> that answers from <see cref="Respond"/> and keeps the requests.</summary>
public sealed class FakeDecisionModel : IDecisionModel
{
    /// <summary>Builds the response to a request.</summary>
    public Func<DecisionRequest, DecisionResponse> Respond { get; set; } =
        _ => new DecisionResponse { Model = "fake", Answers = ImmutableDictionary<string, DecisionAnswer>.Empty };

    /// <summary>The requests received, oldest first.</summary>
    public List<DecisionRequest> Requests { get; } = [];

    /// <inheritdoc />
    public string Name => "fake";

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }

    /// <inheritdoc />
    public Task<ImmutableList<DecisionModelDto>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ImmutableList<DecisionModelDto>.Empty);
}
