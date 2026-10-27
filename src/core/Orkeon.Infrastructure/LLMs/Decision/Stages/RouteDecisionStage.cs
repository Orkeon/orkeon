using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Decision.Stages;

/// <summary>
/// <c>route</c>: one choice question on the request, each option a profile and its description.
/// The chosen profile answers; under <see cref="MinConfidence"/> the fallback does, or the target
/// when there is none.
/// </summary>
public sealed class RouteDecisionStage : IDecisionStage
{
    /// <summary>The question's name in the request.</summary>
    public const string Question = "route";

    private readonly IDecisionModel _model;
    private readonly ImmutableDictionary<string, string> _routes;

    /// <summary>Initializes a new instance of <see cref="RouteDecisionStage"/>.</summary>
    /// <param name="model">The decision model.</param>
    /// <param name="routes">Each profile a request may go to, with what it is for.</param>
    /// <param name="minConfidence">The confidence under which the choice is not followed.</param>
    /// <param name="fallback">The profile taken under that confidence; null keeps the target.</param>
    public RouteDecisionStage(IDecisionModel model, IReadOnlyDictionary<string, string> routes, double minConfidence = 0, string? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(routes);
        if (routes.Count == 0)
            throw new ArgumentException("A route stage needs at least one route.", nameof(routes));
        _model = model;
        _routes = routes.ToImmutableDictionary();
        MinConfidence = minConfidence;
        Fallback = string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }

    /// <inheritdoc />
    public string Name => Question;

    /// <summary>The confidence under which the choice is not followed.</summary>
    public double MinConfidence { get; }

    /// <summary>The profile taken under <see cref="MinConfidence"/>; null keeps the target.</summary>
    public string? Fallback { get; }

    /// <inheritdoc />
    public async Task<DecisionVerdict> OnRequestAsync(LlmMessage[] messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var response = await _model.DecideAsync(new DecisionRequest
        {
            State = DecisionStageState.OfRequest(messages),
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add(Question, new ChoiceQuestion
            {
                Criteria = _routes.ToImmutableDictionary(r => r.Key, r => (JsonNode?)JsonValue.Create(r.Value)),
            }),
        }, cancellationToken).ConfigureAwait(false);

        var answer = response.Get<ChoiceAnswer>(Question);
        var confidence = answer.Confidence.ToString("0.00", CultureInfo.InvariantCulture);
        if (answer.Confidence >= MinConfidence && _routes.ContainsKey(answer.Choice))
            return DecisionVerdict.Route(answer.Choice, $"chose '{answer.Choice}' at {confidence}");
        return Fallback is null
            ? DecisionVerdict.Continue
            : DecisionVerdict.Route(Fallback, $"'{answer.Choice}' at {confidence} is under {MinConfidence.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <inheritdoc />
    public Task<DecisionVerdict> OnResponseAsync(LlmMessage[] messages, LlmResponse response, CancellationToken cancellationToken = default) =>
        Task.FromResult(DecisionVerdict.Continue);
}
