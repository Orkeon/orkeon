using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation.LlmJudge;

/// <summary>
/// LLM-as-Judge evaluator that checks whether the output is grounded in the provided context.
/// </summary>
public sealed class GroundednessEvaluator : LlmJudgeEvaluatorBase
{
    /// <inheritdoc />
    public override string Name => "Groundedness";
    /// <inheritdoc />
    public override string Description => "Judges if the output is grounded in and supported by the provided context.";

    /// <summary>Initializes a new instance of <see cref="GroundednessEvaluator"/>.</summary>
    /// <param name="chatClient">The chat client used as judge.</param>
    public GroundednessEvaluator(IChatClient chatClient) : base(chatClient) { }

    /// <inheritdoc />
    protected override string BuildJudgePrompt(EvaluationInput input)
    {
        var prompt = new StringBuilder();
        prompt.Append("""
            Evaluate whether the following output is grounded in the provided context.

            Consider:
            1. Does the output contain only claims that are supported by the context?
            2. Are there any hallucinated facts or unsupported assertions?
            3. Does the output accurately reflect information from the context?
            4. Are there claims that go beyond what the context supports?
            """);

        if (!string.IsNullOrWhiteSpace(input.Context))
        {
            prompt.Append(CultureInfo.InvariantCulture, $"\n\nContext:\n{input.Context}");
        }
        else
        {
            prompt.Append("\n\nNote: No context was provided. Evaluate general factual accuracy.");
        }

        if (!string.IsNullOrWhiteSpace(input.TaskDescription))
        {
            prompt.Append(CultureInfo.InvariantCulture, $"\n\nTask description:\n{input.TaskDescription}");
        }

        prompt.Append(CultureInfo.InvariantCulture, $"\n\nOutput to evaluate:\n{input.Output}");
        prompt.Append(JudgeInstructionSuffix);

        return prompt.ToString();
    }
}
