using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Knowledge.Validation;

namespace Orkeon.Infrastructure.Tests.Knowledge.Validation;

public class ContentIntegrityValidatorTests
{
    private readonly ContentIntegrityValidator _validator = new();

    [Fact]
    public async Task Validate_FirstIngestion_Allows()
    {
        var context = new DataValidationContext("doc-1");
        var result = await _validator.ValidateAsync("Hello world", context, TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Allow, result.Decision);
    }

    [Fact]
    public async Task Validate_UnmodifiedContent_Allows()
    {
        var content = "Hello world";
        var hash = ContentIntegrityValidator.ComputeHash(content);
        var context = new DataValidationContext("doc-1", ContentHash: hash);

        var result = await _validator.ValidateAsync(content, context, TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Allow, result.Decision);
    }

    [Fact]
    public async Task Validate_ModifiedContent_Rejects()
    {
        var originalContent = "Hello world";
        var hash = ContentIntegrityValidator.ComputeHash(originalContent);
        var context = new DataValidationContext("doc-1", ContentHash: hash);

        var result = await _validator.ValidateAsync("Modified content", context, TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Reject, result.Decision);
        Assert.Equal("Content modified after ingestion", result.Reason);
        Assert.Equal(1.0, result.RiskScore);
    }

    [Fact]
    public void ComputeHash_ProducesConsistentHash()
    {
        var content = "Test content for hashing";
        var hash1 = ContentIntegrityValidator.ComputeHash(content);
        var hash2 = ContentIntegrityValidator.ComputeHash(content);

        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
        // SHA-256 produces 64 hex characters
        Assert.Equal(64, hash1.Length);
    }
}
