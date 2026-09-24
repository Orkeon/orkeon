using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.UseCases;
using Orkeon.Tests.Shared.UseCases;

namespace Orkeon.Studio.Core.Tests.UseCases;

/// <summary>
/// What Studio reads off the catalogue besides the sheets themselves: a title in the UI's
/// language (Chinese included), the terms a text becomes — the CLI's own spelling — and which
/// terms are rare enough to say something about a need.
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
    [MemberData(nameof(UseCaseTermsCorpus.Entries), MemberType = typeof(UseCaseTermsCorpus))]
    public void A_text_becomes_the_terms_the_cli_reads(string text, string[] expected)
    {
        Assert.Equal(expected, UseCaseTerms.Tokenize(text));
    }

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

    [Fact]
    public void A_term_is_counted_in_every_text_the_cli_indexes()
    {
        var catalog = new UseCaseCatalog(
        [
            new UseCase
            {
                Id = "40-invoice-processing",
                Category = "03-finance-trading",
                Title = new Dictionary<string, string> { ["fr"] = "Contrôle des factures", ["zh-Hans"] = "供应商发票核对" },
                Problem = new Dictionary<string, string> { ["en"] = "Check supplier invoices" },
                Tags = ["invoices"],
                Tools = ["pdf_reader"],
            },
            Sheet("35-accounting-reconciliation", "Rapprochement des factures"),
        ]);

        Assert.Equal(2, catalog.DocumentFrequency("factures"));
        Assert.Equal(1, catalog.DocumentFrequency("controle"));   // accents folded, as the CLI answers it
        Assert.Equal(1, catalog.DocumentFrequency("发票"));
        Assert.Equal(1, catalog.DocumentFrequency("supplier"));
        Assert.Equal(1, catalog.DocumentFrequency("pdf"));         // a tool, split into words
        Assert.Equal(1, catalog.DocumentFrequency("trading"));     // the category, its number dropped
        Assert.Equal(1, catalog.DocumentFrequency("reconciliation"));   // the id
        Assert.Equal(0, catalog.DocumentFrequency("veille"));
    }

    [Fact]
    public void A_term_is_rare_when_at_most_three_use_cases_in_a_hundred_share_it()
    {
        var catalog = Padded(
            105,
            Sheet("06-competitive-intelligence", "Veille concurrentielle"),
            Sheet("20-patent-monitoring", "Veille sur les brevets"),
            Sheet("52-pharmacovigilance", "Veille des effets"),
            Sheet("12-onboarding", "Accueil d'une recrue"));

        Assert.Equal(3, catalog.RareLimit);
        Assert.True(catalog.IsRare("veille"));        // three sheets of 105
        Assert.True(catalog.IsRare("recrue"));        // one
        Assert.False(catalog.IsRare("exemple"));      // every filler sheet
        Assert.False(catalog.IsRare("chat"));         // no sheet: a term Studio cannot vouch for
    }

    /// <summary>
    /// A rare short term is a keyword in a search of one or two words, and a function word in a
    /// sentence — the catalogue happens to hold «est» in three sheets, and a sentence about a sick
    /// cat must not suggest them. Chinese terms are character pairs: no floor applies to them.
    /// </summary>
    [Fact]
    public void A_short_term_counts_in_a_keyword_search_and_not_in_a_sentence_unless_it_is_chinese()
    {
        var catalog = Padded(
            105,
            Sheet("45-kyc-onboarding", "Contrôle kyc des clients"),
            Sheet("69-performance-analysis", "Une application qui est lente"),
            new UseCase
            {
                Id = "30-adaptive-summary",
                Category = "02-science-research",
                Title = new Dictionary<string, string> { ["zh-Hans"] = "按读者水平定制的摘要" },
            });

        Assert.Equal(["45-kyc-onboarding"], Close("kyc", Match("45-kyc-onboarding", "kyc")));
        Assert.Empty(Close("un contrôle kyc des nouveaux clients", Match("45-kyc-onboarding", "kyc")));
        Assert.Empty(Close("le chat de ma voisine est malade", Match("69-performance-analysis", "de", "est")));
        Assert.Equal(["30-adaptive-summary"], Close("每天早上总结新闻", Match("30-adaptive-summary", "读者")));

        IReadOnlyList<string> Close(string query, UseCaseMatch match) =>
            [.. UseCaseSuggestions.Close(new UseCaseAnswer { Query = query, Matches = [match] }, catalog).Select(close => close.Id)];

        static UseCaseMatch Match(string id, params string[] terms) =>
            new() { Rank = 1, Id = id, Reason = UseCaseMatchReason.Terms, Terms = terms };
    }

    [Fact]
    public void Only_the_matches_sharing_a_distinctive_term_are_suggested()
    {
        var catalog = Padded(
            105,
            Sheet("03-email-pipeline", "Tri des mails"),
            Sheet("30-adaptive-summary", "Un résumé adapté"),
            Sheet("48-mental-health", "Suivi du bien-être"));
        var answer = new UseCaseAnswer
        {
            Query = "je veux un résumé de mes mails",
            Matches =
            [
                new UseCaseMatch { Rank = 1, Id = "03-email-pipeline", Reason = UseCaseMatchReason.Terms, Terms = ["de", "mails"] },
                new UseCaseMatch { Rank = 2, Id = "000-filler", Reason = UseCaseMatchReason.Terms, Terms = ["un", "de"] },
                new UseCaseMatch { Rank = 3, Id = "48-mental-health", Reason = UseCaseMatchReason.Meaning, Terms = [] },
                new UseCaseMatch { Rank = 4, Id = "30-adaptive-summary", Reason = UseCaseMatchReason.TermsAndMeaning, Terms = ["un", "resume"] },
                new UseCaseMatch { Rank = 5, Id = "99-not-in-the-catalogue", Reason = UseCaseMatchReason.Terms, Terms = ["mails"] },
            ],
        };

        var close = UseCaseSuggestions.Close(answer, catalog);

        Assert.Equal(["03-email-pipeline", "30-adaptive-summary"], close.Select(match => match.Id));
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
