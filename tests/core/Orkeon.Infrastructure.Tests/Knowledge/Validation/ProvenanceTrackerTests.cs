using Orkeon.Infrastructure.Knowledge.Validation;

namespace Orkeon.Infrastructure.Tests.Knowledge.Validation;

public class ProvenanceTrackerTests
{
    private readonly ProvenanceTracker _tracker = new();

    [Fact]
    public async Task Track_StoresProvenance_WithHash()
    {
        var content = "Document content";
        var record = await _tracker.TrackAsync("doc-1", content, "file://test.txt", ct: TestContext.Current.CancellationToken);

        Assert.Equal("doc-1", record.DocumentId);
        Assert.Equal("file://test.txt", record.Source);
        Assert.NotEmpty(record.ContentHash);
        Assert.Equal(ContentIntegrityValidator.ComputeHash(content), record.ContentHash);
    }

    [Fact]
    public async Task Verify_UnmodifiedContent_ReturnsTrue()
    {
        var content = "Document content";
        await _tracker.TrackAsync("doc-1", content, "source", ct: TestContext.Current.CancellationToken);

        var isValid = await _tracker.VerifyIntegrityAsync("doc-1", content, TestContext.Current.CancellationToken);

        Assert.True(isValid);
    }

    [Fact]
    public async Task Verify_ModifiedContent_ReturnsFalse()
    {
        await _tracker.TrackAsync("doc-1", "Original content", "source", ct: TestContext.Current.CancellationToken);

        var isValid = await _tracker.VerifyIntegrityAsync("doc-1", "Tampered content", TestContext.Current.CancellationToken);

        Assert.False(isValid);
    }

    [Fact]
    public async Task Get_ExistingDocument_ReturnsRecord()
    {
        await _tracker.TrackAsync("doc-1", "Content", "source", ct: TestContext.Current.CancellationToken);

        var record = await _tracker.GetAsync("doc-1", TestContext.Current.CancellationToken);

        Assert.NotNull(record);
        Assert.Equal("doc-1", record.DocumentId);
    }

    [Fact]
    public async Task Get_NonExistingDocument_ReturnsNull()
    {
        var record = await _tracker.GetAsync("nonexistent", TestContext.Current.CancellationToken);

        Assert.Null(record);
    }

    [Fact]
    public async Task List_FiltersBySource()
    {
        await _tracker.TrackAsync("doc-1", "Content1", "source-a", ct: TestContext.Current.CancellationToken);
        await _tracker.TrackAsync("doc-2", "Content2", "source-b", ct: TestContext.Current.CancellationToken);
        await _tracker.TrackAsync("doc-3", "Content3", "source-a", ct: TestContext.Current.CancellationToken);

        var filtered = await _tracker.ListAsync("source-a", TestContext.Current.CancellationToken);

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, r => Assert.Equal("source-a", r.Source));
    }

    [Fact]
    public async Task List_NoFilter_ReturnsAll()
    {
        await _tracker.TrackAsync("doc-1", "Content1", "source-a", ct: TestContext.Current.CancellationToken);
        await _tracker.TrackAsync("doc-2", "Content2", "source-b", ct: TestContext.Current.CancellationToken);

        var all = await _tracker.ListAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, all.Count);
    }
}
