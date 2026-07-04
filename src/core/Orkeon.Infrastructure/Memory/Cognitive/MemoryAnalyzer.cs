using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Constants.Security;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.Cognitive;

/// <summary>
/// Uses an LLM to analyze memory content, producing importance scores,
/// categories, entity extraction, summaries, and suggested tags.
/// </summary>
public sealed partial class MemoryAnalyzer
{
    private readonly ILlmProvider _llmProvider;
    private readonly CognitiveMemoryOptions _options;
    private readonly ILogger<MemoryAnalyzer> _logger;

    /// <summary>Initializes a new instance of <see cref="MemoryAnalyzer"/>.</summary>
    public MemoryAnalyzer(
        ILlmProvider llmProvider,
        IOptions<CognitiveMemoryOptions> options,
        ILogger<MemoryAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Analyzes the given content using the LLM and returns a structured analysis.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "LLM-call fault barrier: any provider/parse failure is logged and a deterministic fallback analysis is returned so a transient model error does not block memory writes.")]
    public Task<MemoryAnalysis> AnalyzeAsync(
        string content,
        string? context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        return AnalyzeCoreAsync();

        async Task<MemoryAnalysis> AnalyzeCoreAsync()
        {
            var userPrompt = BuildUserPrompt(content, context);

            var messages = new[]
            {
                LlmMessage.System(SystemPrompt),
                LlmMessage.User(userPrompt)
            };

            var config = LlmConfig.Create(_options.AnalysisModel ?? LlmDefaults.DefaultModelName) with
            {
                Temperature = _options.AnalysisTemperature,
                MaxTokens = 500
            };

            try
            {
                var response = await _llmProvider.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
                return ParseAnalysis(response.Content);
            }
            catch (Exception ex)
            {
                LogAnalysisError(ex);
                return FallbackAnalysis(content);
            }
        }
    }

    private const string SystemPrompt =
        """
        You are a memory analysis assistant. Analyze the given content and return a JSON object with these fields:
        - importance: float 0.0 to 1.0 (0=trivial, 1=critical)
        - category: string (one of: fact, decision, observation, preference, instruction, event, relationship, error)
        - key_entities: string array of important entities mentioned
        - summary: concise 1-sentence summary
        - suggested_tags: string array of categorization tags
        - reasoning: brief explanation of your analysis

        Return ONLY valid JSON, no markdown fences or extra text.
        """;

    private static string BuildUserPrompt(string content, string? context)
    {
        var prompt = $"Content: {content}";
        if (!string.IsNullOrWhiteSpace(context))
        {
            prompt += $"\nContext: {context}";
        }
        return prompt;
    }

    internal MemoryAnalysis ParseAnalysis(string responseContent)
    {
        // Try JSON parse first
        try
        {
            var jsonStart = responseContent.IndexOf('{', StringComparison.Ordinal);
            var jsonEnd = responseContent.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseContent[jsonStart..(jsonEnd + 1)];
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new MemoryAnalysis
                {
                    Importance = GetFloatProperty(root, "importance", MemoryDefaults.DefaultImportance),
                    Category = GetStringProperty(root, "category", "unknown"),
                    KeyEntities = GetStringArrayProperty(root, "key_entities"),
                    Summary = GetStringProperty(root, "summary", ""),
                    SuggestedTags = GetStringArrayProperty(root, "suggested_tags"),
                    Reasoning = GetStringProperty(root, "reasoning", "")
                };
            }
        }
        catch (JsonException ex)
        {
            LogJsonParseWarning(ex, responseContent);
        }

        // Fallback: regex extraction
        return FallbackParse(responseContent);
    }

    private MemoryAnalysis FallbackParse(string text)
    {
        LogFallbackParsing();

        var importance = ExtractFloat(text, @"""importance""\s*:\s*([\d.]+)") ?? MemoryDefaults.DefaultImportance;
        var category = ExtractString(text, @"""category""\s*:\s*""([^""]+)""") ?? "unknown";
        var summary = ExtractString(text, @"""summary""\s*:\s*""([^""]+)""") ?? "";

        return new MemoryAnalysis
        {
            Importance = Math.Clamp(importance, 0f, 1f),
            Category = category,
            KeyEntities = Array.Empty<string>(),
            Summary = summary,
            SuggestedTags = Array.Empty<string>(),
            Reasoning = "Parsed via fallback regex"
        };
    }

    private static MemoryAnalysis FallbackAnalysis(string content) =>
        new()
        {
            Importance = MemoryDefaults.DefaultImportance,
            Category = "unknown",
            KeyEntities = Array.Empty<string>(),
            Summary = content.Length > 100 ? content[..100] + "..." : content,
            SuggestedTags = Array.Empty<string>(),
            Reasoning = "LLM analysis failed; using defaults"
        };

    private static float GetFloatProperty(JsonElement element, string name, float defaultValue)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return Math.Clamp((float)prop.GetDouble(), 0f, 1f);
            if (prop.ValueKind == JsonValueKind.String && float.TryParse(prop.GetString(), CultureInfo.InvariantCulture, out var val))
                return Math.Clamp(val, 0f, 1f);
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

    private static float? ExtractFloat(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(SecurityDefaults.RegexTimeoutSeconds));
        return match.Success && float.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var val) ? val : null;
    }

    private static string? ExtractString(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(SecurityDefaults.RegexTimeoutSeconds));
        return match.Success ? match.Groups[1].Value : null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "LLM analysis failed, using fallback defaults")]
    private partial void LogAnalysisError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "JSON parse failed for analysis response: {Response}")]
    private partial void LogJsonParseWarning(Exception ex, string response);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Using fallback regex parsing for analysis response")]
    private partial void LogFallbackParsing();
}
