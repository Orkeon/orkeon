using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Decision.Stages;

/// <summary>
/// <c>guard</c>: one yes-or-no question per risk, on the request, the answer, or both. A risk at or
/// over <see cref="BlockThreshold"/> blocks the call; one at or over <see cref="WarnThreshold"/> is
/// logged and the call goes on.
/// </summary>
public sealed partial class GuardDecisionStage : IDecisionStage
{
    private readonly IDecisionModel _model;
    private readonly ImmutableDictionary<string, string> _risks;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of <see cref="GuardDecisionStage"/>.</summary>
    /// <param name="model">The decision model.</param>
    /// <param name="risks">Each risk's name and what it is, phrased as the case to detect.</param>
    /// <param name="phase">When the guard judges.</param>
    /// <param name="blockThreshold">The probability at or over which a risk blocks the call.</param>
    /// <param name="warnThreshold">The probability at or over which a risk is logged.</param>
    /// <param name="logger">The logger.</param>
    public GuardDecisionStage(
        IDecisionModel model,
        IReadOnlyDictionary<string, string> risks,
        DecisionStagePhase phase,
        double blockThreshold,
        double warnThreshold,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(risks);
        ArgumentNullException.ThrowIfNull(logger);
        if (risks.Count == 0)
            throw new ArgumentException("A guard stage needs at least one risk.", nameof(risks));
        _model = model;
        _risks = risks.ToImmutableDictionary();
        Phase = phase;
        BlockThreshold = blockThreshold;
        WarnThreshold = warnThreshold;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "guard";

    /// <summary>When the guard judges.</summary>
    public DecisionStagePhase Phase { get; }

    /// <summary>The probability at or over which a risk blocks the call.</summary>
    public double BlockThreshold { get; }

    /// <summary>The probability at or over which a risk is logged.</summary>
    public double WarnThreshold { get; }

    /// <summary>
    /// The probability of each risk on <paramref name="state"/>, highest first. Shared with the
    /// agent guard, which asks the same questions.
    /// </summary>
    /// <param name="model">The decision model.</param>
    /// <param name="risks">Each risk's name and description.</param>
    /// <param name="state">What is judged.</param>
    /// <param name="cancellationToken">Cancels the decision.</param>
    /// <returns>Each risk and its probability, highest first.</returns>
    public static async Task<IReadOnlyList<(string Risk, double Probability)>> AssessAsync(
        IDecisionModel model,
        IReadOnlyDictionary<string, string> risks,
        JsonNode state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(risks);
        var response = await model.DecideAsync(new DecisionRequest
        {
            State = state,
            Questions = risks.ToImmutableDictionary(
                r => r.Key,
                r => (DecisionQuestion)new NoulQuestion { Criteria = new NoulCriteria { True = JsonValue.Create(r.Value) } }),
        }, cancellationToken).ConfigureAwait(false);
        return [.. risks.Keys
            .Select(risk => (risk, response.Get<NoulAnswer>(risk).Noul))
            .OrderByDescending(r => r.Item2)];
    }

    /// <inheritdoc />
    public Task<DecisionVerdict> OnRequestAsync(LlmMessage[] messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return Phase == DecisionStagePhase.Response
            ? Task.FromResult(DecisionVerdict.Continue)
            : JudgeAsync(DecisionStageState.OfRequest(messages), "request", cancellationToken);
    }

    /// <inheritdoc />
    public Task<DecisionVerdict> OnResponseAsync(LlmMessage[] messages, LlmResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(response);
        return Phase == DecisionStagePhase.Request
            ? Task.FromResult(DecisionVerdict.Continue)
            : JudgeAsync(DecisionStageState.OfExchange(messages, response), "response", cancellationToken);
    }

    private async Task<DecisionVerdict> JudgeAsync(JsonNode state, string what, CancellationToken cancellationToken)
    {
        var assessed = await AssessAsync(_model, _risks, state, cancellationToken).ConfigureAwait(false);
        var (risk, probability) = assessed[0];
        var shown = probability.ToString("0.00", CultureInfo.InvariantCulture);
        if (probability >= BlockThreshold)
            return DecisionVerdict.Block($"the {what} matches the risk '{risk}' at {shown}");
        if (probability >= WarnThreshold)
            LogRiskUnderBlock(what, risk, probability);
        return DecisionVerdict.Continue;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decision guard: the {What} matches the risk {Risk} at {Probability}, under the block threshold")]
    private partial void LogRiskUnderBlock(string what, string risk, double probability);
}
