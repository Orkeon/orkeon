using System.Collections.Immutable;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// A decision model whose answers the test sets: one answer per question type, a failure to
/// throw, or a wait that only cancellation ends. Every request is recorded.
/// </summary>
internal sealed class StubDecisionModel : IDecisionModel
{
    /// <summary>The answer given to every noul question.</summary>
    public NoulAnswer Noul = new() { Noul = 0.9 };

    /// <summary>The answer given to every choice question.</summary>
    public ChoiceAnswer Choice = new()
    {
        Choice = "delivery",
        Confidence = 0.9,
        Probabilities = ImmutableDictionary<string, double>.Empty.Add("delivery", 0.9).Add("billing", 0.05).Add("account", 0.05),
    };

    /// <summary>The answer given to every score question.</summary>
    public ScoreAnswer Score = new()
    {
        Score = 2,
        Confidence = 0.9,
        Probabilities = ImmutableDictionary<string, double>.Empty.Add("0", 0.05).Add("1", 0.05).Add("2", 0.9),
    };

    /// <summary>The answer given to every box question.</summary>
    public BoxAnswer Box = new() { Box = new BoxCoordinates { Top = 0.4, Bottom = 0.6, Left = 0.3, Right = 0.7 } };

    /// <summary>The input tokens every answer reports.</summary>
    public int? InputTokens = 300;

    /// <summary>Thrown instead of answering, when set.</summary>
    public Exception? Failure;

    /// <summary>When true, waits until the call is cancelled.</summary>
    public bool Hang;

    /// <summary>Every request received, in order.</summary>
    public readonly List<DecisionRequest> Requests = [];

    public string Name => "stub";

    public async Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Hang)
            await Task.Delay(Timeout.Infinite, cancellationToken);
        if (Failure is not null)
            throw Failure;

        var answers = request.Questions.ToImmutableDictionary(
            q => q.Key,
            q => q.Value switch
            {
                NoulQuestion => (DecisionAnswer)Noul,
                ChoiceQuestion => Choice,
                ScoreQuestion => Score,
                BoxQuestion => Box,
                _ => throw new DecisionModelException("unknown question type"),
            });
        return new DecisionResponse
        {
            Model = "stub-1",
            Answers = answers,
            Usage = new DecisionUsage { InputTokens = InputTokens },
        };
    }
}
