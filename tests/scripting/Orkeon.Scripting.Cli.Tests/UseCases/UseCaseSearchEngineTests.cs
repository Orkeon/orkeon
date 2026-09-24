using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Scripting.Cli.Commands.UseCases;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// The use-case search (STUDIO-38): BM25 over the five languages, normalized on both sides
/// (D-01); the English embeddings fused in by RRF only for the languages whose policy says so,
/// loaded once, at the first search that needs them (D-02, D-03); and a fall-back to terms that
/// always says why (D-05).
/// </summary>
public sealed class UseCaseSearchEngineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_query_without_accents_finds_the_sheet_written_with_them()
    {
        using var engine = UseCaseFixtures.Engine();

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "resume des mails", Language = "fr" }, Ct);

        var best = answer.Matches[0];
        Assert.Equal(UseCaseFixtures.MailDigest, best.UseCase.Id);
        Assert.Contains("resume", best.Terms);
        Assert.Equal(UseCaseMatchReason.Terms, best.Reason);
        Assert.Null(best.Similarity);
    }

    [Fact]
    public async Task A_chinese_query_finds_its_sheet()
    {
        using var engine = UseCaseFixtures.Engine();

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "每天早上总结我的邮件" }, Ct);

        Assert.Equal("zh-Hans", answer.Language);
        Assert.Equal(UseCaseLanguageSource.Detected, answer.LanguageSource);
        Assert.Equal(UseCaseFixtures.MailDigest, answer.Matches[0].UseCase.Id);
        Assert.Contains("邮件", answer.Matches[0].Terms);
    }

    [Theory]
    [InlineData("facture fournisseur", UseCaseFixtures.InvoiceMatching)]
    [InlineData("Betrug Transaktionen", UseCaseFixtures.FraudAlerts)]
    [InlineData("incorporacion de empleados", UseCaseFixtures.Onboarding)]
    public async Task Each_language_finds_its_sheet_by_its_own_words(string text, string expected)
    {
        using var engine = UseCaseFixtures.Engine();

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = text }, Ct);

        Assert.Equal(expected, answer.Matches[0].UseCase.Id);
    }

    [Fact]
    public async Task A_match_names_the_terms_it_matched_on()
    {
        using var engine = UseCaseFixtures.Engine();

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "facture fournisseur", Language = "fr" }, Ct);

        Assert.Equal(["facture", "fournisseur"], answer.Matches[0].Terms);
    }

    /// <summary>Every sheet of the real manifest has empty texts until STUDIO-37: it must stay findable.</summary>
    [Fact]
    public async Task A_sheet_with_empty_texts_is_still_found_by_its_id_tools_and_category()
    {
        using var engine = UseCaseFixtures.Engine();

        var byId = await engine.SearchAsync(new UseCaseQuery { Text = "untitled example" }, Ct);
        var byTool = await engine.SearchAsync(new UseCaseQuery { Text = "json" }, Ct);
        var byCategory = await engine.SearchAsync(new UseCaseQuery { Text = "experimental" }, Ct);

        Assert.Equal(UseCaseFixtures.Untitled, byId.Matches[0].UseCase.Id);
        Assert.Equal(UseCaseFixtures.Untitled, byTool.Matches[0].UseCase.Id);
        Assert.Equal(UseCaseFixtures.Untitled, byCategory.Matches[0].UseCase.Id);
    }

    [Fact]
    public async Task Top_bounds_the_number_of_matches()
    {
        using var engine = UseCaseFixtures.Engine();

        // "de" is a French and a Spanish word: three of the sheets contain it.
        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "de", Language = "fr", Top = 2 }, Ct);

        Assert.Equal(2, answer.Matches.Count);
        Assert.Equal([1, 2], answer.Matches.Select(m => m.Rank));
    }

    [Fact]
    public async Task A_query_that_matches_nothing_answers_an_empty_list()
    {
        using var engine = UseCaseFixtures.Engine();

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "zzzz qqqq", Language = "en" }, Ct);

        Assert.Empty(answer.Matches);
        Assert.Equal(UseCaseSearchMode.Bm25, answer.Mode);
    }

    [Fact]
    public async Task Hybrid_mode_finds_what_only_meaning_can_find()
    {
        using var model = new FakeKeywordEmbeddingProvider();
        using var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () => model);

        // No term of the query appears in any sheet; "inbox" is only close to "email" in meaning.
        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "inbox overnight", Language = "en" }, Ct);

        Assert.Equal(UseCaseSearchMode.Hybrid, answer.Mode);
        Assert.Null(answer.Degraded);
        Assert.Equal(UseCaseFixtures.MailDigest, answer.Matches[0].UseCase.Id);
        Assert.Equal(UseCaseMatchReason.Meaning, answer.Matches[0].Reason);
        Assert.Empty(answer.Matches[0].Terms);
        Assert.Equal(1.0, answer.Matches[0].Similarity!.Value, precision: 3);
        Assert.True(answer.Matches[1].Similarity < answer.Matches[0].Similarity);
    }

    [Fact]
    public async Task A_match_by_terms_and_meaning_says_both()
    {
        using var model = new FakeKeywordEmbeddingProvider();
        using var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () => model);

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "email summary", Language = "en" }, Ct);

        Assert.Equal(UseCaseFixtures.MailDigest, answer.Matches[0].UseCase.Id);
        Assert.Equal(UseCaseMatchReason.TermsAndMeaning, answer.Matches[0].Reason);
    }

    /// <summary>
    /// D-02: the model is English, so the sheets are embedded from their English text — and a
    /// sheet with no English text yet is embedded from what names it: its id and category.
    /// </summary>
    [Fact]
    public async Task The_sheets_are_embedded_from_their_english_text()
    {
        using var model = new FakeKeywordEmbeddingProvider();
        using var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () => model);

        await engine.SearchAsync(new UseCaseQuery { Text = "email", Language = "en" }, Ct);

        Assert.Contains(model.Texts, text => text.StartsWith("Daily email digest", StringComparison.Ordinal));
        Assert.DoesNotContain(model.Texts, text => text.Contains("Résumé", StringComparison.Ordinal));
        Assert.Contains(model.Texts, text => text.Contains("untitled example", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_model_loads_once_whatever_the_number_of_queries()
    {
        var loads = 0;
        using var model = new FakeKeywordEmbeddingProvider();
        using var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () =>
        {
            loads++;
            return model;
        });

        await engine.SearchAsync(new UseCaseQuery { Text = "email digest", Language = "en" }, Ct);
        await engine.SearchAsync(new UseCaseQuery { Text = "invoice", Language = "en" }, Ct);
        await engine.SearchAsync(new UseCaseQuery { Text = "fraud", Language = "en" }, Ct);

        Assert.Equal(1, loads);
        // One batch for the sheets, then one per query: the sheets are never embedded twice.
        Assert.Equal(4, model.Batches);
    }

    [Fact]
    public async Task A_language_searched_by_terms_never_loads_the_model()
    {
        var loads = 0;
        var policy = new UseCaseSearchPolicy(new Dictionary<string, UseCaseSearchMode>(StringComparer.Ordinal)
        {
            ["fr"] = UseCaseSearchMode.Bm25,
            ["en"] = UseCaseSearchMode.Hybrid,
        });
        using var engine = new UseCaseSearchEngine(UseCaseFixtures.Catalog(), policy, () =>
        {
            loads++;
            return new FakeKeywordEmbeddingProvider();
        });

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "facture fournisseur", Language = "fr" }, Ct);

        Assert.Equal(0, loads);
        Assert.Equal(UseCaseSearchMode.Bm25, answer.Mode);
        Assert.Null(answer.Degraded);
    }

    [Fact]
    public async Task Without_the_model_the_search_falls_back_to_terms_and_says_why()
    {
        using var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () =>
            throw new UseCaseModelUnavailableException("the model files are missing"));

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "email digest", Language = "en" }, Ct);

        Assert.Equal(UseCaseSearchMode.Bm25, answer.Mode);
        Assert.NotNull(answer.Degraded);
        Assert.Contains("the model files are missing", answer.Degraded, StringComparison.Ordinal);
        Assert.Equal(UseCaseFixtures.MailDigest, answer.Matches[0].UseCase.Id);
    }

    [Fact]
    public async Task A_model_that_failed_to_load_is_not_retried_at_every_query()
    {
        var loads = 0;
        using var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () =>
        {
            loads++;
            throw new UseCaseModelUnavailableException("no model");
        });

        var first = await engine.SearchAsync(new UseCaseQuery { Text = "email", Language = "en" }, Ct);
        var second = await engine.SearchAsync(new UseCaseQuery { Text = "invoice", Language = "en" }, Ct);

        Assert.Equal(1, loads);
        Assert.NotNull(first.Degraded);
        Assert.NotNull(second.Degraded);
    }

    /// <summary>The native runtime can fail in ways no probe predicts: still terms, still said.</summary>
    [Fact]
    public async Task A_model_that_crashes_while_loading_degrades_the_same_way()
    {
        using var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () =>
            throw new DllNotFoundException("onnxruntime"));

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "email", Language = "en" }, Ct);

        Assert.Equal(UseCaseSearchMode.Bm25, answer.Mode);
        Assert.Contains("onnxruntime", answer.Degraded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_search_releases_the_model_it_loaded()
    {
        using var model = new FakeKeywordEmbeddingProvider();
        using (var engine = UseCaseFixtures.Engine(UseCaseSearchMode.Hybrid, () => model))
        {
            await engine.SearchAsync(new UseCaseQuery { Text = "email", Language = "en" }, Ct);
            Assert.False(model.Disposed);
        }

        Assert.True(model.Disposed);
    }

    [Fact]
    public async Task Without_a_language_option_an_undetected_query_uses_the_fallback_language()
    {
        using var engine = UseCaseFixtures.Engine();

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "json" }, Ct);

        Assert.Equal(UseCaseLanguages.Fallback, answer.Language);
        Assert.Equal(UseCaseLanguageSource.Default, answer.LanguageSource);
    }

    [Fact]
    public async Task The_language_option_wins_over_detection()
    {
        using var engine = UseCaseFixtures.Engine();

        var answer = await engine.SearchAsync(new UseCaseQuery { Text = "je veux un résumé", Language = "de" }, Ct);

        Assert.Equal("de", answer.Language);
        Assert.Equal(UseCaseLanguageSource.Option, answer.LanguageSource);
    }

    private static IEmbeddingProvider Unused() => throw new InvalidOperationException("unused");

    [Fact]
    public void The_default_policy_names_a_mode_for_each_of_the_five_languages()
    {
        foreach (var language in UseCaseLanguages.All)
            Assert.True(UseCaseSearchPolicy.Default.Modes.ContainsKey(language), language);

        using var engine = new UseCaseSearchEngine(UseCaseFixtures.Catalog(), UseCaseSearchPolicy.Default, Unused);
        Assert.NotNull(engine);
    }
}
