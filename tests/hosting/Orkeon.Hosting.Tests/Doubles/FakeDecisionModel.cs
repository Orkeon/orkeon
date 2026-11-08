using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// An <see cref="IDecisionModel"/> that answers each request, in turn, with the next choice of
/// <see cref="Choices"/> (the last one again once they run out) on every question, and counts the requests.
/// </summary>
public sealed class FakeDecisionModel : IDecisionModel
{
    private int _requests;

    /// <summary>The choices answered, one per request, in order.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>How many requests were received.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <inheritdoc />
    public string Name => "fake";

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        var index = Interlocked.Increment(ref _requests) - 1;
        var choice = Choices[Math.Min(index, Choices.Count - 1)];
        return Task.FromResult(new DecisionResponse
        {
            Model = "fake",
            Answers = request.Questions.Keys.ToImmutableDictionary(
                name => name,
                _ => (DecisionAnswer)new ChoiceAnswer
                {
                    Choice = choice,
                    Confidence = 0.9,
                    Probabilities = ImmutableDictionary<string, double>.Empty.Add(choice, 0.9),
                }),
        });
    }
}
