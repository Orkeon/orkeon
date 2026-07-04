using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

public class MemoryAnalyzerTests
{
    private readonly MockLlmProvider _llmProvider;
    private readonly MemoryAnalyzer _analyzer;

    public MemoryAnalyzerTests()
    {
        _llmProvider = new MockLlmProvider();
        var options = Options.Create(new CognitiveMemoryOptions());
        var logger = new MockLogger<MemoryAnalyzer>();
        _analyzer = new MemoryAnalyzer(_llmProvider, options, logger);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsValidAnalysis()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"importance": 0.8, "category": "fact", "key_entities": ["OpenAI"], "summary": "AI company info", "suggested_tags": ["ai", "tech"], "reasoning": "important fact"}
            """);

        // Act
        var result = await _analyzer.AnalyzeAsync("OpenAI is an AI company", null, CancellationToken.None);

        // Assert
        Assert.Equal(0.8f, result.Importance, 0.01f);
        Assert.Equal("fact", result.Category);
        Assert.Contains("OpenAI", result.KeyEntities);
        Assert.Equal("AI company info", result.Summary);
        Assert.Contains("ai", result.SuggestedTags);
        Assert.Equal(1, _llmProvider.ChatCallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_WithContext_IncludesContextInPrompt()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"importance": 0.5, "category": "observation", "key_entities": [], "summary": "test", "suggested_tags": [], "reasoning": "ok"}
            """);

        // Act
        await _analyzer.AnalyzeAsync("some content", "research project context", CancellationToken.None);

        // Assert
        var lastMessages = _llmProvider.LastChatMessages;
        Assert.NotNull(lastMessages);
        Assert.Contains("research project context", lastMessages[1].Content);
    }

    [Fact]
    public async Task AnalyzeAsync_HighImportance_ForCriticalInfo()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"importance": 0.95, "category": "decision", "key_entities": ["budget"], "summary": "Critical budget decision", "suggested_tags": ["critical", "budget"], "reasoning": "High-impact decision"}
            """);

        // Act
        var result = await _analyzer.AnalyzeAsync("The project budget has been cut by 50%", null, CancellationToken.None);

        // Assert
        Assert.True(result.Importance > 0.9f);
        Assert.Equal("decision", result.Category);
    }

    [Fact]
    public async Task AnalyzeAsync_LowImportance_ForTrivialInfo()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"importance": 0.1, "category": "observation", "key_entities": [], "summary": "Weather note", "suggested_tags": ["trivial"], "reasoning": "Low importance"}
            """);

        // Act
        var result = await _analyzer.AnalyzeAsync("It might rain tomorrow", null, CancellationToken.None);

        // Assert
        Assert.True(result.Importance <= 0.2f);
    }

    [Fact]
    public async Task AnalyzeAsync_ExtractsKeyEntities()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"importance": 0.7, "category": "relationship", "key_entities": ["Alice", "Bob", "ProjectX"], "summary": "Team assignment", "suggested_tags": ["team"], "reasoning": "People and project"}
            """);

        // Act
        var result = await _analyzer.AnalyzeAsync("Alice and Bob are assigned to ProjectX", null, CancellationToken.None);

        // Assert
        Assert.Equal(3, result.KeyEntities.Count);
        Assert.Contains("Alice", result.KeyEntities);
        Assert.Contains("Bob", result.KeyEntities);
        Assert.Contains("ProjectX", result.KeyEntities);
    }

    [Fact]
    public async Task AnalyzeAsync_InvalidJson_FallbackParsing()
    {
        // Arrange - LLM returns malformed JSON that requires fallback regex parsing
        _llmProvider.SetChatResult("""not json but has "importance": 0.6 and "category": "fact" and "summary": "test summary" in it""");

        // Act
        var result = await _analyzer.AnalyzeAsync("some content", null, CancellationToken.None);

        // Assert - should still extract what it can via regex
        Assert.Equal(0.6f, result.Importance, 0.01f);
        Assert.Equal("fact", result.Category);
        Assert.Equal("test summary", result.Summary);
        Assert.Equal("Parsed via fallback regex", result.Reasoning);
    }
}
