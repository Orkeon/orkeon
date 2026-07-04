using Orkeon.Application.Evaluation;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Evaluation.Base;

/// <summary>
/// Generic typed base class for non-LLM evaluators. Bridges IEvaluator with
/// the ComponentBase Dict-Model pipeline for strongly typed evaluation.
/// Complementary to LlmJudgeEvaluatorBase (for LLM-based evaluators).
/// </summary>
public abstract class EvaluatorBase<TInput, TResult> : ComponentBase<TInput, TResult>, IEvaluator
    where TInput : class, new()
    where TResult : class
{
    /// <inheritdoc />
    public abstract string Name { get; }
    /// <inheritdoc />
    public abstract string Description { get; }
    /// <inheritdoc />
    public virtual bool RequiresLlm => false;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Evaluator-boundary fault barrier: any failure in a concrete evaluator's typed pipeline is converted into a 0.0 EvaluationScore so one evaluator cannot crash the evaluation run.")]
    public async Task<EvaluationScore> EvaluateAsync(EvaluationInput input, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Build parameters from evaluation input
            var parameters = BuildParametersFromInput(input);

            // Step 2: Deserialize to typed input
            var typedInput = DeserializeRequest(parameters);

            // Step 3: Validate
            var validationError = ValidateTypedRequest(typedInput);
            if (validationError is not null)
            {
                return new EvaluationScore(Name, 0.0, Reasoning: $"Validation failed: {validationError}");
            }

            // Step 4: Execute typed evaluation
            var typedResult = await ExecuteTypedAsync(typedInput, ct).ConfigureAwait(false);

            // Step 5: Serialize to details dictionary
            var details = SerializeResponse(typedResult);

            // Step 6-7: Extract score and reasoning
            var score = ExtractScore(typedResult);
            var reasoning = ExtractReasoning(typedResult);

            return new EvaluationScore(
                Name,
                score,
                reasoning,
                details.AsReadOnly());
        }
        catch (Exception ex)
        {
            return new EvaluationScore(Name, 0.0, Reasoning: $"Evaluation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts EvaluationInput to a parameter dictionary for deserialization.
    /// </summary>
    protected abstract Dictionary<string, object?> BuildParametersFromInput(EvaluationInput input);

    /// <summary>
    /// Extracts the numeric score from the typed result.
    /// </summary>
    protected abstract double ExtractScore(TResult result);

    /// <summary>
    /// Optionally extracts reasoning text from the typed result. Default: null.
    /// </summary>
    protected virtual string? ExtractReasoning(TResult result) => null;
}

/// <summary>
/// Extension to make Dictionary read-only.
/// </summary>
internal static class DictionaryExtensions
{
    public static IReadOnlyDictionary<string, object> AsReadOnly(this Dictionary<string, object?> dict)
        => dict.Where(kvp => kvp.Value is not null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);
}
