using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Knowledge.Validation;

namespace Orkeon.Infrastructure.Tests.Knowledge.Validation;

public class QuarantineStoreTests
{
    private readonly InMemoryQuarantineStore _store = new();

    [Fact]
    public async Task Quarantine_StoresDocument()
    {
        var result = DataValidationResult.Quarantined("suspicious", 0.5);
        await _store.QuarantineAsync("doc-1", "content", result, TestContext.Current.CancellationToken);

        var doc = await _store.GetAsync("doc-1", TestContext.Current.CancellationToken);

        Assert.NotNull(doc);
        Assert.Equal("doc-1", doc.DocumentId);
        Assert.Equal("content", doc.Content);
        Assert.Null(doc.Approved);
    }

    [Fact]
    public async Task ListPending_ReturnsUnreviewed()
    {
        var result = DataValidationResult.Quarantined("suspicious", 0.5);
        await _store.QuarantineAsync("doc-1", "content1", result, TestContext.Current.CancellationToken);
        await _store.QuarantineAsync("doc-2", "content2", result, TestContext.Current.CancellationToken);

        // Review one
        await _store.ReviewAsync("doc-1", true, "admin", TestContext.Current.CancellationToken);

        var pending = await _store.ListPendingAsync(TestContext.Current.CancellationToken);

        Assert.Single(pending);
        Assert.Equal("doc-2", pending[0].DocumentId);
    }

    [Fact]
    public async Task Review_MarksAsApproved()
    {
        var result = DataValidationResult.Quarantined("suspicious", 0.5);
        await _store.QuarantineAsync("doc-1", "content", result, TestContext.Current.CancellationToken);

        await _store.ReviewAsync("doc-1", true, "admin", TestContext.Current.CancellationToken);

        var doc = await _store.GetAsync("doc-1", TestContext.Current.CancellationToken);

        Assert.NotNull(doc);
        Assert.True(doc.Approved);
        Assert.Equal("admin", doc.ReviewedBy);
        Assert.NotNull(doc.ReviewedAt);
    }
}
