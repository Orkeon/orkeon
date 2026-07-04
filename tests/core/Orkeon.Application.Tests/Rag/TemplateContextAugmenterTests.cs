using Orkeon.Application.Rag;

namespace Orkeon.Application.Tests.Rag;

public class TemplateContextAugmenterTests
{
    private readonly TemplateContextAugmenter _augmenter = new();

    private static List<RetrievedChunk> SampleChunks() =>
    [
        new()
        {
            Content = "AI stands for Artificial Intelligence.",
            SourceId = "doc1",
            RelevanceScore = 0.95f,
            Metadata = new Dictionary<string, object> { ["file_name"] = "glossary.txt" }
        },
        new()
        {
            Content = "Machine Learning is a subset of AI.",
            SourceId = "doc2",
            RelevanceScore = 0.85f,
            Metadata = []
        },
        new()
        {
            Content = "Deep Learning uses neural networks.",
            SourceId = "doc3",
            RelevanceScore = 0.75f,
            Metadata = []
        }
    ];

    [Fact]
    public async System.Threading.Tasks.Task ShouldAppliesTemplate_WhenAugmentingAsync()
    {
        var chunks = SampleChunks();
        var options = new AugmentationOptions();

        var result = await _augmenter.AugmentAsync("What is AI?", chunks, options, TestContext.Current.CancellationToken);

        Assert.Contains("What is AI?", result.UserPrompt);
        Assert.Contains("AI stands for Artificial Intelligence", result.UserPrompt);
        Assert.NotEmpty(result.SystemPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectsTokenBudget_WhenAugmentingAsync()
    {
        // Create chunks with known sizes
        var largeContent = new string('X', 4000); // ~1000 tokens
        var chunks = new List<RetrievedChunk>
        {
            new() { Content = largeContent, SourceId = "s1", RelevanceScore = 0.9f, Metadata = [] },
            new() { Content = largeContent, SourceId = "s2", RelevanceScore = 0.8f, Metadata = [] },
            new() { Content = largeContent, SourceId = "s3", RelevanceScore = 0.7f, Metadata = [] },
        };

        var options = new AugmentationOptions
        {
            MaxContextTokens = 1200, // Only enough for ~1 large chunk
            MaxChunksInPrompt = 10
        };

        var result = await _augmenter.AugmentAsync("question", chunks, options, TestContext.Current.CancellationToken);

        // Should only include chunks that fit in budget
        Assert.True(result.UsedChunks.Count < chunks.Count,
            $"Expected fewer than {chunks.Count} chunks due to token budget, got {result.UsedChunks.Count}");
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDeduplicatesChunks_WhenAugmentingAsync()
    {
        var chunks = new List<RetrievedChunk>
        {
            new() { Content = "Same content", SourceId = "s1", RelevanceScore = 0.9f, Metadata = [] },
            new() { Content = "Same content", SourceId = "s2", RelevanceScore = 0.8f, Metadata = [] },
            new() { Content = "Different content", SourceId = "s3", RelevanceScore = 0.7f, Metadata = [] },
        };

        var options = new AugmentationOptions { DeduplicateChunks = true };

        var result = await _augmenter.AugmentAsync("question", chunks, options, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.UsedChunks.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldIncludeSourceReferences_WhenAugmentingAsync()
    {
        var chunks = SampleChunks();
        var options = new AugmentationOptions { IncludeSourceReferences = true };

        var result = await _augmenter.AugmentAsync("question", chunks, options, TestContext.Current.CancellationToken);

        // First chunk has file_name metadata
        Assert.Contains("glossary.txt", result.UserPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLimitsMaxChunks_WhenAugmentingAsync()
    {
        var chunks = SampleChunks();
        var options = new AugmentationOptions
        {
            MaxChunksInPrompt = 1,
            MaxContextTokens = 100000 // large budget, so only MaxChunksInPrompt matters
        };

        var result = await _augmenter.AugmentAsync("question", chunks, options, TestContext.Current.CancellationToken);

        Assert.Single(result.UsedChunks);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyContext_WhenAugmentingAsyncWithEmptyChunks()
    {
        var chunks = Array.Empty<RetrievedChunk>();
        var options = new AugmentationOptions();

        var result = await _augmenter.AugmentAsync("question", chunks, options, TestContext.Current.CancellationToken);

        Assert.Empty(result.UsedChunks);
        Assert.Contains("question", result.UserPrompt);
        Assert.NotEmpty(result.SystemPrompt);
    }
}
