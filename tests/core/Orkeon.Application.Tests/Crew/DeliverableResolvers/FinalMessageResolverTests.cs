using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.DeliverableResolvers;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.DeliverableResolvers;

public sealed class FinalMessageResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DiskBackedFileSystemService _fs;

    public FinalMessageResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-deliv-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/output");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    private FinalMessageResolver NewResolver() => new(_fs, NullLogger<FinalMessageResolver>.Instance);

    private static CrewTask BuildTask(string virtualPath, bool sanitize = true)
    {
        var task = CrewTask.Create(
            TaskDescription.From("unit-test task"),
            ExpectedOutput.From("markdown report"));
        task.SetDeliverable(new TaskDeliverable
        {
            Path = virtualPath,
            Source = DeliverableSource.FinalMessage,
            Format = "markdown",
            Sanitize = sanitize,
        });
        return task;
    }

    [Fact]
    public async System.Threading.Tasks.Task WritesFinalText_WhenNonEmpty()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/report.md");

        var result = await resolver.ResolveAsync(task, "# Hello\n\nBody of report.", CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.Equal("/output/report.md", result.Path);
        Assert.Equal(DeliverableSource.FinalMessage, result.SourceUsed);

        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "report.md"), TestContext.Current.CancellationToken);
        Assert.Contains("# Hello", written);
        Assert.EndsWith("\n", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task SanitizesTrailingEosTokens()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/eos.md");

        var content = "# Report\n\nBody.  <eos><eos><eos>";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "eos.md"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<eos>", written);
        Assert.Contains("Body.", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task SanitizesTrailingTripleQuote_FromR12Bug()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/r12.md");

        // Exact tail pattern observed in R12 Task 4
        var content = "# AXIS 2 API Surface\n\nContent body here. \"\"\") <eos> <eos><eos><eos><eos>";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "r12.md"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<eos>", written);
        Assert.DoesNotContain("\"\"\")", written);
        Assert.Contains("Content body here.", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task RespectsSanitizeFalse_WritesRawText()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/raw.md", sanitize: false);

        var content = "body <eos>";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "raw.md"), TestContext.Current.CancellationToken);
        Assert.Contains("<eos>", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReturnsNotPersisted_WhenFinalMessageEmpty()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/empty.md");

        var result = await resolver.ResolveAsync(task, "   \n\t\n  ", CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal("empty_final_message", result.FailureReason);
        Assert.False(File.Exists(Path.Combine(_tempDir, "empty.md")));
    }

    [Fact]
    public async System.Threading.Tasks.Task OverwritesExistingFile_WhenPresent()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/overwrite.md");

        // Seed existing file
        var physPath = Path.Combine(_tempDir, "overwrite.md");
        await File.WriteAllTextAsync(physPath, "STALE", TestContext.Current.CancellationToken);

        var result = await resolver.ResolveAsync(task, "# Fresh\n", CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(physPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("STALE", written);
        Assert.Contains("Fresh", written);
    }

    [Fact]
    public void SupportedSource_IsFinalMessage()
    {
        var resolver = NewResolver();
        Assert.Equal(DeliverableSource.FinalMessage, resolver.SupportedSource);
    }

    [Fact]
    public void Sanitize_StripsMultipleTokenKinds()
    {
        var cleaned = FinalMessageResolver.Sanitize("body\n<|eot_id|><|endoftext|><|im_end|>");
        Assert.Equal("body\n", cleaned);
    }

    [Fact]
    public void Sanitize_NoOp_WhenTextIsClean()
    {
        var cleaned = FinalMessageResolver.Sanitize("regular markdown body");
        Assert.Equal("regular markdown body\n", cleaned);
    }

    [Fact]
    public void DeliverableResolverFactory_DispatchesOnSource()
    {
        var resolver = NewResolver();
        var services = new ServiceCollection();
        services.AddSingleton<IDeliverableResolver>(resolver);
        var sp = services.BuildServiceProvider();
        var factory = new DeliverableResolverFactory(sp);

        Assert.Same(resolver, factory.GetFor(DeliverableSource.FinalMessage));
        Assert.Null(factory.GetFor(DeliverableSource.StructuredOutput));
        Assert.Null(factory.GetFor(DeliverableSource.ToolCall));
    }

    // ── R31-P1 / R31-P2: prose-preamble stripping by format ──────────────

    private static CrewTask BuildTaskWithFormat(string virtualPath, string format)
    {
        var task = CrewTask.Create(
            TaskDescription.From("unit-test task"),
            ExpectedOutput.From("formatted deliverable"));
        task.SetDeliverable(new TaskDeliverable
        {
            Path = virtualPath,
            Source = DeliverableSource.FinalMessage,
            Format = format,
            Sanitize = true,
        });
        return task;
    }

    [Fact]
    public async System.Threading.Tasks.Task YamlFormat_StripsProsePreamble_BeforeNameAnchor()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithFormat("/output/04-resolved.yaml", "yaml");

        // Real shape from R30 attempt-02 D — model prefaced its YAML with
        // a prose summary of its set arithmetic before emitting `name:`.
        const string payload =
            "No Orkéon file reads returned hits. The mapping's section 2.c provides all needed resolution data.\n\n" +
            "**Set arithmetic:**\n- S_yaml = 12\n\n" +
            "name: orkeon_demo_resolved\n" +
            "goal: do something\n";

        var result = await resolver.ResolveAsync(task, payload, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "04-resolved.yaml"), TestContext.Current.CancellationToken);
        Assert.StartsWith("name: orkeon_demo_resolved", written);
        Assert.DoesNotContain("Set arithmetic", written);
        Assert.DoesNotContain("No Orkéon file reads", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task YamlFormat_RecoveryAnchor_StripsPreamble()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithFormat("/output/04-resolved.yaml", "yaml");

        const string payload =
            "Now I have all the data. Producing the final summary.\n\n" +
            "# RECOVERED_FROM_MAPPING_command_registry: dropped upstream\n" +
            "name: orkeon_demo_resolved\n";

        var result = await resolver.ResolveAsync(task, payload, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "04-resolved.yaml"), TestContext.Current.CancellationToken);
        Assert.StartsWith("# RECOVERED_FROM_MAPPING_command_registry", written);
        Assert.DoesNotContain("Now I have", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task MarkdownFormat_StripsProsePreamble_BeforeHeading()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithFormat("/output/02-mapping.md", "markdown");

        // Real shape from R30 attempt-02 B — model prefaced the mapping
        // markdown with reasoning prose before the `# 02-...` header.
        const string payload =
            "I now have the full TS architecture analysis and Orkéon surface data. " +
            "Let me produce the final deliverable.\n\n" +
            "# 02-orkeon-mapping\n\n" +
            "## 1. Mapping table\n";

        var result = await resolver.ResolveAsync(task, payload, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "02-mapping.md"), TestContext.Current.CancellationToken);
        Assert.StartsWith("# 02-orkeon-mapping", written);
        Assert.DoesNotContain("I now have", written);
        Assert.DoesNotContain("Let me produce", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task MarkdownFormat_NumericHeader_IsRecognisedAsAnchor()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithFormat("/output/01-arch.md", "markdown");

        // R29-P2 anchor regression: `# 01-architecture-analysis` (digit
        // after `# `) was rejected by the old `# [A-Za-z]` pattern; the
        // resolver must accept any alphanumeric first char.
        const string payload =
            "Note: I'm finalising the deliverable now.\n\n" +
            "# 01-architecture-analysis\n\n" +
            "## 1. Executive summary\n";

        var result = await resolver.ResolveAsync(task, payload, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "01-arch.md"), TestContext.Current.CancellationToken);
        Assert.StartsWith("# 01-architecture-analysis", written);
        Assert.DoesNotContain("Note:", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task YamlFormat_AlreadyClean_NoStripping()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithFormat("/output/04-clean.yaml", "yaml");

        const string payload =
            "name: orkeon_already_clean_resolved\n" +
            "goal: nothing to strip\n";

        var result = await resolver.ResolveAsync(task, payload, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "04-clean.yaml"), TestContext.Current.CancellationToken);
        Assert.StartsWith("name: orkeon_already_clean_resolved", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task YamlFormat_NoAnchorMatch_PassesThrough()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithFormat("/output/raw.yaml", "yaml");

        // No legal anchor — preserve content unchanged so the runner's
        // post-hoc force_rewrite (or operator inspection) can decide.
        const string payload = "Just some prose with no YAML keys.\n";

        var result = await resolver.ResolveAsync(task, payload, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "raw.yaml"), TestContext.Current.CancellationToken);
        Assert.Equal("Just some prose with no YAML keys.\n", written);
    }

    [Fact]
    public void StripPreambleByFormat_UnknownFormat_ReturnsUnchanged()
    {
        const string payload = "Some prose\n# header\nbody\n";
        var (result, stripped, skipped) = FinalMessageResolver.StripPreambleByFormat(payload, "json");
        Assert.Equal(payload, result);
        Assert.False(stripped);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void StripPreambleByFormat_EmptyPayload_ReturnsUnchanged()
    {
        var (result, stripped, skipped) = FinalMessageResolver.StripPreambleByFormat("", "yaml");
        Assert.Equal("", result);
        Assert.False(stripped);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void UnwrapLineFence_AllLinesBacktickWrapped_StripsBacktickPerLine()
    {
        // R40 attempt-04 reproducer: D's resolved YAML had every line
        // wrapped in single backticks, defeating both anchor strip and
        // every count_pattern downstream.
        var payload = string.Join('\n',
            "`name: claude_code_runtime_replica_resolved`",
            "`resolution_meta:`",
            "`  resolved_concrete: 0`",
            "`  resolved_explicit: 17`",
            "`goal: >`",
            "`  Replicate the runtime as an Orkeon crew`",
            "`process: graph`",
            "`verbose: true`",
            "`memory: true`");

        var (result, unwrapped) = FinalMessageResolver.UnwrapLineFence(payload);

        Assert.True(unwrapped);
        Assert.StartsWith("name: claude_code_runtime_replica_resolved\n", result);
        Assert.DoesNotContain("`name:", result);
        Assert.Contains("process: graph", result);
    }

    [Fact]
    public void UnwrapLineFence_PlainYaml_ReturnsUnchanged()
    {
        // Clean YAML must not be touched.
        var payload = string.Join('\n',
            "name: clean_crew",
            "goal: >",
            "  Plain backticked `code` token in prose still works.",
            "process: sequential",
            "agents:",
            "  supervisor:",
            "    role: Supervisor",
            "tasks:",
            "  go:",
            "    description: do");

        var (result, unwrapped) = FinalMessageResolver.UnwrapLineFence(payload);

        Assert.False(unwrapped);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void UnwrapLineFence_TripleFenceMarkdown_NotAffected()
    {
        // Markdown with triple-fence code blocks must NOT trigger
        // line-fence detection. ``` lines start with backtick but match
        // the triple-fence guard.
        var payload = string.Join('\n',
            "# Architecture analysis",
            "",
            "```yaml",
            "name: example",
            "process: sequential",
            "```",
            "",
            "Body text continues with reference to `Tool` in prose.");

        var (result, unwrapped) = FinalMessageResolver.UnwrapLineFence(payload);

        Assert.False(unwrapped);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void UnwrapLineFence_BelowMinimumLines_NotTriggered()
    {
        // Five backticked lines is below the minimum-8 threshold; the
        // pattern is rare enough at low counts that we don't risk
        // unwrapping a legitimate inline-quoted snippet.
        var payload = string.Join('\n',
            "`name: tiny`",
            "`process: sequential`",
            "`memory: false`",
            "`agents:`",
            "`  one: {}`");

        var (result, unwrapped) = FinalMessageResolver.UnwrapLineFence(payload);

        Assert.False(unwrapped);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void UnwrapLineFence_MixedWrappedAndNot_BelowThreshold_NotTriggered()
    {
        // Only ~50% of lines are wrapped; below the 90% threshold.
        var payload = string.Join('\n',
            "`name: foo`",
            "goal: bar",
            "`process: graph`",
            "verbose: true",
            "`memory: true`",
            "memoryProvider: \"InMemory\"",
            "`planning: true`",
            "agents:",
            "  one: {}");

        var (result, unwrapped) = FinalMessageResolver.UnwrapLineFence(payload);

        Assert.False(unwrapped);
        Assert.Equal(payload, result);
    }
}
