using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// Minimal <see cref="IRaggableStore"/> stub keyed only on <c>GetAsync</c>. Returns a
/// placeholder <see cref="RaggableNode"/> for known FQNs and <c>null</c> for the rest.
/// </summary>
internal sealed class StubFqnStore : IRaggableStore
{
    private readonly HashSet<string> _known;

    public StubFqnStore(params string[] knownFqns) =>
        _known = new HashSet<string>(knownFqns, StringComparer.Ordinal);

    public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct)
    {
        if (!_known.Contains(fqn))
            return System.Threading.Tasks.Task.FromResult<RaggableNode?>(null);

        return System.Threading.Tasks.Task.FromResult<RaggableNode?>(MakeNode(fqn));
    }

    /// <summary>
    /// Suffix-match the trailing symbol after the last <c>::</c> across the known set.
    /// Mirrors <c>InMemoryRaggableStore.FindByLocalNameAsync</c> semantics.
    /// </summary>
    public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct)
    {
        var hits = _known
            .Where(f =>
            {
                var idx = f.LastIndexOf("::", StringComparison.Ordinal);
                return idx >= 0 && f.AsSpan(idx + 2).SequenceEqual(localName);
            })
            .Select(MakeNode)
            .ToList();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<RaggableNode>>(hits);
    }

    private static RaggableNode MakeNode(string fqn) => new()
    {
        Id = fqn,
        Fqn = fqn,
        Name = fqn,
        Level = NodeLevel.L3_Symbol,
        Kind = UniversalNodeKind.Function,
        VirtualFilePath = "/virtual/stub.ts",
        Range = new NodeRange(0, 0, 0, 0, 0),
        Language = "typescript",
        SourceSnippet = string.Empty,
        Sha256 = string.Empty,
    };

    // ── unused ──
    public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct) => throw new NotImplementedException();
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
    public Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct) => throw new NotImplementedException();
    public IReadOnlyList<IndexedRoot> GetIndexedRoots() => throw new NotImplementedException();
}

public class InlineFqnValidatorTests
{
    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_AllFqnsKnown_ReturnsEmptyUnknown()
    {
        var store = new StubFqnStore("inngest-js::Execution::Client");
        var sut = new InlineFqnValidator(store);

        const string content = """
            # Analysis

            See inngest-js::Execution::Client for details.
            """;

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.Single(result.ExtractedFqns);
        Assert.True(result.UnknownFqns.IsEmpty);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_UnknownFqn_IsSurfaced()
    {
        var store = new StubFqnStore(); // everything unknown
        var sut = new InlineFqnValidator(store);

        const string content = "Refer to /src::FakeModule::FakeType in prose.";

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.Contains("/src::FakeModule::FakeType", result.UnknownFqns);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_FqnInsideFencedCode_IsIgnored()
    {
        // Ground-truth: store considers the FQN unknown. If the scan were to hit it
        // through the code fence, UnknownFqns would contain it.
        var store = new StubFqnStore();
        var sut = new InlineFqnValidator(store);

        const string content = """
            # Rapport

            ```typescript
            // FQN  : inngest-js::Execution::Client
            class Client {}
            ```
            """;

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.True(result.ExtractedFqns.IsEmpty);
        Assert.True(result.UnknownFqns.IsEmpty);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_TextWithoutFqns_ReturnsEmpty()
    {
        var store = new StubFqnStore();
        var sut = new InlineFqnValidator(store);

        const string content = "Pure prose with no qualified names in sight.";

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.True(result.ExtractedFqns.IsEmpty);
        Assert.True(result.UnknownFqns.IsEmpty);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_MixedKnownAndUnknown_SurfacesOnlyUnknown()
    {
        var store = new StubFqnStore("inngest-js::Execution::Client");
        var sut = new InlineFqnValidator(store);

        const string content = """
            - inngest-js::Execution::Client — valid
            - inngest-js::Missing::Symbol — fabricated
            - @scope/pkg::Foo::Bar — also fabricated
            """;

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.Equal(3, result.ExtractedFqns.Length);
        Assert.Equal(2, result.UnknownFqns.Length);
        Assert.DoesNotContain("inngest-js::Execution::Client", result.UnknownFqns);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_AbbreviatedFqnAnchoredByFencedLongForm_IsSuppressed()
    {
        // R29 false-positive pattern: a "Key symbols" summary table in prose mentions
        // `ts::Inngest`, while the canonical long form `/src::inngest::Inngest` lives
        // in a fenced `markdown_ready_block` (which CitationBlockValidator owns).
        // The validator must NOT flag the abbreviated form as unknown when the long
        // form anchoring it exists somewhere in the document.
        var store = new StubFqnStore(); // both forms unknown to the index stub
        var sut = new InlineFqnValidator(store);

        const string content = """
            # Synthesis

            | Symbol | Role |
            |--------|------|
            | ts::Inngest | client factory |

            ```typescript
            // FQN  : /src::inngest::Inngest
            export class Inngest {}
            ```
            """;

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.Contains("ts::Inngest", result.ExtractedFqns);
        Assert.DoesNotContain("ts::Inngest", result.UnknownFqns);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_AbbreviatedFqnWithoutAnchor_IsStillFlagged()
    {
        // Negative control: when no long-form anchor exists in the document, the
        // abbreviated form is genuinely unknown and must remain flagged.
        var store = new StubFqnStore();
        var sut = new InlineFqnValidator(store);

        const string content = """
            # Synthesis

            | Symbol | Role |
            |--------|------|
            | ts::PhantomSymbol | fabricated reference |
            """;

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.Contains("ts::PhantomSymbol", result.UnknownFqns);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_AbbreviatedFqnAnchoredByFilePathLongForm_IsSuppressed()
    {
        // R31 false-positive pattern: synthesizer cites short kind-prefixed forms
        // (`ts::Scene`) in prose while the canonical long form lives in a fenced
        // `markdown_ready_block` as a file-path-style FQN
        // (`/src/packages/element/src/Scene.ts::Scene`). The `.ts` extension splits
        // the prefix walk-back if `.` is treated as a non-identifier — leaving the
        // perceived prefix as `ts`, which matches the unknown's prefix and fails
        // to anchor. Including `.` in IsIdentifierChar lets the walk reach
        // `/src/packages/element/src/Scene.ts`, a clearly distinct prefix.
        var store = new StubFqnStore();
        var sut = new InlineFqnValidator(store);

        const string content = """
            # Synthesis

            The `ts::Scene` symbol owns the canvas state.

            ```typescript
            // FQN  : /src/packages/element/src/Scene.ts::Scene
            export class Scene {}
            ```
            """;

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.Contains("ts::Scene", result.ExtractedFqns);
        Assert.DoesNotContain("ts::Scene", result.UnknownFqns);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_BareFqnWithSingleMatch_IsAutoRewritten()
    {
        // Bare-form `ts::MySqlDialect` resolves uniquely against the long form already in
        // the index; the validator must rewrite it (drop from UnknownFqns, surface in Rewrites).
        var store = new StubFqnStore("/src/drizzle-orm/src/mysql-core/dialect.ts::MySqlDialect");
        var sut = new InlineFqnValidator(store);

        const string content = "The dialect is implemented by ts::MySqlDialect.";

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.DoesNotContain("ts::MySqlDialect", result.UnknownFqns);
        Assert.True(result.Rewrites.ContainsKey("ts::MySqlDialect"));
        Assert.Equal(
            "/src/drizzle-orm/src/mysql-core/dialect.ts::MySqlDialect",
            result.Rewrites["ts::MySqlDialect"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_BareFqnWithMultipleMatches_IsListedAsAmbiguous()
    {
        // xstate-style scenario: `ts::StateMachine` matches several canonical FQNs
        // (e.g. one in `core`, one in `inspect`). The validator must surface every
        // candidate so operators can disambiguate without a second tool call.
        var store = new StubFqnStore(
            "/src/xstate/src/StateMachine.ts::StateMachine",
            "/src/xstate/src/inspect/StateMachine.ts::StateMachine");
        var sut = new InlineFqnValidator(store);

        const string content = "The runtime is driven by ts::StateMachine.";

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.DoesNotContain("ts::StateMachine", result.UnknownFqns);
        Assert.False(result.Rewrites.ContainsKey("ts::StateMachine"));

        var ambiguous = Assert.Single(result.Ambiguous);
        Assert.Equal("ts::StateMachine", ambiguous.BareFqn);
        Assert.Equal(2, ambiguous.Candidates.Length);
        Assert.Contains("/src/xstate/src/StateMachine.ts::StateMachine", ambiguous.Candidates);
        Assert.Contains("/src/xstate/src/inspect/StateMachine.ts::StateMachine", ambiguous.Candidates);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_BareFqnWithNoMatch_RemainsUnknown()
    {
        // Sanity: bare form that doesn't resolve via local-name lookup either stays in
        // UnknownFqns and is not silently rewritten.
        var store = new StubFqnStore("/src/somewhere/else.ts::DifferentSymbol");
        var sut = new InlineFqnValidator(store);

        const string content = "Mentions ts::PhantomSymbol nowhere else in the index.";

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.Contains("ts::PhantomSymbol", result.UnknownFqns);
        Assert.False(result.Rewrites.ContainsKey("ts::PhantomSymbol"));
        Assert.Empty(result.Ambiguous);
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateAsync_AbbreviationsAnchoredByOtherProseFqns_AreSuppressed()
    {
        // Same-prose anchoring: the abbreviated `ts::Inngest` is anchored by
        // `inngest-js::Execution::Inngest` appearing elsewhere in prose (no fence
        // needed). Both share the same trailing `Inngest` symbol; the longer one
        // is the real reference, the short one is a known-symbol abbreviation.
        var store = new StubFqnStore("inngest-js::Execution::Inngest");
        var sut = new InlineFqnValidator(store);

        const string content = """
            See inngest-js::Execution::Inngest for the canonical class.
            Summary entries reference `ts::Inngest` for brevity.
            """;

        var result = await sut.ValidateAsync(content, CancellationToken.None);

        Assert.DoesNotContain("ts::Inngest", result.UnknownFqns);
    }
}
