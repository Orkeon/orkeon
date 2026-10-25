using Orkeon.Application.Interfaces.Security;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.Tests.Validation;

/// <summary>Tests for <see cref="InMemoryQuarantineStore"/>: quarantine, review and pending listing.</summary>
public class InMemoryQuarantineStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DataValidationResult Suspicious() =>
        DataValidationResult.Quarantined("suspicious", 0.5, ["act-as"]);

    [Fact]
    public async Task ShouldKeepDocumentPending_WhenQuarantined()
    {
        var store = new InMemoryQuarantineStore();
        var result = Suspicious();
        var before = DateTimeOffset.UtcNow;

        await store.QuarantineAsync("d1", "content", result, Ct);

        var after = DateTimeOffset.UtcNow;
        var doc = await store.GetAsync("d1", Ct);
        Assert.NotNull(doc);
        Assert.Equal("content", doc.Content);
        Assert.Same(result, doc.ValidationResult);
        Assert.InRange(doc.QuarantinedAt, before, after);
        Assert.Null(doc.Approved);
        Assert.Null(doc.ReviewedBy);
        Assert.Equal("d1", Assert.Single(await store.ListPendingAsync(Ct)).DocumentId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldRecordReviewAndLeavePendingList_WhenReviewed(bool approved)
    {
        var store = new InMemoryQuarantineStore();
        await store.QuarantineAsync("d1", "one", Suspicious(), Ct);
        await store.QuarantineAsync("d2", "two", Suspicious(), Ct);

        await store.ReviewAsync("d1", approved, "alice", Ct);

        var doc = await store.GetAsync("d1", Ct);
        Assert.NotNull(doc);
        Assert.Equal(approved, doc.Approved);
        Assert.Equal("alice", doc.ReviewedBy);
        Assert.NotNull(doc.ReviewedAt);
        Assert.Equal("d2", Assert.Single(await store.ListPendingAsync(Ct)).DocumentId);
    }

    [Fact]
    public async Task ShouldIgnoreReview_WhenDocumentIsUnknown()
    {
        var store = new InMemoryQuarantineStore();

        await store.ReviewAsync("missing", true, "alice", Ct);

        Assert.Null(await store.GetAsync("missing", Ct));
        Assert.Empty(await store.ListPendingAsync(Ct));
    }
}
