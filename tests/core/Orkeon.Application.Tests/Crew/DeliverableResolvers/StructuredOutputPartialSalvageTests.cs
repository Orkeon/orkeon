using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.DeliverableResolvers;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.DeliverableResolvers;

/// <summary>
/// Regression coverage for Experiment 07 friction #3: when an LLM emits JSON that misses
/// some required top-level keys, the resolver must salvage what it can (persist the file
/// flagged as <see cref="DeliverableResolutionResult.PartialExtraction"/>) instead of
/// dropping the assistant message on the floor. Fence/greedy extraction paths are
/// already covered by <see cref="StructuredOutputResolverTests"/>; this file focuses on
/// the partial-salvage decision matrix and the enriched fail-mode logging.
/// </summary>
public sealed class StructuredOutputPartialSalvageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DiskBackedFileSystemService _fs;

    public StructuredOutputPartialSalvageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-partial-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/output");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    private StructuredOutputResolver NewResolver() => new(_fs, NullLogger<StructuredOutputResolver>.Instance);

    private static CrewTask BuildTaskWithRequired(string virtualPath, params string[] requiredKeys)
    {
        var task = CrewTask.Create(
            TaskDescription.From("partial-salvage task"),
            ExpectedOutput.From("JSON with required keys"));

        // Build a minimal inline JSON Schema that declares the required keys.
        var requiredArray = string.Join(",", requiredKeys.Select(k => $"\"{k}\""));
        var schema = $"{{\"type\":\"object\",\"required\":[{requiredArray}]}}";
        task.SetDeliverable(new TaskDeliverable
        {
            Path = virtualPath,
            Source = DeliverableSource.StructuredOutput,
            Format = "json",
            SchemaInline = schema,
        });
        return task;
    }

    [Fact]
    public async System.Threading.Tasks.Task PersistsWithPartialFlag_WhenJsonMissesOneRequiredKey()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithRequired("/output/partial.json", "status", "items_count");

        // Model emitted `status` but forgot `items_count`. Previously the resolver
        // failed with `missing_required_keys` and wrote nothing — friction #3.
        const string content = "{\"status\":\"ok\"}";

        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.True(result.PartialExtraction);
        Assert.Equal("partial_extraction", result.FailureReason);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "partial.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"ok\"", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task DoesNotMarkPartial_WhenAllRequiredKeysPresent()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithRequired("/output/full.json", "status", "items_count");

        const string content = "{\"status\":\"ok\",\"items_count\":42}";

        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.False(result.PartialExtraction);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async System.Threading.Tasks.Task PartialExtractionSurvives_WhenJsonIsFenceWrapped()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithRequired("/output/fenced-partial.json", "status", "summary");

        // Fence wrapper around a partial payload — both fence stripping AND partial
        // salvage must trigger in concert.
        const string content = "Result:\n```json\n{\"status\":\"ok\"}\n```";

        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.True(result.PartialExtraction);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "fenced-partial.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"ok\"", written);
        Assert.DoesNotContain("```", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task PartialExtractionSurvives_WhenJsonIsProseWrapped()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithRequired("/output/prose-partial.json", "status", "errors");

        // Prose before and after the partial payload.
        const string content = "Here is what I have so far: {\"status\":\"ok\"} (errors not yet enumerated)";

        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.True(result.PartialExtraction);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "prose-partial.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"ok\"", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task StillFails_WhenNoCandidateParses_EvenWithRequiredKeys()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithRequired("/output/none-parse.json", "status");

        // Pure prose with no balanced JSON blocks — even greedy extraction yields
        // nothing parseable. The historical hard-fail behavior is preserved.
        const string content = "I cannot produce valid JSON right now.";

        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.False(result.PartialExtraction);
        Assert.Equal("no_json_payload", result.FailureReason);
        Assert.False(File.Exists(Path.Combine(_tempDir, "none-parse.json")));
    }

    [Fact]
    public async System.Threading.Tasks.Task StillFails_WhenOnlyMalformedCandidates_EvenWithRequiredKeys()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithRequired("/output/malformed.json", "status");

        const string content = "{not_even_json: yes}";

        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.False(result.PartialExtraction);
        Assert.Equal("invalid_json", result.FailureReason);
    }

    [Fact]
    public void RawHash_IsStable_ForKnownText()
    {
        var hash1 = StructuredOutputResolver.RawHash("hello world");
        var hash2 = StructuredOutputResolver.RawHash("hello world");
        Assert.Equal(hash1, hash2);
        Assert.Equal(16, hash1.Length); // 8 bytes × 2 hex chars
    }

    [Fact]
    public void RawHash_IsEmptyMarker_ForNullOrEmpty()
    {
        Assert.Equal("<empty>", StructuredOutputResolver.RawHash(null));
        Assert.Equal("<empty>", StructuredOutputResolver.RawHash(""));
    }
}
