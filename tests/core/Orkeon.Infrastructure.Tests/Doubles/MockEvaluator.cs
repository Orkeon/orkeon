using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IEvaluator with call tracking and configurable results.
/// </summary>
public class MockEvaluator : IEvaluator
{
    private EvaluationScore _evaluateResult = new("MockEvaluator", 1.0);
    private Func<EvaluationInput, EvaluationScore>? _evaluateFunc;

    // --- Tracking ---
    public int EvaluateCallCount { get; private set; }
    public EvaluationInput? LastEvaluationInput { get; private set; }
    public List<EvaluationInput> AllEvaluationInputs { get; } = [];

    // --- Configuration ---
    public string Name { get; set; } = "MockEvaluator";
    public string Description { get; set; } = "A mock evaluator for testing";
    public bool RequiresLlm { get; set; }

    public MockEvaluator()
    {
    }

    public void SetEvaluateResult(EvaluationScore result) => _evaluateResult = result;

    public void SetScore(double score, string? reasoning = null) =>
        _evaluateResult = new EvaluationScore(Name, score, reasoning);

    public void SetEvaluateFunc(Func<EvaluationInput, EvaluationScore> func) => _evaluateFunc = func;

    // --- IEvaluator ---
    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input, CancellationToken ct = default)
    {
        EvaluateCallCount++;
        LastEvaluationInput = input;
        AllEvaluationInputs.Add(input);

        var result = _evaluateFunc != null ? _evaluateFunc(input) : _evaluateResult;
        return Task.FromResult(result);
    }
}
