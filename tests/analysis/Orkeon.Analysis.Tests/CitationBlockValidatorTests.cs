using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

// ── Hand-rolled stub ──────────────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="IRaggableStore"/> stub that serves a fixed set of
/// (fqn → SourceSlice) entries for citation-block validator tests.
/// All other store methods throw <see cref="NotImplementedException"/>.
/// </summary>
internal sealed class StubRaggableStore : IRaggableStore
{
    private readonly Dictionary<string, SourceSlice> _slices;

    public StubRaggableStore(Dictionary<string, SourceSlice> slices)
    {
        _slices = slices;
    }

    public Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct)
    {
        _slices.TryGetValue(fqn, out var slice);
        return System.Threading.Tasks.Task.FromResult<SourceSlice?>(slice);
    }

    // ── unused stubs ──

    public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
    public Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct) => throw new NotImplementedException();
    public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct) => throw new NotImplementedException();
    public Task<int> GetNodeCountAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct) => throw new NotImplementedException();
    public Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<SearchHit>> SemanticSearchAsync(SemanticQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct) => throw new NotImplementedException();
    public IReadOnlyList<IndexedRoot> GetIndexedRoots() => throw new NotImplementedException();
}

// ── Tests ─────────────────────────────────────────────────────────────────

public class CitationBlockValidatorTests
{
    // Ground-truth source for all tests that involve a real symbol.
    private const string RealSource =
        "public void DoWork() {\n" +
        "    Console.WriteLine(\"Hello\");\n" +
        "}";

    // A SHA-256 hex that starts with "abcdef12" (the real short-SHA we'll use in headers).
    private const string RealSha256Full = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
    private const string RealShortSha = "abcdef12"; // first 8 chars — matches StartsWith check

    private const string Fqn = "MyProject::MyNamespace::MyClass::DoWork";

    private static SourceSlice MakeSlice(string source, string sha256) => new()
    {
        VirtualFilePath = "/src/MyClass.cs",
        StartLine = 10,
        EndLine = 13,
        Language = "csharp",
        Source = source,
        Sha256 = sha256,
        Stable = true,
    };

    /// <summary>Builds a citation block as SymbolSourceTool.BuildCitationBlock would produce.</summary>
    private static string MakeCitationBlock(string fqn, string shortSha, string source, string lang = "csharp") =>
        $"```{lang}\n" +
        $"// FQN  : {fqn}\n" +
        $"// File : /src/MyClass.cs:10-13\n" +
        $"// SHA  : sha256:{shortSha}\n" +
        source +
        (source.EndsWith('\n') ? "```" : "\n```");

    private static CitationBlockValidator MakeValidator(string fqn, SourceSlice slice)
    {
        var store = new StubRaggableStore(new Dictionary<string, SourceSlice>
        {
            [fqn] = slice,
        });
        return new CitationBlockValidator(store);
    }

    // ── Test 1: valid SHA + exact body → IsValid == true ─────────────────

    [Fact]
    public async Task ValidateAsync_valid_sha_and_body_returns_IsValid_true()
    {
        var slice = MakeSlice(RealSource, RealSha256Full);
        var validator = MakeValidator(Fqn, slice);

        var content = $"# Report\n\n{MakeCitationBlock(Fqn, RealShortSha, RealSource)}\n\nEnd.";
        var result = await validator.ValidateAsync(content, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    // ── Test 2: fabricated SHA → IsValid == false, violation listed ───────

    [Fact]
    public async Task ValidateAsync_fake_sha_returns_IsValid_false_with_violation()
    {
        var slice = MakeSlice(RealSource, RealSha256Full);
        var validator = MakeValidator(Fqn, slice);

        // The LLM fabricated this short SHA (the classic fake from the task spec)
        const string fakeSha = "1a2b3c4d5e6f7890";
        var content = MakeCitationBlock(Fqn, fakeSha, RealSource);
        var result = await validator.ValidateAsync(content, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Violations);
        // Violation must mention the FQN and both the fake and real SHAs
        var violation = Assert.Single(result.Violations, v => v.Contains(Fqn));
        Assert.Contains(fakeSha, violation, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 3: modified body → IsValid == false ──────────────────────────

    [Fact]
    public async Task ValidateAsync_modified_body_returns_IsValid_false()
    {
        var slice = MakeSlice(RealSource, RealSha256Full);
        var validator = MakeValidator(Fqn, slice);

        // The LLM simplified the method body
        const string simplifiedBody =
            "public void DoWork() {\n" +
            "    // ... implementation ...\n" +
            "}";

        var content = MakeCitationBlock(Fqn, RealShortSha, simplifiedBody);
        var result = await validator.ValidateAsync(content, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("mismatch") && v.Contains(Fqn));
    }

    // ── Test 4: block without FQN/SHA headers → ignored (not failed) ──────

    [Fact]
    public async Task ValidateAsync_block_without_citation_headers_is_ignored()
    {
        // Store is empty — if this block were treated as a citation it would fail.
        var store = new StubRaggableStore(new Dictionary<string, SourceSlice>());
        var validator = new CitationBlockValidator(store);

        const string content =
            "Here is some code:\n\n" +
            "```csharp\n" +
            "var x = 42;\n" +
            "```\n\n" +
            "Done.";

        var result = await validator.ValidateAsync(content, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }
}
