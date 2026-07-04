using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.DeliverableResolvers;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.DeliverableResolvers;

public sealed class StructuredOutputResolverTests : IDisposable
{
    private static readonly string[] DeliverableAndStatusKeys = ["deliverable", "overall_status"];

    private readonly string _tempDir;
    private readonly DiskBackedFileSystemService _fs;

    public StructuredOutputResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-struct-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/output");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    private StructuredOutputResolver NewResolver() => new(_fs, NullLogger<StructuredOutputResolver>.Instance);

    private static CrewTask BuildTask(string virtualPath)
    {
        var task = CrewTask.Create(
            TaskDescription.From("unit-test task"),
            ExpectedOutput.From("JSON report"));
        task.SetDeliverable(new TaskDeliverable
        {
            Path = virtualPath,
            Source = DeliverableSource.StructuredOutput,
            Format = "json",
            SchemaInline = "{\"type\":\"object\"}",
        });
        return task;
    }

    [Fact]
    public async System.Threading.Tasks.Task WritesValidJson_WhenRawObject()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/data.json");

        var result = await resolver.ResolveAsync(task, "{\"ok\":true,\"n\":3}", CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.Equal(DeliverableSource.StructuredOutput, result.SourceUsed);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "data.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"ok\":true", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExtractsFromMarkdownFence()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/fenced.json");

        const string content = "Here is the result:\n```json\n{\"status\":\"ok\"}\n```\n";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "fenced.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"ok\"", written);
        Assert.DoesNotContain("```", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExtractsFirstObject_WhenProseWrapped()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/prose.json");

        const string content = "My answer: {\"v\":42} hope that helps.";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "prose.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"v\":42", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExtractsArray_WhenRootIsArray()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/arr.json");

        var result = await resolver.ResolveAsync(task, "[1,2,3]", CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "arr.json"), TestContext.Current.CancellationToken);
        Assert.Contains("[1,2,3]", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task FailsWithInvalidJson_WhenMalformed()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/bad.json");

        var result = await resolver.ResolveAsync(task, "{not even close}", CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal("invalid_json", result.FailureReason);
        Assert.False(File.Exists(Path.Combine(_tempDir, "bad.json")));
    }

    [Fact]
    public async System.Threading.Tasks.Task FailsWithEmpty_WhenOnlyWhitespace()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/empty.json");

        var result = await resolver.ResolveAsync(task, "   \n\t  ", CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal("empty_final_message", result.FailureReason);
    }

    [Fact]
    public async System.Threading.Tasks.Task FailsWithNoJsonPayload_WhenPurelyProse()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/nojson.json");

        var result = await resolver.ResolveAsync(task, "This response has no JSON whatsoever.", CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal("no_json_payload", result.FailureReason);
    }

    [Fact]
    public async System.Threading.Tasks.Task NestedBraces_AreBalanced()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/nested.json");

        const string content = "Result: {\"outer\":{\"inner\":{\"x\":1}}} end";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "nested.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"inner\":{\"x\":1}", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task StringContainingBrace_NotConfusing()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/brace.json");

        const string content = "{\"note\":\"contains } brace\",\"ok\":true}";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "brace.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"contains } brace\"", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task EscapedQuoteInString_DoesNotBreakExtraction()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/escaped.json");

        // The value contains \" which must not be treated as the closing quote.
        const string content = "{\"msg\":\"say \\\"hello\\\"\",\"ok\":true}";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "escaped.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"ok\":true", written);
    }

    [Fact]
    public void SupportedSource_IsStructuredOutput()
    {
        var resolver = NewResolver();
        Assert.Equal(DeliverableSource.StructuredOutput, resolver.SupportedSource);
    }

    [Fact]
    public void Factory_DispatchesStructuredOutput()
    {
        var resolver = NewResolver();
        var services = new ServiceCollection();
        services.AddSingleton<IDeliverableResolver>(resolver);
        var sp = services.BuildServiceProvider();
        var factory = new DeliverableResolverFactory(sp);

        Assert.Same(resolver, factory.GetFor(DeliverableSource.StructuredOutput));
        Assert.Null(factory.GetFor(DeliverableSource.FinalMessage));
    }

    [Fact]
    public void CanonicaliseJson_ReturnsResult_ForValidObject()
    {
        var (result, error) = StructuredOutputResolver.CanonicaliseJson("{\"k\":1}");
        Assert.Equal("{\"k\":1}", result);
        Assert.Null(error);
    }

    [Fact]
    public void CanonicaliseJson_ReturnsNullWithError_ForInvalidInput()
    {
        var (result, error) = StructuredOutputResolver.CanonicaliseJson("{bad json}");
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void CanonicaliseJson_HandlesArray()
    {
        var (result, error) = StructuredOutputResolver.CanonicaliseJson("[1,2,3]");
        Assert.Equal("[1,2,3]", result);
        Assert.Null(error);
    }

    // ── P1+P2 regression: candidate iteration + schema bias ─────────────────

    [Fact]
    public async System.Threading.Tasks.Task SkipsBracketArtifact_WhenSchemaExpectsObject_AndJsonComesAfterProse()
    {
        // R34/xstate regression: model emits a markdown preamble whose `[P1]` brackets
        // were extracted as the first JSON candidate (`[P1]` is balanced but not parseable),
        // then the real `{"deliverables":...}` object. The old single-candidate scanner
        // returned `[P1]`, JsonDocument.Parse failed on the 'P', and FINAL_SUMMARY.json
        // was never persisted. With P1 (try every candidate) + P2 (object schema biases
        // toward `{...}` first), the resolver MUST persist the trailing object.
        var resolver = NewResolver();
        var task = BuildTask("/output/r34.json");

        const string content = """
            I have all the deterministic counts from count_pattern. Let me enumerate:

            - P1 items: `[P1] REFACTOR mapAction`, `[P1] REFACTOR addDescendantStatesToEnter` → 3
            - P2 items: `[P2] VERSION createMachine`, `[P2] TYPE Actor` → 4
            - P3 items: `[P3] TEST formatTransitions`, `[P3] DOCUMENT fromCallback` → 3

            {"deliverables":[{"path":"/output/01_inventory.md","status":"ok","size_bytes":2396}],"overall_status":"ok"}
            """;

        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.Equal(DeliverableSource.StructuredOutput, result.SourceUsed);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "r34.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"overall_status\":\"ok\"", written);
        Assert.DoesNotContain("REFACTOR", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task PrefersObjectCandidate_OverEarlierValidArray_WhenSchemaIsObject()
    {
        // Here both `[1,2]` and `{"foo":1}` parse as valid JSON. The array appears first
        // in the text, so a naive "first parseable" scanner would persist the array.
        // P2 schema bias must prefer the object since the schema declares type=object.
        var resolver = NewResolver();
        var task = BuildTask("/output/biased.json");

        const string content = "Lookup table [1, 2, 3]. Result: {\"foo\":1}.";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "biased.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"foo\":1", written);
        Assert.DoesNotContain("[1", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task PrefersArrayCandidate_OverEarlierValidObject_WhenSchemaIsArray()
    {
        var resolver = NewResolver();
        var task = CrewTask.Create(
            TaskDescription.From("unit-test task"),
            ExpectedOutput.From("JSON array"));
        task.SetDeliverable(new TaskDeliverable
        {
            Path = "/output/arr-biased.json",
            Source = DeliverableSource.StructuredOutput,
            Format = "json",
            SchemaInline = "{\"type\":\"array\"}",
        });

        const string content = "Header {\"k\":1}. Items: [1, 2, 3].";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "arr-biased.json"), TestContext.Current.CancellationToken);
        // Canonicalisation preserves whitespace inside the parsed payload (no reformatting).
        Assert.Contains("[1, 2, 3]", written);
        Assert.DoesNotContain("\"k\":1", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task TriesNextCandidate_WhenFirstObjectFailsToParse()
    {
        // Two object-shaped candidates: the first is malformed (depth-balanced but
        // not valid JSON), the second is correct. P1 must skip the broken one.
        var resolver = NewResolver();
        var task = BuildTask("/output/two-objects.json");

        const string content = "First attempt {oops not json} corrected: {\"ok\":true}.";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "two-objects.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"ok\":true", written);
        Assert.DoesNotContain("oops", written);
    }

    [Fact]
    public async System.Threading.Tasks.Task IsTolerantOfMalformedSchema_FallsBackToScanOrder()
    {
        // A malformed schemaInline must not crash the resolver — it simply disables
        // the schema-bias and the resolver behaves as if expectedRoot=Any.
        var resolver = NewResolver();
        var task = CrewTask.Create(
            TaskDescription.From("unit-test task"),
            ExpectedOutput.From("JSON"));
        task.SetDeliverable(new TaskDeliverable
        {
            Path = "/output/malformed-schema.json",
            Source = DeliverableSource.StructuredOutput,
            Format = "json",
            SchemaInline = "{this is not a schema",
        });

        var result = await resolver.ResolveAsync(task, "{\"ok\":1}", CancellationToken.None);

        Assert.True(result.Persisted);
    }

    [Fact]
    public async System.Threading.Tasks.Task FailsWithInvalidJson_WhenNoCandidateParses()
    {
        var resolver = NewResolver();
        var task = BuildTask("/output/none-parse.json");

        const string content = "Tag [P1] then garbage {nope} and more [oops].";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal("invalid_json", result.FailureReason);
    }

    [Fact]
    public void EnumerateJsonCandidates_ReturnsObjectsBeforeArrays_WhenSchemaIsObject()
    {
        const string text = "Tag [P1] and then {\"a\":1} more [P2] and {\"b\":2}.";
        var candidates = StructuredOutputResolver
            .EnumerateJsonCandidates(text, StructuredOutputResolver.JsonRootKind.Object)
            .ToList();

        // Object candidates must come before array candidates.
        var firstObjIdx = candidates.FindIndex(c => c.StartsWith('{'));
        var firstArrIdx = candidates.FindIndex(c => c.StartsWith('['));
        Assert.True(firstObjIdx >= 0);
        Assert.True(firstArrIdx > firstObjIdx,
            $"Expected object candidates first; got order: {string.Join(" | ", candidates)}");
    }

    [Fact]
    public void EnumerateJsonCandidates_PreservesScanOrder_WhenSchemaIsAny()
    {
        // No leading bracket → no raw-start candidate. Schema=Any disables the bias,
        // so balanced blocks come back in pure scan order.
        const string text = "x [1,2] then {\"a\":1}";
        var candidates = StructuredOutputResolver
            .EnumerateJsonCandidates(text, StructuredOutputResolver.JsonRootKind.Any)
            .ToList();

        Assert.Equal("[1,2]", candidates[0]);
        Assert.Equal("{\"a\":1}", candidates[1]);
    }

    [Theory]
    [InlineData(null, "Any")]
    [InlineData("", "Any")]
    [InlineData("   ", "Any")]
    [InlineData("{\"type\":\"object\"}", "Object")]
    [InlineData("{\"type\":\"array\"}", "Array")]
    [InlineData("{\"type\":\"string\"}", "Any")]
    [InlineData("{\"type\":[\"object\",\"null\"]}", "Object")]
    [InlineData("{\"type\":[\"null\",\"array\"]}", "Array")]
    [InlineData("{not even json", "Any")]
    [InlineData("[\"top-level array schema\"]", "Any")]
    public void ResolveExpectedRoot_MapsSchemaCorrectly(string? schema, string expectedName)
    {
        var actual = StructuredOutputResolver.ResolveExpectedRoot(schema);
        Assert.Equal(expectedName, actual.ToString());
    }

    // ── R32-P1: strict required-keys validation ──────────────────────────

    private static CrewTask BuildTaskWithRequiredKeys(string virtualPath, string[] requiredKeys)
    {
        var task = CrewTask.Create(
            TaskDescription.From("unit-test task"),
            ExpectedOutput.From("JSON report"));
        var schema = "{\"type\":\"object\",\"required\":[" +
                     string.Join(",", requiredKeys.Select(k => $"\"{k}\"")) +
                     "]}";
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
    public async System.Threading.Tasks.Task FEAT07_StrictRequired_PersistsWithPartialFlag_WhenIncompleteJsonOnly()
    {
        // Schema requires both `deliverable` and `overall_status`. The model
        // emits valid JSON with `deliverable` only. Experiment 07 friction #3
        // changed the resolver to salvage the partial payload instead of
        // dropping the assistant message on the floor: PartialExtraction=true
        // and FailureReason="partial_extraction" let downstream gates decide
        // whether the partial result is usable.
        var resolver = NewResolver();
        var task = BuildTaskWithRequiredKeys(
            "/output/incomplete.json",
            DeliverableAndStatusKeys);

        const string content = "{\"deliverable\":{\"status\":\"ok\"}}";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.True(result.PartialExtraction);
        Assert.Equal("partial_extraction", result.FailureReason);
    }

    [Fact]
    public async System.Threading.Tasks.Task FEAT07_StrictRequired_PersistsEmptyObjectWithPartialFlag()
    {
        // R31 a05/a10 B regression: model emits prose like
        // `**S_yaml** = {} — 0 TBDs` and then tries to emit the real
        // FINAL_SUMMARY later. The resolver picks the prose `{}` and now
        // persists it under PartialExtraction=true so the runner / downstream
        // gates can detect the degradation (instead of silently losing the
        // message as the pre-friction-#3 behavior did).
        var resolver = NewResolver();
        var task = BuildTaskWithRequiredKeys(
            "/output/empty.json",
            DeliverableAndStatusKeys);

        const string content = "Set arithmetic: S_yaml = {} so we have nothing.";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.True(result.PartialExtraction);
        Assert.Equal("partial_extraction", result.FailureReason);
    }

    [Fact]
    public async System.Threading.Tasks.Task R32_StrictRequired_AcceptsCompleteJson()
    {
        var resolver = NewResolver();
        var task = BuildTaskWithRequiredKeys(
            "/output/complete.json",
            DeliverableAndStatusKeys);

        const string content = "{\"deliverable\":{\"status\":\"ok\"},\"overall_status\":\"ok\"}";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
    }

    [Fact]
    public async System.Threading.Tasks.Task R32_NoRequiredKeys_FallbackPass2_StillWorks()
    {
        // Schema with no `required` ⇒ pass 2 fallback remains active for
        // backward compat with loose schemas.
        var resolver = NewResolver();
        var task = BuildTask("/output/loose.json");  // schema = {"type":"object"} no required

        const string content = "Note: my answer is {\"v\":42} indeed.";
        var result = await resolver.ResolveAsync(task, content, CancellationToken.None);

        Assert.True(result.Persisted);
        var written = await File.ReadAllTextAsync(Path.Combine(_tempDir, "loose.json"), TestContext.Current.CancellationToken);
        Assert.Contains("\"v\":42", written);
    }

    // ── R36-P3: ground-truth zero-invention safeguard ───────────────────

    [Fact]
    public void CountNumberedTableRows_CountsExactlyTheNumberedRows()
    {
        // Mirrors the prescribed "^\\| +\\d+ +\\| " regex used by Crew A's verifier.
        const string md = """
            # Doc

            ## 4. User Command Surface

            **Total commands enumerated: 3 (registry source: …)**

            | # | Command | Handler FQN |
            |---|---|---|
            | 1 | addDir | `/src/.../addDir` |
            | 2 | clear  | `/src/.../clear`  |
            | 3 | doctor | `/src/.../doctor` |

            ## 5. Axis 1
            (no numbered rows here)

            ## 7. Aliasing notes
            | foo | bar |  (table without leading digit — must NOT count)
            |-----|-----|
            """;

        Assert.Equal(3, StructuredOutputResolver.CountNumberedTableRows(md));
    }

    [Fact]
    public void CountNumberedTableRows_HandlesEdgeCases()
    {
        Assert.Equal(0, StructuredOutputResolver.CountNumberedTableRows(""));
        Assert.Equal(0, StructuredOutputResolver.CountNumberedTableRows("just prose without any tables"));
        Assert.Equal(0, StructuredOutputResolver.CountNumberedTableRows("| not | a | digit |"));
        Assert.Equal(1, StructuredOutputResolver.CountNumberedTableRows("| 7 | only one row |"));
        // CRLF survives the line splitter:
        Assert.Equal(2, StructuredOutputResolver.CountNumberedTableRows("| 1 | a |\r\n| 2 | b |\r\n"));
    }

    [Fact]
    public async System.Threading.Tasks.Task WarnsOnZeroInvention_WhenJsonClaimsZeroButFileHasRows()
    {
        // Reproduces the round-36 attempt-03 regression: Supervisor persists a
        // FINAL_SUMMARY-a-shaped JSON declaring `commands_table_rows: 0` while
        // /output/01-architecture-analysis.md actually has 113 numbered rows.
        // The runner cannot safely auto-correct — that would mask other bugs —
        // but it MUST surface the disagreement loudly so the run is
        // diagnosable from the runner-output log alone.
        var captureLogger = new Orkeon.Application.Tests.Fixtures.TestLogger<StructuredOutputResolver>();
        var resolver = new StructuredOutputResolver(_fs, captureLogger);

        // Seed the markdown ground truth at the virtual path that the JSON
        // payload's `document` field will reference.
        const string markdown = """
            # 01-architecture-analysis

            ## 4. User Command Surface

            | # | Command | Handler FQN |
            |---|---|---|
            | 1 | addDir | `/src/.../addDir` |
            | 2 | clear  | `/src/.../clear`  |
            | 3 | doctor | `/src/.../doctor` |
            """;
        await _fs.WriteAllTextAsync("/output/01-architecture-analysis.md", markdown, CancellationToken.None);

        var task = BuildTask("/output/FINAL_SUMMARY.json");
        const string supervisorEmittedJson = """
            {
              "document": "/output/01-architecture-analysis.md",
              "overall_status": "failed",
              "commands_table_rows": 0,
              "commands_total_declared": 0,
              "commands_certainty": "unknown"
            }
            """;

        var result = await resolver.ResolveAsync(task, supervisorEmittedJson, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.True(captureLogger.HasLoggedWarning(),
            "Supervisor wrote commands_table_rows=0 against a markdown that has 3 rows — runner must log a warning.");
        Assert.True(captureLogger.HasLoggedMessage("zero-invention"),
            "Warning text must mention the 'zero-invention' pattern so the run is searchable in logs.");
        Assert.True(captureLogger.HasLoggedMessage("3 numbered table rows"),
            "Warning must include the actual row count so the operator sees the magnitude of the lie.");
    }

    [Fact]
    public async System.Threading.Tasks.Task SilentOnZeroInvention_WhenFileGenuinelyHasNoRows()
    {
        // Honest 0/0: the agent correctly reports an empty table on a file
        // with no numbered rows. No warning should fire.
        var captureLogger = new Orkeon.Application.Tests.Fixtures.TestLogger<StructuredOutputResolver>();
        var resolver = new StructuredOutputResolver(_fs, captureLogger);

        await _fs.WriteAllTextAsync("/output/empty-table.md",
            "# Doc\n\nNo numbered rows here, just prose.\n", CancellationToken.None);

        var task = BuildTask("/output/FINAL_SUMMARY.json");
        const string honestJson = """
            {
              "document": "/output/empty-table.md",
              "overall_status": "failed",
              "commands_table_rows": 0
            }
            """;

        var result = await resolver.ResolveAsync(task, honestJson, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.False(captureLogger.HasLoggedWarning(),
            "An honest 0/0 must not trigger the zero-invention warning.");
    }

    [Fact]
    public async System.Threading.Tasks.Task SilentOnZeroInvention_WhenJsonHasNoDocumentField()
    {
        // Many other deliverables (FINAL_SUMMARY-{b,c,d}, generic structured
        // outputs) carry `commands_table_rows`-shaped fields without a
        // `document` pointer. The safeguard must stay quiet in those cases.
        var captureLogger = new Orkeon.Application.Tests.Fixtures.TestLogger<StructuredOutputResolver>();
        var resolver = new StructuredOutputResolver(_fs, captureLogger);

        var task = BuildTask("/output/no-doc.json");
        const string json = """{"commands_table_rows":0,"overall_status":"failed"}""";

        var result = await resolver.ResolveAsync(task, json, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.False(captureLogger.HasLoggedWarning(),
            "Without a `document` pointer there's no ground truth to compare to.");
    }

    [Fact]
    public async System.Threading.Tasks.Task SilentOnZeroInvention_WhenRowsValueIsNonZero()
    {
        // Defensive: the safeguard only fires on declared 0. If the agent
        // declared the right number, no warning even if it disagrees with
        // the file (other gates handle that case).
        var captureLogger = new Orkeon.Application.Tests.Fixtures.TestLogger<StructuredOutputResolver>();
        var resolver = new StructuredOutputResolver(_fs, captureLogger);

        await _fs.WriteAllTextAsync("/output/has-rows.md",
            "| 1 | a |\n| 2 | b |\n", CancellationToken.None);

        var task = BuildTask("/output/FINAL_SUMMARY.json");
        const string json = """
            {
              "document": "/output/has-rows.md",
              "commands_table_rows": 5
            }
            """;
        var result = await resolver.ResolveAsync(task, json, CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.False(captureLogger.HasLoggedWarning());
    }
}
