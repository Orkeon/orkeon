using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.UseCases;

namespace Orkeon.Studio.Core.Tests.UseCases;

/// <summary>
/// What Studio reads off the catalogue and off an answer: a title in the UI's language (Chinese
/// included), the sheets as the CLI lists them, and which matches are close enough to a need to be
/// suggested — how many use cases carry a term is read off the answer, never recomputed here.
/// </summary>
public sealed class UseCaseCatalogTests
{
    private static UseCaseCatalog Read(string line)
    {
        Assert.True(OrkeonEventParser.TryParse(line, out var orkeonEvent));
        Assert.True(UseCaseCatalog.TryRead(orkeonEvent!, out var catalog));
        return catalog!;
    }

    /// <summary>A catalogue of <paramref name="size"/> filler sheets, then the given ones.</summary>
    private static UseCaseCatalog Padded(int size, params UseCase[] sheets) => new(
    [
        .. sheets,
        .. Enumerable.Range(0, size - sheets.Length).Select(i => new UseCase
        {
            Id = $"{i:000}-filler",
            Category = "01-enterprise",
            Title = new Dictionary<string, string> { ["fr"] = "Un exemple de la liste", ["en"] = "An example of the list" },
        }),
    ]);

    private static UseCase Sheet(string id, string frenchTitle) => new()
    {
        Id = id,
        Category = "01-enterprise",
        Title = new Dictionary<string, string> { ["fr"] = frenchTitle },
    };

    [Theory]
    [InlineData("fr", "Tri des e-mails")]
    [InlineData("zh", "邮件分拣")]
    [InlineData("zh-Hans", "邮件分拣")]
    [InlineData("ZH", "邮件分拣")]
    [InlineData("de", "Mail sorting")]   // not written in German: English first
    [InlineData(null, "Mail sorting")]
    [InlineData("pt", "Mail sorting")]
    public void A_title_is_read_in_the_ui_language_and_falls_back_on_english(string? language, string expected)
    {
        var sheet = new UseCase
        {
            Id = "03-email-pipeline",
            Category = "01-enterprise",
            Title = new Dictionary<string, string>
            {
                ["fr"] = "Tri des e-mails",
                ["en"] = "Mail sorting",
                ["de"] = " ",
                ["zh-Hans"] = "邮件分拣",
            },
        };

        Assert.Equal(expected, sheet.TitleIn(language));
    }

    [Fact]
    public void Studios_zh_is_the_catalogues_zh_hans()
    {
        Assert.Equal("zh-Hans", UseCaseLanguages.FromUiLanguage("zh"));
        Assert.Equal("fr", UseCaseLanguages.FromUiLanguage("FR"));
        Assert.Equal("en", UseCaseLanguages.FromUiLanguage(""));
    }

    [Fact]
    public void A_sheet_that_cannot_be_read_is_skipped_and_the_rest_of_the_catalogue_kept()
    {
        var line = UseCaseLines.Catalog(
            UseCaseLines.Sheet("03-email-pipeline", "01-enterprise"),
            UseCaseLines.Sheet("", "01-enterprise"),
            UseCaseLines.Sheet("06-competitive-intelligence", "01-enterprise"));

        var catalog = Read(line);

        Assert.Equal(["03-email-pipeline", "06-competitive-intelligence"], catalog.UseCases.Select(u => u.Id));
        Assert.Equal(["fr", "en", "es", "de", "zh-Hans"], catalog.Languages);
    }

    /// <summary>An answer to <paramref name="query"/> for the whole catalogue: the given matches, then fillers carrying <paramref name="common"/>.</summary>
    private static UseCaseAnswer Answer(string query, UseCaseMatch[] matches, params (string Term, int Carriers)[] common)
    {
        var all = new List<UseCaseMatch>(matches);
        var filler = 0;
        foreach (var (term, carriers) in common)
        {
            for (var i = 0; i < carriers; i++)
                all.Add(Match($"{filler++:000}-filler", UseCaseMatchReason.Terms, term));
        }

        return new UseCaseAnswer
        {
            Query = query,
            Matches = [.. all.Select((match, index) => match with { Rank = index + 1 })],
        };
    }

    private static UseCaseMatch Match(string id, UseCaseMatchReason reason, params string[] terms) =>
        new() { Rank = 0, Id = id, Reason = reason, Terms = terms };

    private static UseCaseMatch Terms(string id, params string[] terms) => Match(id, UseCaseMatchReason.Terms, terms);

    private static IReadOnlyList<string> Close(UseCaseAnswer answer, UseCaseCatalog catalog) =>
        [.. UseCaseSuggestions.Close(answer, catalog).Select(match => match.Id)];

    [Fact]
    public void A_term_is_rare_when_the_answer_shows_it_in_at_most_three_use_cases_in_a_hundred()
    {
        var catalog = Padded(
            105,
            Sheet("06-competitive-intelligence", "Veille concurrentielle"),
            Sheet("20-patent-monitoring", "Veille sur les brevets"),
            Sheet("52-pharmacovigilance", "Veille des effets"),
            Sheet("77-podcast-production", "Veille audio"));
        Assert.Equal(3, UseCaseSuggestions.RareLimit(catalog.Count));

        // An answer by terms lists every sheet sharing a term: the rare one in three, the article
        // of the sentence in thirty.
        var threeCarriers = Answer(
            "une veille documentaire",
            [Terms("06-competitive-intelligence", "veille"), Terms("20-patent-monitoring", "veille"), Terms("52-pharmacovigilance", "une", "veille")],
            ("une", 30));
        Assert.Equal(["06-competitive-intelligence", "20-patent-monitoring", "52-pharmacovigilance"], Close(threeCarriers, catalog));

        // A fourth sheet carrying it, and it says nothing about the need any more.
        var fourCarriers = Answer(
            "une veille documentaire",
            [Terms("06-competitive-intelligence", "veille"), Terms("20-patent-monitoring", "veille"), Terms("52-pharmacovigilance", "veille"), Terms("77-podcast-production", "veille")],
            ("une", 30));
        Assert.Empty(Close(fourCarriers, catalog));
    }

    /// <summary>
    /// A rare short term is a keyword in a search of one or two words, and a function word in a
    /// sentence — the catalogue happens to hold «est» in two sheets, and a sentence about a sick cat
    /// must not suggest them. Chinese terms are character pairs: no floor applies to them.
    /// </summary>
    [Fact]
    public void A_short_term_counts_in_a_keyword_search_and_not_in_a_sentence_unless_it_is_chinese()
    {
        var catalog = Padded(
            105,
            Sheet("37-kyc-aml-compliance", "Conformité kyc"),
            Sheet("69-performance-analysis", "Une application qui est lente"),
            Sheet("70-versioned-documentation", "Une documentation qui est tenue"),
            Sheet("30-adaptive-summary", "Résumé adapté"));

        Assert.Equal(["37-kyc-aml-compliance"], Close(Answer("kyc", [Terms("37-kyc-aml-compliance", "kyc")]), catalog));
        Assert.Empty(Close(
            Answer("un contrôle kyc de mes clients", [Terms("37-kyc-aml-compliance", "kyc", "de")], ("de", 40)),
            catalog));
        Assert.Empty(Close(
            Answer("le chat de ma voisine est malade", [Terms("69-performance-analysis", "de", "est"), Terms("70-versioned-documentation", "de", "est")], ("de", 40)),
            catalog));
        Assert.Equal(["30-adaptive-summary"], Close(Answer("每天早上总结新闻", [Terms("30-adaptive-summary", "总结")]), catalog));
    }

    [Fact]
    public void Only_the_matches_sharing_a_distinctive_term_are_suggested()
    {
        var catalog = Padded(
            105,
            Sheet("03-email-pipeline", "Tri des mails"),
            Sheet("30-adaptive-summary", "Un résumé adapté"),
            Sheet("48-mental-health", "Suivi du bien-être"));
        var answer = Answer(
            "je veux un résumé de mes mails",
            [
                Terms("03-email-pipeline", "de", "mails"),
                Terms("000-filler", "un", "de"),
                Match("48-mental-health", UseCaseMatchReason.Meaning),
                Match("30-adaptive-summary", UseCaseMatchReason.TermsAndMeaning, "un", "resume"),
                Terms("99-not-in-the-catalogue", "mails"),
            ],
            ("de", 40),
            ("un", 20));

        Assert.Equal(["03-email-pipeline", "30-adaptive-summary"], Close(answer, catalog));
    }

    [Fact]
    public void Only_the_best_five_matches_can_be_suggested()
    {
        var catalog = Padded(105, Sheet("06-competitive-intelligence", "Veille concurrentielle"));
        var answer = Answer(
            "une veille documentaire",
            [
                Terms("001-filler", "une"),
                Terms("002-filler", "une"),
                Terms("003-filler", "une"),
                Terms("004-filler", "une"),
                Terms("005-filler", "une"),
                Terms("06-competitive-intelligence", "veille"),
            ],
            ("une", 20));

        Assert.Empty(Close(answer, catalog));
    }

    [Fact]
    public void A_nonsense_need_answered_by_meaning_alone_suggests_nothing()
    {
        var catalog = Padded(105, Sheet("16-interactive-qa", "Réponses avec sources"));
        var answer = new UseCaseAnswer
        {
            Query = "asdkjh qwe zzz",
            Matches =
            [
                new UseCaseMatch { Rank = 1, Id = "16-interactive-qa", Reason = UseCaseMatchReason.Meaning, Similarity = 0.61 },
                new UseCaseMatch { Rank = 2, Id = "000-filler", Reason = UseCaseMatchReason.Meaning, Similarity = 0.57 },
            ],
        };

        Assert.Empty(UseCaseSuggestions.Close(answer, catalog));
    }

    [Fact]
    public void An_answer_line_is_read_with_its_reasons_and_terms()
    {
        var line = UseCaseLines.Results(
            "q7",
            "invoice processing",
            UseCaseLines.Result(1, "40-invoice-processing", "terms+meaning", "invoice", "processing"),
            UseCaseLines.Result(2, "32-fraud-detection", "meaning"),
            UseCaseLines.Result(3, "35-accounting-reconciliation", "terms", "invoice"));
        Assert.True(OrkeonEventParser.TryParse(line, out var orkeonEvent));

        Assert.True(UseCaseAnswer.TryRead(orkeonEvent!, out var answer));

        Assert.Equal("q7", orkeonEvent!.CorrelationId);
        Assert.Equal("invoice processing", answer!.Query);
        Assert.Equal("bm25", answer.Mode);
        Assert.Equal(
            [UseCaseMatchReason.TermsAndMeaning, UseCaseMatchReason.Meaning, UseCaseMatchReason.Terms],
            answer.Matches.Select(match => match.Reason));
        Assert.Equal(["invoice", "processing"], answer.Matches[0].Terms);
        Assert.Equal([1, 2, 3], answer.Matches.Select(match => match.Rank));
    }
}
