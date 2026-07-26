using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// LLM-as-judge for the generation metrics (plan §9.2), through
/// <see cref="IChatClient"/>. Prompts for a strict JSON verdict
/// (<c>{"groundedness": 0..1, "answer_relevance": 0..1}</c>); any failure —
/// transport error, unparsable verdict — falls back to the deterministic
/// <see cref="HeuristicGenerationJudge"/> for THAT case, and the returned
/// judgement is labelled <see cref="RagJudgeMode.Heuristic"/> accordingly.
/// </summary>
public sealed partial class LlmGenerationJudge : IGenerationJudge
{
    /// <summary>System prompt of the judge.</summary>
    public const string SystemPrompt =
        "You are a strict, impartial evaluation judge for retrieval-augmented generation. " +
        "Score the ANSWER for the QUESTION given the CONTEXT passages. " +
        "groundedness: fraction of the answer's claims supported by the context (0 to 1). " +
        "answer_relevance: how completely the answer addresses the question (0 to 1). " +
        "Respond with ONLY a JSON object: {\"groundedness\": <number>, \"answer_relevance\": <number>}.";

    private readonly IChatClient _chatClient;
    private readonly HeuristicGenerationJudge _fallback;
    private readonly ILogger<LlmGenerationJudge> _logger;

    /// <summary>Initializes the judge over <paramref name="chatClient"/>.</summary>
    public LlmGenerationJudge(
        IChatClient chatClient,
        HeuristicGenerationJudge? fallback = null,
        ILogger<LlmGenerationJudge>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _fallback = fallback ?? new HeuristicGenerationJudge();
        _logger = logger ?? NullLogger<LlmGenerationJudge>.Instance;
    }

    /// <inheritdoc />
    public RagJudgeMode Mode => RagJudgeMode.Llm;

    /// <inheritdoc />
    public async Task<GenerationJudgement> JudgeAsync(
        RagEvalCase evalCase,
        RagAnswer answer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evalCase);
        ArgumentNullException.ThrowIfNull(answer);

        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, BuildUserPrompt(evalCase, answer)),
            };

            var response = await _chatClient.GetResponseAsync(messages, options: null, cancellationToken)
                .ConfigureAwait(false);

            var parsed = TryParseVerdict(response.Text ?? string.Empty);
            if (parsed is { } verdict)
            {
                return new GenerationJudgement
                {
                    Mode = RagJudgeMode.Llm,
                    Groundedness = verdict.Groundedness,
                    AnswerRelevance = verdict.AnswerRelevance,
                };
            }

            LogUnparsableVerdict(evalCase.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogJudgeFailure(evalCase.Id, ex.Message);
        }

        // Deterministic fallback — labelled as such (never a silently mislabelled score).
        return await _fallback.JudgeAsync(evalCase, answer, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildUserPrompt(RagEvalCase evalCase, RagAnswer answer)
    {
        var builder = new StringBuilder();
        builder.Append("QUESTION:\n").AppendLine(evalCase.Question).AppendLine();
        builder.Append("ANSWER:\n").AppendLine(answer.Text ?? string.Empty).AppendLine();

        builder.AppendLine("CONTEXT:");
        if (answer.Citations.Count == 0)
        {
            builder.AppendLine("(no passage retrieved)");
        }
        else
        {
            foreach (var citation in answer.Citations)
            {
                builder.Append('[').Append(citation.Marker).Append("] (")
                    .Append(citation.SourceId).AppendLine(")");
                builder.AppendLine(citation.Snippet ?? string.Empty);
            }
        }

        if (!string.IsNullOrWhiteSpace(evalCase.ReferenceAnswer))
        {
            builder.AppendLine();
            builder.Append("REFERENCE ANSWER:\n").AppendLine(evalCase.ReferenceAnswer);
        }

        return builder.ToString();
    }

    private static (double Groundedness, double AnswerRelevance)? TryParseVerdict(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            var groundedness = ReadScore(root, "groundedness");
            var relevance = ReadScore(root, "answer_relevance") ?? ReadScore(root, "answerRelevance");
            if (groundedness is null || relevance is null)
                return null;

            return (Math.Clamp(groundedness.Value, 0.0, 1.0), Math.Clamp(relevance.Value, 0.0, 1.0));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? ReadScore(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(
                element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "LLM judge returned an unparsable verdict for case '{CaseId}' — falling back to the heuristic judge (labelled).")]
    private partial void LogUnparsableVerdict(string caseId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "LLM judge failed for case '{CaseId}' ({Reason}) — falling back to the heuristic judge (labelled).")]
    private partial void LogJudgeFailure(string caseId, string reason);
}
