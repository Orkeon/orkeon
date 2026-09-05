using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Core.Lexical;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// Hybrid code search (PLAN phase A): the code-aware tokenizer, the BM25 index, the
/// rank fusion, the store-level behaviour, and the concurrency guarantee the RW lock
/// exists for.
/// </summary>
public class CodeTokenizerTests
{
    [Fact]
    public void Splits_camelCase_and_keeps_the_whole_identifier()
    {
        var tokens = CodeTokenizer.Tokenize("getUserById");
        Assert.Contains("get", tokens);
        Assert.Contains("user", tokens);
        Assert.Contains("by", tokens);
        Assert.Contains("id", tokens);
        // The whole identifier keeps exact-match queries strong.
        Assert.Contains("getuserbyid", tokens);
    }

    [Fact]
    public void Splits_snake_and_kebab_case()
    {
        Assert.Contains("snake", CodeTokenizer.Tokenize("snake_case_name"));
        Assert.Contains("kebab", CodeTokenizer.Tokenize("kebab-case-name"));
    }

    [Fact]
    public void Splits_ALLCAPS_runs_before_a_hump()
    {
        var tokens = CodeTokenizer.Tokenize("HTTPClient");
        Assert.Contains("http", tokens);
        Assert.Contains("client", tokens);
    }

    [Fact]
    public void Splits_paths_and_fqns_on_their_separators()
    {
        var tokens = CodeTokenizer.Tokenize("/src/math.ts::add");
        Assert.Contains("math", tokens);
        Assert.Contains("add", tokens);
        Assert.Contains("src", tokens);
    }

    [Fact]
    public void Drops_single_characters_and_blank_input()
    {
        Assert.DoesNotContain("a", CodeTokenizer.Tokenize("a + b"));
        Assert.Empty(CodeTokenizer.Tokenize("   "));
        Assert.Empty(CodeTokenizer.Tokenize(null));
    }

    [Fact]
    public void Plain_word_is_not_double_counted()
    {
        // A non-composite word must appear once, not once as part + once as whole.
        var tokens = CodeTokenizer.Tokenize("service");
        Assert.Single(tokens, t => t == "service");
    }
}

public class Bm25CodeIndexTests
{
    private static RaggableNode Node(string id, string name, string signature = "", string file = "/src/a.ts")
        => new()
        {
            Id = id,
            Kind = UniversalNodeKind.Function,
            Name = name,
            Fqn = $"{file}::{name}",
            VirtualFilePath = file,
            Range = new NodeRange(0, 0, 1, 2, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "",
            Sha256 = id,
            Signature = signature,
        };

    [Fact]
    public void Exact_identifier_query_ranks_the_exact_symbol_first()
    {
        var index = new Bm25CodeIndex();
        index.Upsert(Node("1", "getUserById"));
        index.Upsert(Node("2", "deleteUserById"));
        index.Upsert(Node("3", "renderChart"));

        var hits = index.Search("getUserById", topK: 3);
        Assert.Equal("1", hits[0].NodeId);
    }

    [Fact]
    public void Concept_query_reaches_a_camelCase_identifier()
    {
        // The whole point vs the prose tokenizer: "user id" matches getUserById.
        var index = new Bm25CodeIndex();
        index.Upsert(Node("1", "getUserById"));
        index.Upsert(Node("2", "renderChart"));

        var hits = index.Search("user id", topK: 2);
        Assert.NotEmpty(hits);
        Assert.Equal("1", hits[0].NodeId);
    }

    [Fact]
    public void Upsert_replaces_instead_of_duplicating()
    {
        // Disjoint sub-tokens on purpose: "oldName"/"newName" would share the
        // sub-token "name" and legitimately keep matching after the rename.
        var index = new Bm25CodeIndex();
        index.Upsert(Node("1", "alphaFirst"));
        index.Upsert(Node("1", "betaSecond"));

        Assert.Equal(1, index.Count);
        Assert.Empty(index.Search("alphaFirst", topK: 5));
        Assert.NotEmpty(index.Search("betaSecond", topK: 5));
    }

    [Fact]
    public void Remove_forgets_the_node_and_unknown_ids_are_a_noop()
    {
        var index = new Bm25CodeIndex();
        index.Upsert(Node("1", "getUserById"));
        index.Remove("1");
        index.Remove("ghost");
        Assert.Equal(0, index.Count);
        Assert.Empty(index.Search("getUserById", topK: 5));
    }

    [Fact]
    public void Empty_query_or_empty_index_return_empty()
    {
        var index = new Bm25CodeIndex();
        Assert.Empty(index.Search("anything", topK: 5));
        index.Upsert(Node("1", "getUserById"));
        Assert.Empty(index.Search("+++", topK: 5));
    }

    [Fact]
    public void Out_of_range_tuning_parameters_are_refused_at_construction()
    {
        // Okapi BM25 only means anything for k1 >= 0 and b in [0, 1]: a negative k1
        // inverts the term-frequency saturation, and a b outside the unit interval turns
        // length normalization into an unbounded multiplier. Either way the index keeps
        // answering, with scores no caller can interpret and no error to notice. The
        // constructor is public, so the range belongs here and not in a comment.
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25CodeIndex(k1: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25CodeIndex(b: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25CodeIndex(b: 1.1));
    }
}

public class RankFusionTests
{
    private static IReadOnlyList<(string, double)> Ranking(params string[] ids)
        => [.. ids.Select((id, i) => (id, 1.0 - (i * 0.1)))];

    [Fact]
    public void A_candidate_in_both_lists_beats_single_list_leaders()
    {
        var fused = RankFusion.Fuse(3, Ranking("a", "both"), Ranking("b", "both"));
        Assert.Equal("both", fused[0].Id);
        // Origins: present in list 0 (bit 1) and list 1 (bit 2) → 0b11.
        Assert.Equal(0b11, fused[0].Origins);
    }

    [Fact]
    public void Single_list_candidates_carry_their_list_bit()
    {
        var fused = RankFusion.Fuse(3, Ranking("a"), Ranking("b"));
        Assert.Equal(0b01, fused.Single(f => f.Id == "a").Origins);
        Assert.Equal(0b10, fused.Single(f => f.Id == "b").Origins);
    }

    [Fact]
    public void Scores_depend_on_rank_not_input_scores()
    {
        // RRF is a rank aggregate: rank 1 contributes 1/61 whatever the raw score was.
        var fused = RankFusion.Fuse(1, [("x", 999.0)]);
        Assert.Equal(1.0 / 61, fused[0].Score, precision: 10);
    }

    [Fact]
    public void TopK_trims_after_fusion()
    {
        var fused = RankFusion.Fuse(1, Ranking("a", "b", "c"), Ranking("c"));
        Assert.Single(fused);
        Assert.Equal("c", fused[0].Id); // in both lists → highest fused score
    }
}

public class StoreConcurrencyTests
{
    private static RaggableNode Node(int i) => new()
    {
        Id = $"n{i}",
        Kind = UniversalNodeKind.Function,
        Name = $"symbol{i}",
        Fqn = $"/src/f{i}.ts::symbol{i}",
        VirtualFilePath = $"/src/f{i}.ts",
        Range = new NodeRange(0, 0, 1, 2, 0),
        Level = NodeLevel.L3_Symbol,
        Language = "typescript",
        SourceSnippet = "",
        Sha256 = $"sha{i}",
    };

    [Fact]
    public async Task Search_during_AddNodes_never_throws()
    {
        // THE crash the RW lock exists for: the store used to be plain Dictionaries
        // mutated in place by incremental reindex while a concurrent search enumerated
        // them — InvalidOperationException (collection modified) under real agent load
        // (one ticket edits, another searches, same process, same store).
        var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var added = 0;
        var searches = 0;
        var widestPage = 0;

        // do/while on both sides: each task performs at least one operation, so the
        // assertions below cannot pass vacuously on a starved machine.
        var writer = Task.Run(() =>
        {
            var i = 0;
            do
            {
                store.AddNodes([Node(i++)]);
                if (i % 50 == 0) store.RemoveFilesAndDescendants([$"/src/f{i - 25}.ts"]);
            }
            while (!stop.IsCancellationRequested);
            added = i;
        }, TestContext.Current.CancellationToken);

        var reader = Task.Run(async () =>
        {
            do
            {
                var hits = await store.SemanticSearchAsync(
                    new SemanticQuery { Text = "symbol", TopK = 10 },
                    CancellationToken.None);
                widestPage = Math.Max(widestPage, hits.Count);
                searches++;
            }
            while (!stop.IsCancellationRequested);
        }, TestContext.Current.CancellationToken);

        // Fails the test naturally if either task threw.
        await Task.WhenAll(writer, reader);

        // Both sides really interleaved, and every search came back within its TopK bound
        // instead of on a half-mutated index.
        Assert.True(added > 0, "the writer never added a node");
        Assert.True(searches > 0, "the reader never completed a search");
        Assert.True(widestPage <= 10, $"a search returned {widestPage} hits for TopK 10");

        // The invariant the RW lock exists for: not one write was lost to the concurrent
        // reads. The writer added `added` nodes and removed exactly one every 50 (an
        // earlier one, by exact path, with no descendants), so the graph holds precisely
        // that many - a lost update or a double insert moves this number.
        var surviving = store.ExportSnapshot().Nodes;
        Assert.Equal(added - (added / 50), surviving.Count);
    }
}
