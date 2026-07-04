using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.Memory.Cognitive;

/// <summary>
/// Uses an LLM to detect contradictions between new content and existing memories.
/// </summary>
public sealed partial class ContradictionDetector
{
    private readonly ILlmProvider _llmProvider;
    private readonly CognitiveMemoryOptions _options;
    private readonly ILogger<ContradictionDetector> _logger;

    /// <summary>Initializes a new instance of <see cref="ContradictionDetector"/>.</summary>
    public ContradictionDetector(
        ILlmProvider llmProvider,
        IOptions<CognitiveMemoryOptions> options,
        ILogger<ContradictionDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Checks the new content against existing memories for contradictions.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "LLM-call fault barrier: any provider/parse failure is logged and reported as 'no contradiction' so a transient model error does not block memory writes.")]
    public async Task<ContradictionCheck> CheckAsync(
        string newContent,
        IEnumerable<MemoryItem> existingMemories,
        CancellationToken cancellationToken)
    {
        var memoryList = existingMemories.ToList();
        if (memoryList.Count == 0)
        {
            return new ContradictionCheck { HasContradiction = false };
        }

        var userPrompt = BuildUserPrompt(newContent, memoryList);

        var messages = new[]
        {
            LlmMessage.System(SystemPrompt),
            LlmMessage.User(userPrompt)
        };

        var config = LlmConfig.Create(_options.AnalysisModel ?? LlmDefaults.DefaultModelName) with
        {
            Temperature = 0.0,
            MaxTokens = 400
        };

        try
        {
            var response = await _llmProvider.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
            return ParseCheck(response.Content);
        }
        catch (Exception ex)
        {
            LogDetectionError(ex);
            return new ContradictionCheck { HasContradiction = false };
        }
    }

    private const string SystemPrompt =
        """
        You are a contradiction detection assistant. Compare new content against existing memories and detect contradictions.
        Return a JSON object with these fields:
        - has_contradiction: boolean
        - conflicting_ids: string array of memory IDs that conflict
        - description: string describing the contradiction (empty if none)
        - resolution: string suggesting how to resolve (empty if none)
        - action: string, one of: "keep_new", "keep_existing", "merge", "keep_both"

        Return ONLY valid JSON, no markdown fences or extra text.
        """;

    private static string BuildUserPrompt(string newContent, List<MemoryItem> memories)
    {
        var memorySummaries = string.Join("\n", memories.Select(
            (m, i) => $"[ID={m.Id}] {m.Content}"));

        return $"""
            New content: {newContent}

            Existing memories:
            {memorySummaries}
            """;
    }

    internal ContradictionCheck ParseCheck(string responseContent)
    {
        try
        {
            var jsonStart = responseContent.IndexOf('{', StringComparison.Ordinal);
            var jsonEnd = responseContent.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseContent[jsonStart..(jsonEnd + 1)];
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new ContradictionCheck
                {
                    HasContradiction = GetBoolProperty(root, "has_contradiction", false),
                    ConflictingMemoryIds = GetStringArrayProperty(root, "conflicting_ids"),
                    Description = GetStringProperty(root, "description", ""),
                    Resolution = GetStringProperty(root, "resolution", ""),
                    RecommendedAction = ParseAction(GetStringProperty(root, "action", "keep_both"))
                };
            }
        }
        catch (JsonException ex)
        {
            LogJsonParseWarning(ex, responseContent);
        }

        // Fallback regex
        return FallbackParse(responseContent);
    }

    private ContradictionCheck FallbackParse(string text)
    {
        LogFallbackParsing();

        var hasContradiction = HasContradictionRegex().IsMatch(text);
        var action = ExtractString(text, ActionRegex()) ?? "keep_both";

        return new ContradictionCheck
        {
            HasContradiction = hasContradiction,
            ConflictingMemoryIds = Array.Empty<string>(),
            Description = ExtractString(text, DescriptionRegex()) ?? "",
            Resolution = ExtractString(text, ResolutionRegex()) ?? "",
            RecommendedAction = ParseAction(action)
        };
    }

    private static ConflictResolution ParseAction(string action) =>
#pragma warning disable CA1308 // lowercase is the required switch-key form, not a comparison normalization
        action.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "keep_new" => ConflictResolution.KeepNew,
            "keep_existing" => ConflictResolution.KeepExisting,
            "merge" => ConflictResolution.Merge,
            _ => ConflictResolution.KeepBoth
        };

    private static bool GetBoolProperty(JsonElement element, string name, bool defaultValue)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.String)
                return bool.TryParse(prop.GetString(), out var val) && val;
        }
        return defaultValue;
    }

    private static string GetStringProperty(JsonElement element, string name, string defaultValue)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }

    private static IReadOnlyList<string> GetStringArrayProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            var result = new List<string>();
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    result.Add(item.GetString()!);
            }
            return result;
        }
        return Array.Empty<string>();
    }

    private static string? ExtractString(string text, Regex pattern)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"""has_contradiction""\s*:\s*true", RegexOptions.IgnoreCase)]
    private static partial Regex HasContradictionRegex();

    [GeneratedRegex(@"""action""\s*:\s*""([^""]+)""")]
    private static partial Regex ActionRegex();

    [GeneratedRegex(@"""description""\s*:\s*""([^""]+)""")]
    private static partial Regex DescriptionRegex();

    [GeneratedRegex(@"""resolution""\s*:\s*""([^""]+)""")]
    private static partial Regex ResolutionRegex();

    [LoggerMessage(Level = LogLevel.Error, Message = "Contradiction detection failed, assuming no contradiction")]
    private partial void LogDetectionError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "JSON parse failed for contradiction response: {Response}")]
    private partial void LogJsonParseWarning(Exception ex, string response);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Using fallback regex parsing for contradiction response")]
    private partial void LogFallbackParsing();
}
