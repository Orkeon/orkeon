using Orkeon.Application.Interfaces.Security;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.Tests.Validation;

/// <summary>
/// Tests for the ported <see cref="DataValidationPipeline"/>: first Reject wins,
/// Quarantine stores the document, all-Allow tracks provenance.
/// </summary>
public class DataValidationPipelineTests
{
    private sealed class FixedDecisionValidator : IDataValidator
    {
        private readonly DataValidationResult _result;

        public FixedDecisionValidator(string name, DataValidationResult result)
        {
            Name = name;
            _result = result;
        }

        public string Name { get; }

        public int CallCount { get; private set; }

        public Task<DataValidationResult> ValidateAsync(
            string content, DataValidationContext context, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private static DataValidationPipeline CreatePipeline(
        IEnumerable<IDataValidator> validators,
        out InMemoryQuarantineStore quarantine,
        out ProvenanceTracker provenance)
    {
        quarantine = new InMemoryQuarantineStore();
        provenance = new ProvenanceTracker();
        return new DataValidationPipeline(validators, quarantine, provenance);
    }

    [Fact]
    public async Task ValidateAsync_AllAllow_TracksProvenance()
    {
        var pipeline = CreatePipeline(
            [new ContentIntegrityValidator(), new PromptInjectionDocumentValidator()],
            out _,
            out var provenance);

        var result = await pipeline.ValidateAsync(
            "clean content",
            new DataValidationContext("doc-1", "unit-test"),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Allow, result.Decision);
        var record = await provenance.GetAsync("doc-1", TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.Equal("unit-test", record.Source);
        Assert.Equal(ContentIntegrityValidator.ComputeHash("clean content"), record.ContentHash);
    }

    [Fact]
    public async Task ValidateAsync_RejectWins_StopsPipelineAndSkipsProvenance()
    {
        var reject = new FixedDecisionValidator(
            "rejector", DataValidationResult.Rejected("bad", 1.0, ["finding-a"]));
        var never = new FixedDecisionValidator("never", DataValidationResult.Allowed());
        var pipeline = CreatePipeline([reject, never], out _, out var provenance);

        var result = await pipeline.ValidateAsync(
            "content",
            new DataValidationContext("doc-2"),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Reject, result.Decision);
        Assert.Contains("finding-a", result.Findings);
        Assert.Equal(0, never.CallCount);
        Assert.Null(await provenance.GetAsync("doc-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidateAsync_Quarantine_StoresDocumentInQuarantineStore()
    {
        var suspicious = new FixedDecisionValidator(
            "suspicious", DataValidationResult.Quarantined("odd", 0.5, ["finding-q"]));
        var pipeline = CreatePipeline([suspicious], out var quarantine, out _);

        var result = await pipeline.ValidateAsync(
            "questionable content",
            new DataValidationContext("doc-3"),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataValidationDecision.Quarantine, result.Decision);
        var stored = await quarantine.GetAsync("doc-3", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal("questionable content", stored.Content);
        var pending = await quarantine.ListPendingAsync(TestContext.Current.CancellationToken);
        Assert.Single(pending);
    }
}
