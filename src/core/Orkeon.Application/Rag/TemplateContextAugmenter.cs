using System.Text;
using Orkeon.Application.Interfaces.Rag;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Rag;

/// <summary>
/// Context augmenter that uses string template substitution to build prompts
/// from retrieved chunks and the user's question.
/// </summary>
public sealed class TemplateContextAugmenter : IContextAugmenter
{
    /// <inheritdoc />
    public System.Threading.Tasks.Task<AugmentedPrompt> AugmentAsync(
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        AugmentationOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var selectedChunks = SelectChunks(chunks, options);

        var contextBuilder = new StringBuilder();
        for (int i = 0; i < selectedChunks.Count; i++)
        {
            var chunk = selectedChunks[i];
            contextBuilder.AppendLine(Inv.Format($"[Source {i + 1}] (relevance: {chunk.RelevanceScore:F2})"));
            contextBuilder.AppendLine(chunk.Content);

            if (options.IncludeSourceReferences &&
                chunk.Metadata.TryGetValue("file_name", out var fileName))
            {
                contextBuilder.AppendLine(Inv.Format($"  -- Source: {fileName}"));
            }

            contextBuilder.AppendLine();
        }

        var userPrompt = options.PromptTemplate
            .Replace("{context}", contextBuilder.ToString(), StringComparison.Ordinal)
            .Replace("{question}", question, StringComparison.Ordinal);

        var estimatedTokens = userPrompt.Length / 4;

        return System.Threading.Tasks.Task.FromResult(new AugmentedPrompt
        {
            SystemPrompt = "You are a helpful assistant that answers questions based on provided context. " +
                           "Always cite your sources when possible. If the context doesn't contain relevant information, say so.",
            UserPrompt = userPrompt,
            UsedChunks = selectedChunks,
            EstimatedTokens = estimatedTokens
        });
    }

    private static List<RetrievedChunk> SelectChunks(
        IReadOnlyList<RetrievedChunk> chunks, AugmentationOptions options)
    {
        var selected = new List<RetrievedChunk>();
        var tokenBudget = options.MaxContextTokens;

        foreach (var chunk in chunks.Take(options.MaxChunksInPrompt))
        {
            var chunkTokens = chunk.Content.Length / 4;
            if (tokenBudget - chunkTokens < 0 && selected.Count > 0)
                break;

            if (options.DeduplicateChunks && selected.Any(s => s.Content == chunk.Content))
                continue;

            selected.Add(chunk);
            tokenBudget -= chunkTokens;
        }

        return selected;
    }
}
