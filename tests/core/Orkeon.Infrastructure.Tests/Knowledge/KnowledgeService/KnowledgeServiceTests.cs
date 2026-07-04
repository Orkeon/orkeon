using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
namespace Orkeon.Infrastructure.Tests.Knowledge;

public sealed class KnowledgeServiceTests : IDisposable
{
    private readonly KnowledgeServiceTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldLoadAndStoreKnowledge_WhenAddingSource()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("test-source", "This is test content for knowledge. It has multiple sentences.");

        var name = await _fixture.AddSourceAsync(source);

        Assert.Equal("test-source", name);

        var stats = await _fixture.GetStatisticsAsync();
        Assert.True(stats.TotalItems > 0);
        Assert.Equal(1, stats.SourceCount);
    }

    [Fact]
    public async Task ShouldNotLoadContent_WhenAddingSourceWithoutImmediateLoad()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("lazy-source", "Content that should not be loaded yet.");

        await _fixture.AddSourceAsync(source, loadImmediately: false);

        var stats = await _fixture.GetStatisticsAsync();
        Assert.Equal(0, stats.TotalItems);
        Assert.Equal(1, stats.SourceCount);
    }

    [Fact]
    public async Task ShouldFindRelevantItems_WhenSearching()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("search-source",
            "The fox is quick and brown. The dog is lazy and sleeps all day. The cat is curious and playful.");

        await _fixture.AddSourceAsync(source);

        var results = await _fixture.SearchAsync("fox quick brown", minSimilarity: 0.3f);

        Assert.NotEmpty(results);
        Assert.Contains("fox", results[0].Content);
    }

    [Fact]
    public async Task ShouldOnlySearchSpecifiedSources_WhenSourceFilterIsProvided()
    {
        var source1 = KnowledgeServiceTestsFixture.CreateMockSource("source-a", "Alpha content about foxes and dogs.");
        var source2 = KnowledgeServiceTestsFixture.CreateMockSource("source-b", "Beta content about foxes and cats.");

        await _fixture.AddSourceAsync(source1);
        await _fixture.AddSourceAsync(source2);

        var results = await _fixture.SearchAsync("foxes", sources: KnowledgeServiceTestsFixture.GetSourceAFilter(), minSimilarity: 0.3f);

        Assert.NotEmpty(results);
        Assert.All(results, item => Assert.Equal("source-a", item.Source));
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchQueryIsEmpty()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("source", "Some content.");
        await _fixture.AddSourceAsync(source);

        var results = await _fixture.SearchAsync("");

        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchHasNoMatch()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("source", "Hello world.");
        await _fixture.AddSourceAsync(source);

        var results = await _fixture.SearchAsync("zzzyyyxxx", minSimilarity: 0.1f);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnContextWithKnowledge_WhenGettingContext()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("context-source",
            "Machine learning is a subset of artificial intelligence. Deep learning uses neural networks.");

        await _fixture.AddSourceAsync(source);

        var context = await _fixture.GetContextAsync("machine learning", agentId: AgentId1);

        Assert.NotNull(context);
        Assert.True(context.Metadata.ContainsKey(ParamQuery));
        Assert.Equal("machine learning", context.Metadata[ParamQuery]);
        Assert.True(context.Metadata.ContainsKey("agent_id"));
        Assert.Equal(AgentId1, context.Metadata["agent_id"]);
    }

    [Fact]
    public async Task ShouldStoreItem_WhenAddingKnowledge()
    {
        var id = await _fixture.AddKnowledgeAsync(
            "Direct knowledge content",
            metadata: new Dictionary<string, object> { ["key"] = "value" },
            source: "manual");

        Assert.False(string.IsNullOrEmpty(id));

        var stats = await _fixture.GetStatisticsAsync();
        Assert.Equal(1, stats.TotalItems);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenAddingEmptyContent()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.AddKnowledgeAsync(""));
    }

    [Fact]
    public async Task ShouldUpdateContent_WhenUpdatingExistingItem()
    {
        var id = await _fixture.AddKnowledgeAsync("Original content", source: "test");

        var updated = await _fixture.UpdateKnowledgeAsync(id, "Updated content");

        Assert.True(updated);

        var results = await _fixture.SearchAsync("Updated", minSimilarity: 0.3f);
        Assert.NotEmpty(results);
        Assert.Equal("Updated content", results[0].Content);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenUpdatingNonExistentItem()
    {
        var result = await _fixture.UpdateKnowledgeAsync("non-existent-id", "content");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldRemoveItem_WhenDeletingKnowledge()
    {
        var id = await _fixture.AddKnowledgeAsync("Content to delete", source: "test");

        var deleted = await _fixture.DeleteKnowledgeAsync(id);

        Assert.True(deleted);

        var stats = await _fixture.GetStatisticsAsync();
        Assert.Equal(0, stats.TotalItems);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenDeletingNonExistentItem()
    {
        var result = await _fixture.DeleteKnowledgeAsync("non-existent-id");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnCorrectCounts_WhenGettingStatistics()
    {
        await _fixture.AddKnowledgeAsync("Item one", source: "source-a");
        await _fixture.AddKnowledgeAsync("Item two", source: "source-a");
        await _fixture.AddKnowledgeAsync("Item three", source: "source-b");

        var stats = await _fixture.GetStatisticsAsync();

        Assert.Equal(3, stats.TotalItems);
        Assert.True(stats.TotalSizeBytes > 0);
    }

    [Fact]
    public async Task ShouldRemoveAllItemsFromSource_WhenRemovingSource()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("removable", "Content that will be removed completely.");
        await _fixture.AddSourceAsync(source);

        var statsBefore = await _fixture.GetStatisticsAsync();
        Assert.True(statsBefore.TotalItems > 0);

        var removed = await _fixture.RemoveSourceAsync("removable");

        Assert.True(removed);

        var statsAfter = await _fixture.GetStatisticsAsync();
        Assert.Equal(0, statsAfter.TotalItems);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenRemovingNonExistentSource()
    {
        var result = await _fixture.RemoveSourceAsync("nonexistent");

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReloadAllSources_WhenRefreshingSources()
    {
        var source = KnowledgeServiceTestsFixture.CreateMockSource("refreshable", "Original content for refresh test.");
        await _fixture.AddSourceAsync(source);

        var result = await _fixture.RefreshSourcesAsync();

        Assert.Equal(1, result.SourcesChecked);
        Assert.Equal(1, result.SourcesUpdated);
        Assert.True(result.ItemsAdded > 0);
    }

    [Fact]
    public async Task ShouldRoundTrip_WhenExportingAndImporting()
    {
        var exportPath = _fixture.CreateExportPath();

        await _fixture.AddKnowledgeAsync("Knowledge item one", source: "test");
        await _fixture.AddKnowledgeAsync("Knowledge item two", source: "test");

        await _fixture.ExportAsync(exportPath);

        Assert.True(File.Exists(exportPath));

        // Create a new service and import
        var newService = _fixture.CreateNewService();
        var count = await newService.ImportAsync(exportPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, count);

        var stats = await newService.GetStatisticsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, stats.TotalItems);
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenImportingNonExistentFile()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _fixture.ImportAsync("/tmp/nonexistent_file.json"));
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenLoadingNonExistentSource()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.LoadSourceAsync("nonexistent"));
    }

    [Fact]
    public async Task ShouldIncludeFiltersInMetadata_WhenGettingContextWithFilters()
    {
        await _fixture.AddKnowledgeAsync("Some knowledge", source: "test");

        var filters = new Dictionary<string, object> { ["category"] = "science" };
        var context = await _fixture.GetContextAsync("knowledge", filters: filters);

        Assert.True(context.Metadata.ContainsKey("filter_category"));
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
