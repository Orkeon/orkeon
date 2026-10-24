using Orkeon.Rag.Validation;

namespace Orkeon.Rag.Tests.Validation;

/// <summary>Tests for the in-memory <see cref="ProvenanceTracker"/>.</summary>
public class ProvenanceTrackerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShouldRecordHashSourceAndMetadata_WhenTracked()
    {
        var tracker = new ProvenanceTracker();
        var before = DateTimeOffset.UtcNow;

        var record = await tracker.TrackAsync("d1", "hello", "web", new Dictionary<string, string> { ["k"] = "v" }, Ct);

        var after = DateTimeOffset.UtcNow;
        Assert.Equal("d1", record.DocumentId);
        Assert.Equal(ContentIntegrityValidator.ComputeHash("hello"), record.ContentHash);
        Assert.Equal("web", record.Source);
        Assert.Equal("v", record.Metadata["k"]);
        Assert.InRange(record.IngestedAt, before, after);
        Assert.Null(record.LastVerifiedHash);
        Assert.Same(record, await tracker.GetAsync("d1", Ct));
    }

    [Fact]
    public async Task ShouldUseEmptyMetadata_WhenNoneIsGiven()
    {
        var record = await new ProvenanceTracker().TrackAsync("d1", "x", "s", ct: Ct);

        Assert.Empty(record.Metadata);
    }

    [Fact]
    public async Task ShouldReturnNull_WhenDocumentIsUnknown()
    {
        Assert.Null(await new ProvenanceTracker().GetAsync("missing", Ct));
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenVerifyingUnknownDocument()
    {
        Assert.False(await new ProvenanceTracker().VerifyIntegrityAsync("missing", "x", Ct));
    }

    [Fact]
    public async Task ShouldConfirmIntegrityAndStampVerification_WhenContentIsUnchanged()
    {
        var tracker = new ProvenanceTracker();
        await tracker.TrackAsync("d1", "hello", "web", ct: Ct);
        var before = DateTimeOffset.UtcNow;

        Assert.True(await tracker.VerifyIntegrityAsync("d1", "hello", Ct));

        var after = DateTimeOffset.UtcNow;
        var record = await tracker.GetAsync("d1", Ct);
        Assert.NotNull(record);
        Assert.Equal(record.ContentHash, record.LastVerifiedHash);
        Assert.NotNull(record.LastVerifiedAt);
        Assert.InRange(record.LastVerifiedAt.Value, before, after);
    }

    [Fact]
    public async Task ShouldReportTamperingAndKeepOriginalHash_WhenContentChanged()
    {
        var tracker = new ProvenanceTracker();
        await tracker.TrackAsync("d1", "hello", "web", ct: Ct);

        Assert.False(await tracker.VerifyIntegrityAsync("d1", "hello!", Ct));

        var record = await tracker.GetAsync("d1", Ct);
        Assert.NotNull(record);
        Assert.Equal(ContentIntegrityValidator.ComputeHash("hello"), record.ContentHash);
        Assert.Equal(ContentIntegrityValidator.ComputeHash("hello!"), record.LastVerifiedHash);
    }

    [Fact]
    public async Task ShouldFilterBySourceIgnoringCase_WhenListing()
    {
        var tracker = new ProvenanceTracker();
        await tracker.TrackAsync("d1", "a", "Web", ct: Ct);
        await tracker.TrackAsync("d2", "b", "file", ct: Ct);
        await tracker.TrackAsync("d3", "c", "web", ct: Ct);

        var all = await tracker.ListAsync(ct: Ct);
        var web = await tracker.ListAsync("WEB", Ct);

        Assert.Equal(3, all.Count);
        Assert.Equal(["d1", "d3"], web.Select(r => r.DocumentId).Order(StringComparer.Ordinal));
    }
}
