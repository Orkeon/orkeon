using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.QueryTransform;

/// <summary>
/// HyDE transformer (Hypothetical Document Embeddings, guide §6, Gao et al.
/// 2022): one LLM call writes a short hypothetical passage that would answer
/// the question; the result is <c>[hypothetical document]</c> ONLY. Per the
/// HyDE contract (<see cref="Kind"/> = <see cref="QueryTransformKind.Replacement"/>),
/// it is this passage that gets embedded as the retrieval probe INSTEAD of the
/// question — the original query is deliberately not returned (document ↔
/// document comparison fixes the question/document asymmetry). A failing or
/// empty LLM response degrades to <c>[original]</c> with a warning, never an
/// exception.
/// </summary>
public sealed partial class HydeTransformer : IQueryTransformer
{
    /// <summary>Canonical factory name (<c>hyde</c>).</summary>
    public const string TransformerName = "hyde";

    /// <summary>System prompt of the hypothetical-document call.</summary>
    public const string SystemPrompt =
        "You write short hypothetical passages for retrieval. Given a question, write " +
        "one dense, declarative paragraph that would plausibly answer it, using the " +
        "vocabulary a real document on the topic would use. Do not say you are unsure, " +
        "do not add preamble or disclaimers — output the passage only.";

    // Deterministic single-document generation (the paper samples at 0.7 only
    // to average SEVERAL hypothetical documents; we generate one).
    private const float HydeTemperature = 0f;

    private readonly IChatClient _chatClient;
    private readonly ILogger<HydeTransformer> _logger;

    /// <summary>Creates the transformer over the host's chat client.</summary>
    public HydeTransformer(IChatClient chatClient, ILogger<HydeTransformer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _logger = logger ?? NullLogger<HydeTransformer>.Instance;
    }

    /// <inheritdoc />
    public string Name => TransformerName;

    /// <inheritdoc />
    public QueryTransformKind Kind => QueryTransformKind.Replacement;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> TransformAsync(
        string query,
        QueryTransformContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        string document;
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, $"Write a passage that would answer the question: {query}"),
            };

            var response = await _chatClient
                .GetResponseAsync(messages, new ChatOptions { Temperature = HydeTemperature }, cancellationToken)
                .ConfigureAwait(false);

            document = (response.Text ?? string.Empty).Trim();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogGenerationFailed(exception);
            return [query];
        }

        if (document.Length == 0)
        {
            LogEmptyDocument();
            return [query];
        }

        return [document];
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "HyDE hypothetical-document generation failed — falling back to the original query.")]
    private partial void LogGenerationFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "HyDE response contained no usable hypothetical document — falling back to the original query.")]
    private partial void LogEmptyDocument();
}
