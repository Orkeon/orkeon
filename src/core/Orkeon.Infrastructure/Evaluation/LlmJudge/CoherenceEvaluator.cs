using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation.LlmJudge;

/// <summary>
/// LLM-as-Judge evaluator that rates logical coherence and consistency of the output.
/// </summary>
public sealed class CoherenceEvaluator : LlmJudgeEvaluatorBase
{
    /// <inheritdoc />
    public override string Name => "Coherence";
    /// <inheritdoc />
    public override string Description => "Judges logical coherence and consistency of the output.";

    /// <summary>Initializes a new instance of <see cref="CoherenceEvaluator"/>.</summary>
    /// <param name="chatClient">The chat client used as judge.</param>
    public CoherenceEvaluator(IChatClient chatClient) : base(chatClient) { }

    /// <inheritdoc />
    protected override string BuildJudgePrompt(EvaluationInput input)
    {
        var prompt = new StringBuilder();
        prompt.Append("""
            Evaluate the logical coherence and consistency of the following output.

            Consider:
            1. Does the output maintain logical consistency throughout?
            2. Are there any contradictions or conflicting statements?
            3. Does the reasoning flow logically from one point to the next?
            4. Are conclusions supported by the preceding content?
            """);

        if (!string.IsNullOrWhiteSpace(input.TaskDescription))
        {
            prompt.Append(CultureInfo.InvariantCulture, $"\n\nTask description:\n{input.TaskDescription}");
        }

        prompt.Append(CultureInfo.InvariantCulture, $"\n\nOutput to evaluate:\n{input.Output}");
        prompt.Append(JudgeInstructionSuffix);

        return prompt.ToString();
    }
}
