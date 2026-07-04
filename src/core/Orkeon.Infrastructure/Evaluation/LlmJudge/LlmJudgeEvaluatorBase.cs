using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Evaluation;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Evaluation.LlmJudge;

/// <summary>
/// Base class for LLM-as-Judge evaluators. Sends a structured prompt to an IChatClient
/// and parses a JSON response containing a numeric score (0-10) and reasoning.
/// </summary>
public abstract partial class LlmJudgeEvaluatorBase : IEvaluator
{
    private readonly IChatClient _chatClient;

    /// <inheritdoc />
    public bool RequiresLlm => true;
    /// <inheritdoc />
    public abstract string Name { get; }
    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>Initializes a new instance of <see cref="LlmJudgeEvaluatorBase"/>.</summary>
    /// <param name="chatClient">The chat client to use for judging.</param>
    protected LlmJudgeEvaluatorBase(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
    }

    /// <summary>
    /// Builds the judge prompt that will be sent to the LLM. Subclasses implement this
    /// with domain-specific criteria.
    /// </summary>
    protected abstract string BuildJudgePrompt(EvaluationInput input);

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "LLM-judge fault barrier: any provider/parse failure is converted into a 0.0 EvaluationScore so a transient model error does not crash the evaluation run.")]
    public async Task<EvaluationScore> EvaluateAsync(EvaluationInput input, CancellationToken ct = default)
    {
        var prompt = BuildJudgePrompt(input);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are an impartial evaluation judge. Always respond with valid JSON."),
            new(ChatRole.User, prompt)
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct).ConfigureAwait(false);
            return ParseJudgeResponse(response);
        }
        catch (Exception ex)
        {
            return new EvaluationScore(Name, 0.0, $"LLM judge failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the LLM response to extract score and reasoning.
    /// Expected format: {"score": N, "reasoning": "..."}  where N is 0-10.
    /// Falls back to regex extraction if JSON parsing fails.
    /// </summary>
    protected EvaluationScore ParseJudgeResponse(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = response.Text ?? string.Empty;

        // Try JSON parsing first
        try
        {
            var jsonMatch = JsonBlockRegex().Match(text);
            var jsonText = jsonMatch.Success ? jsonMatch.Value : text;

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var rawScore = root.TryGetProperty("score", out var scoreProp)
                ? scoreProp.GetDouble()
                : 5.0;

            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString()
                : null;

            var normalizedScore = Math.Clamp(rawScore / 10.0, 0.0, 1.0);

            return new EvaluationScore(Name, normalizedScore, reasoning,
                new Dictionary<string, object> { ["raw_score"] = rawScore });
        }
        catch (JsonException)
        {
            // Fallback: try to extract a number from the text
            return ParseFallback(text);
        }
    }

    private EvaluationScore ParseFallback(string text)
    {
        var match = ScoreRegex().Match(text);
        if (match.Success && Inv.TryParseDouble(match.Groups[1].Value, out var numScore))
        {
            var normalized = Math.Clamp(numScore / 10.0, 0.0, 1.0);
            return new EvaluationScore(Name, normalized, text,
                new Dictionary<string, object> { ["raw_score"] = numScore, ["fallback_parse"] = true });
        }

        // If we can't extract any score, return a neutral score with the full text as reasoning.
        return new EvaluationScore(Name, 0.5, $"Could not parse judge response: {text}",
            new Dictionary<string, object> { ["parse_failed"] = true });
    }

    /// <summary>
    /// Builds the standard suffix instructing the LLM to respond with JSON.
    /// </summary>
    protected static string JudgeInstructionSuffix =>
        "\n\nRate on a scale of 0-10. Respond ONLY with valid JSON in this format:\n{\"score\": N, \"reasoning\": \"your reasoning here\"}";

    [GeneratedRegex(@"\{[\s\S]*\}", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex JsonBlockRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:/\s*10|out\s+of\s+10)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ScoreRegex();
}
