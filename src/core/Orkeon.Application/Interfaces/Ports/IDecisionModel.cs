using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// A decision model (category <c>Decision</c>, a "System One" model): answers typed questions
/// about a state with calibrated probabilities and a confidence, and generates no text. It
/// complements an <c>ILlmProvider</c>, never replaces one: the model judges, the code acts.
/// </summary>
public interface IDecisionModel
{
    /// <summary>Gets the provider key (<c>DecisionProviderKeys</c>).</summary>
    string Name { get; }

    /// <summary>Answers every question of <paramref name="request"/> about its state.</summary>
    /// <exception cref="Decision.DecisionModelException">The provider refused or failed the request.</exception>
    Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the models and aliases the configured account may use.</summary>
    /// <exception cref="Decision.DecisionModelException">The provider refused or failed the request.</exception>
    Task<ImmutableList<DecisionModelDto>> ListModelsAsync(CancellationToken cancellationToken = default);
}
