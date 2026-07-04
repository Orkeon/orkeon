using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Knowledge.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.Tests.Knowledge.Validation;

public class DataValidationPipelineTests
{
    private readonly InMemoryQuarantineStore _quarantineStore = new();
    private readonly ProvenanceTracker _provenanceTracker = new();
    private readonly ILogger<DataValidationPipeline> _logger = NullLogger<DataValidationPipeline>.Instance;

    private DataValidationPipeline CreatePipeline(params IDataValidator[] validators)
    {
        return new DataValidationPipeline(validators, _quarantineStore, _provenanceTracker, _logger);
    }

    [Fact]
    public async Task Pipeline_AllValidatorsAllow_Allows()
    {
        var pipeline = CreatePipeline(new AlwaysAllowValidator(), new AlwaysAllowValidator());
        var context = new DataValidationContext("doc-1", Source: "test");

        var result = await pipeline.ValidateAsync("Clean content", context, TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Allow, result.Decision);

        // Should have tracked provenance
        var record = await _provenanceTracker.GetAsync("doc-1", TestContext.Current.CancellationToken);
        Assert.NotNull(record);
    }

    [Fact]
    public async Task Pipeline_AnyValidatorRejects_Rejects()
    {
        var pipeline = CreatePipeline(new AlwaysAllowValidator(), new AlwaysRejectValidator());
        var context = new DataValidationContext("doc-1", Source: "test");

        var result = await pipeline.ValidateAsync("Bad content", context, TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Reject, result.Decision);
    }

    [Fact]
    public async Task Pipeline_QuarantinedDocument_StoredInQuarantine()
    {
        var pipeline = CreatePipeline(new AlwaysQuarantineValidator());
        var context = new DataValidationContext("doc-1", Source: "test");

        var result = await pipeline.ValidateAsync("Suspicious content", context, TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Quarantine, result.Decision);

        var quarantined = await _quarantineStore.GetAsync("doc-1", TestContext.Current.CancellationToken);
        Assert.NotNull(quarantined);
        Assert.Equal("Suspicious content", quarantined.Content);
    }

    // Test helper validators
    private sealed class AlwaysAllowValidator : IDataValidator
    {
        public string Name => "AlwaysAllow";
        public Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default)
            => Task.FromResult(DataValidationResult.Allowed());
    }

    private sealed class AlwaysRejectValidator : IDataValidator
    {
        public string Name => "AlwaysReject";
        public Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default)
            => Task.FromResult(DataValidationResult.Rejected("Rejected by test", 1.0));
    }

    private sealed class AlwaysQuarantineValidator : IDataValidator
    {
        public string Name => "AlwaysQuarantine";
        public Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default)
            => Task.FromResult(DataValidationResult.Quarantined("Quarantined by test", 0.5));
    }
}
