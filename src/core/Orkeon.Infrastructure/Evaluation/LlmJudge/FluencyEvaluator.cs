using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation.LlmJudge;

/// <summary>
/// LLM-as-Judge evaluator that rates language fluency and readability.
/// </summary>
public sealed class FluencyEvaluator : LlmJudgeEvaluatorBase
{
    /// <inheritdoc />
    public override string Name => "Fluency";
    /// <inheritdoc />
    public override string Description => "Judges language fluency, grammar, and readability of the output.";

    /// <summary>Initializes a new instance of <see cref="FluencyEvaluator"/>.</summary>
    /// <param name="chatClient">The chat client used as judge.</param>
    public FluencyEvaluator(IChatClient chatClient) : base(chatClient) { }

    /// <inheritdoc />
    protected override string BuildJudgePrompt(EvaluationInput input)
    {
        var prompt = new StringBuilder();
        prompt.Append("""
            Evaluate the language fluency and readability of the following output.

            Consider:
            1. Is the text grammatically correct?
            2. Is it well-structured and easy to read?
            3. Is the vocabulary appropriate for the context?
            4. Are sentences clear and concise?
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
