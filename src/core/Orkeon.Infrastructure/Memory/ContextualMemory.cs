using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;
using System.Globalization;
using System.Text;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Contextual memory combining all memory types.
/// </summary>
public sealed class ContextualMemory : IContextualMemory
{
    private readonly IShortTermMemory _shortTerm;
    private readonly ILongTermMemory _longTerm;
    private readonly IEntityMemory _entities;

    /// <summary>Initializes a new instance of <see cref="ContextualMemory"/>.</summary>
    /// <param name="shortTerm">The short-term memory store.</param>
    /// <param name="longTerm">The long-term memory store.</param>
    /// <param name="entities">The entity memory store.</param>
    public ContextualMemory(
        IShortTermMemory shortTerm,
        ILongTermMemory longTerm,
        IEntityMemory entities)
    {
        ArgumentNullException.ThrowIfNull(shortTerm);
        _shortTerm = shortTerm;
        ArgumentNullException.ThrowIfNull(longTerm);
        _longTerm = longTerm;
        ArgumentNullException.ThrowIfNull(entities);
        _entities = entities;
    }

    /// <inheritdoc />
    public Task<string> GetContextAsync(string query, int maxTokens = 1000)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetContextCoreAsync();

        async Task<string> GetContextCoreAsync()
        {
            var context = new StringBuilder();
            var approximateTokens = 0;

            approximateTokens = await AppendShortTermMemoriesAsync(context, approximateTokens, maxTokens).ConfigureAwait(false);
            approximateTokens = await AppendLongTermMemoriesAsync(context, query, approximateTokens, maxTokens).ConfigureAwait(false);
            await AppendEntityInformationAsync(context, query, approximateTokens, maxTokens).ConfigureAwait(false);

            return context.ToString();
        }
    }

    private async Task<int> AppendShortTermMemoriesAsync(StringBuilder context, int approximateTokens, int maxTokens)
    {
        var recentMemories = await _shortTerm.GetRecentAsync(5).ConfigureAwait(false);
        if (recentMemories.Count == 0)
            return approximateTokens;

        context.AppendLine("Recent context:");
        return AppendMemoryItems(context, recentMemories, approximateTokens, maxTokens);
    }

    private async Task<int> AppendLongTermMemoriesAsync(StringBuilder context, string query, int approximateTokens, int maxTokens)
    {
        var relevantMemories = await _longTerm.SearchAsync(query, 5).ConfigureAwait(false);
        if (relevantMemories.Count == 0)
            return approximateTokens;

        context.AppendLine("\nRelevant information:");
        return AppendMemoryItems(context, relevantMemories, approximateTokens, maxTokens);
    }

    private static int AppendMemoryItems(StringBuilder context, IReadOnlyList<MemoryItem> memories, int approximateTokens, int maxTokens)
    {
        foreach (var memory in memories)
        {
            var memoryText = $"- {memory.Content}\n";
            approximateTokens += memoryText.Length / 4;

            if (approximateTokens > maxTokens)
                break;

            context.Append(memoryText);
        }
        return approximateTokens;
    }

    private async Task AppendEntityInformationAsync(StringBuilder context, string query, int approximateTokens, int maxTokens)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words.Where(w => char.IsUpper(w[0])))
        {
            var entity = await _entities.GetEntityAsync(word).ConfigureAwait(false);
            if (entity == null)
                continue;

            var entityText = FormatEntityText(entity);
            approximateTokens += entityText.Length / 4;
            if (approximateTokens > maxTokens)
                break;

            context.Append(entityText);
        }
    }

    private static string FormatEntityText(MemoryEntity entity)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"\n{entity.Name} ({entity.Type}):\n");
        foreach (var (key, value) in entity.Attributes.Take(3))
        {
            sb.Append(CultureInfo.InvariantCulture, $"  - {key}: {value}\n");
        }
        return sb.ToString();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryItem>> GetRelevantMemoriesAsync(string context, int count = 20)
    {
        ArgumentNullException.ThrowIfNull(context);
        return GetRelevantMemoriesCoreAsync();

        async Task<IReadOnlyList<MemoryItem>> GetRelevantMemoriesCoreAsync()
        {
            var allMemories = new List<MemoryItem>();

            // Get more memories than requested to ensure we have enough to choose from
            // But get all available memories to ensure we can return the requested count
            var shortTermMemories = await _shortTerm.GetRecentAsync(count * 2).ConfigureAwait(false);
            allMemories.AddRange(shortTermMemories);

            var longTermMemories = await _longTerm.SearchAsync(context, count * 2).ConfigureAwait(false);
            allMemories.AddRange(longTermMemories);

            // Remove duplicates (same content and timestamp)
            allMemories = allMemories
                .GroupBy(m => new { m.Content, m.Timestamp })
                .Select(g => g.First())
                .ToList();

            // Calculate relevance scores
#pragma warning disable CA1308 // lowercase is the required normalized key form stored in the lookup set, not a comparison normalization
            var keywords = context.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2) // Changed from 3 to 2 to include more keywords
                .Select(w => w.ToLowerInvariant())
                .ToHashSet();
#pragma warning restore CA1308

            // Calculate and update relevance scores based on context
            var scoredMemories = new List<(MemoryItem memory, double relevance)>();

            foreach (var memory in allMemories)
            {
#pragma warning disable CA1308 // lowercase is the required normalized key form for set membership lookup, not a comparison normalization
                var memoryWords = memory.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant());
#pragma warning restore CA1308

                var matchCount = memoryWords.Count(w => keywords.Contains(w));
                var relevance = (double)matchCount / Math.Max(keywords.Count, 1);

                // Combine existing importance with contextual relevance
                var combinedScore = (memory.Importance * 0.5) + (relevance * 0.5);
                scoredMemories.Add((memory, combinedScore));
            }

            // Return top memories by combined score
            return scoredMemories
                .OrderByDescending(m => m.relevance)
                .ThenByDescending(m => m.memory.Timestamp)
                .Take(count)
                .Select(m => m.memory)
                .ToList();
        }
    }

    // REMOVED: Horrible reflection hack - dependencies must be provided via constructor
    // If you need to resolve circular dependencies, use a proper DI container or factory pattern
}
