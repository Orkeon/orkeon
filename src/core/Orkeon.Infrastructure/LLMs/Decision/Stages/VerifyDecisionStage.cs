using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Decision.Stages;

/// <summary>
/// <c>verify</c>: one yes-or-no question on the answer — by default, does it address the request,
/// coherently and without invention. Under <see cref="MinProbability"/> the conversation is
/// escalated to a stronger profile: the cascade, a cheap model first and the strong one only when
/// the answer does not hold.
/// </summary>
public sealed class VerifyDecisionStage : IDecisionStage
{
    /// <summary>The question's name in the request.</summary>
    public const string Question = "verified";

    /// <summary>The criterion asked when the configuration names none.</summary>
    public const string DefaultCriterion =
        "The response addresses the request, is coherent, and states nothing the request does not support.";

    private readonly IDecisionModel _model;
    private readonly string _criterion;

    /// <summary>Initializes a new instance of <see cref="VerifyDecisionStage"/>.</summary>
    /// <param name="model">The decision model.</param>
    /// <param name="escalateTo">The profile an answer that does not hold is sent to.</param>
    /// <param name="minProbability">The probability under which the answer does not hold.</param>
    /// <param name="criterion">What a good answer is; null takes <see cref="DefaultCriterion"/>.</param>
    public VerifyDecisionStage(IDecisionModel model, string escalateTo, double minProbability = 0.5, string? criterion = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(escalateTo);
        _model = model;
        EscalateTo = escalateTo.Trim();
        MinProbability = minProbability;
        _criterion = string.IsNullOrWhiteSpace(criterion) ? DefaultCriterion : criterion;
    }

    /// <inheritdoc />
    public string Name => "verify";

    /// <summary>The profile an answer that does not hold is sent to.</summary>
    public string EscalateTo { get; }

    /// <summary>The probability under which the answer does not hold.</summary>
    public double MinProbability { get; }

    /// <inheritdoc />
    public Task<DecisionVerdict> OnRequestAsync(LlmMessage[] messages, CancellationToken cancellationToken = default) =>
        Task.FromResult(DecisionVerdict.Continue);

    /// <inheritdoc />
    public async Task<DecisionVerdict> OnResponseAsync(LlmMessage[] messages, LlmResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(response);
        var decision = await _model.DecideAsync(new DecisionRequest
        {
            State = DecisionStageState.OfExchange(messages, response),
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add(Question, new NoulQuestion
            {
                Criteria = new NoulCriteria { True = JsonValue.Create(_criterion) },
            }),
        }, cancellationToken).ConfigureAwait(false);

        var probability = decision.Get<NoulAnswer>(Question).Noul;
        return probability >= MinProbability
            ? DecisionVerdict.Continue
            : DecisionVerdict.Escalate(
                EscalateTo,
                $"the answer holds at {probability.ToString("0.00", CultureInfo.InvariantCulture)}, under {MinProbability.ToString(CultureInfo.InvariantCulture)}");
    }
}
