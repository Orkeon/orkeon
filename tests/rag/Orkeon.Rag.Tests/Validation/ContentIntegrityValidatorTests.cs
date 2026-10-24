using Orkeon.Application.Interfaces.Security;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.Tests.Validation;

/// <summary>Tests for <see cref="ContentIntegrityValidator"/>: tamper detection by SHA-256.</summary>
public class ContentIntegrityValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ShouldComputeLowercaseSha256_WhenHashing()
    {
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            ContentIntegrityValidator.ComputeHash("hello"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ShouldAllow_WhenNoStoredHashExists(string? storedHash)
    {
        var result = await new ContentIntegrityValidator()
            .ValidateAsync("anything", new DataValidationContext("d1", ContentHash: storedHash), Ct);

        Assert.Equal(DataValidationDecision.Allow, result.Decision);
    }

    [Fact]
    public async Task ShouldAllow_WhenStoredHashMatchesIgnoringCase()
    {
        var hash = ContentIntegrityValidator.ComputeHash("hello").ToUpperInvariant();

        var result = await new ContentIntegrityValidator()
            .ValidateAsync("hello", new DataValidationContext("d1", ContentHash: hash), Ct);

        Assert.Equal(DataValidationDecision.Allow, result.Decision);
    }

    [Fact]
    public async Task ShouldRejectWithBothHashes_WhenContentWasModified()
    {
        var validator = new ContentIntegrityValidator();
        var stored = ContentIntegrityValidator.ComputeHash("hello");

        var result = await validator.ValidateAsync("hello!", new DataValidationContext("d1", ContentHash: stored), Ct);

        Assert.Equal(DataValidationDecision.Reject, result.Decision);
        Assert.Equal(1.0, result.RiskScore);
        var finding = Assert.Single(result.Findings);
        Assert.Contains(stored, finding, StringComparison.Ordinal);
        Assert.Contains(ContentIntegrityValidator.ComputeHash("hello!"), finding, StringComparison.Ordinal);
        Assert.Equal("ContentIntegrity", validator.Name);
    }

    [Fact]
    public async Task ShouldThrow_WhenContextIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new ContentIntegrityValidator().ValidateAsync("x", null!, Ct));
    }
}
