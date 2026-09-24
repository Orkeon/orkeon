using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// The one slug rule of the CLI and Studio (STUDIO-24): lowercase ASCII, accents dropped, one
/// dash between words, at most 64 characters cut at a word — and nothing at all when nothing
/// usable remains, because each caller owns its fallback. Two implementations used to disagree
/// on the cap, the cut and the fallback, so one name could name two different folders.
/// </summary>
public sealed class FolderSlugTests
{
    [Theory]
    [InlineData("Équipe", "equipe")]
    [InlineData("Vérificateur Web", "verificateur-web")]
    [InlineData("Crème brûlée à l'été", "creme-brulee-a-l-ete")]
    [InlineData("ÇA VA ?", "ca-va")]
    public void Accents_are_dropped_and_the_letters_they_sat_on_are_kept(string text, string expected)
    {
        Assert.Equal(expected, FolderSlug.From(text));
    }

    [Theory]
    [InlineData("RAPPORT Q3 2026", "rapport-q3-2026")]
    [InlineData("Veille 每日监控", "veille")]
    public void Ascii_letters_are_lowercased_digits_kept_and_everything_else_separates_words(string text, string expected)
    {
        Assert.Equal(expected, FolderSlug.From(text));
    }

    [Theory]
    [InlineData("veille-fournisseurs")]
    [InlineData("rapport-2026-t3")]
    [InlineData("a")]
    public void A_text_already_in_kebab_case_is_its_own_slug(string text)
    {
        Assert.Equal(text, FolderSlug.From(text));
    }

    [Theory]
    [InlineData("--Veille  __  fournisseurs--", "veille-fournisseurs")]
    [InlineData("  Ma veille quotidienne  ", "ma-veille-quotidienne")]
    [InlineData("Suivi\tdes\r\nfactures", "suivi-des-factures")]
    public void A_run_of_separators_is_one_dash_and_none_leads_or_trails(string text, string expected)
    {
        Assert.Equal(expected, FolderSlug.From(text));
    }

    /// <summary>
    /// Nothing, not a fallback: a forge session falls back on its timestamp, a team or an
    /// agent on <see cref="FolderSlug.TeamFallback"/>, and a rule that picked one for them
    /// would be wrong for the other. A name written in a non-Latin script lands here — zh-Hans
    /// is one of the five languages of Studio's interface.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    [InlineData("— … —")]
    [InlineData("每日监控")]
    [InlineData("Команда")]
    public void A_text_without_an_ascii_letter_or_digit_has_no_slug(string? text)
    {
        Assert.Null(FolderSlug.From(text));
    }

    [Fact]
    public void A_word_that_ends_exactly_at_the_cap_is_kept_whole()
    {
        var goal = "Résumer en une seule exécution les nouveautés d'un site web dont les "
            + "fichiers sont sauvegardés dans le sous-dossier new de c:\\documents sur le PC "
            + "de l'utilisateur et produire un document de synthèse clair et lisible";

        var slug = FolderSlug.From(goal);

        Assert.Equal("resumer-en-une-seule-execution-les-nouveautes-d-un-site-web-dont", slug);
        Assert.Equal(FolderSlug.MaxLength, slug!.Length);
    }

    [Fact]
    public void A_word_that_crosses_the_cap_is_dropped_whole()
    {
        var need = "Résumer chaque matin les offres d'emploi publiées sur les sites des cabinets de recrutement";

        Assert.Equal("resumer-chaque-matin-les-offres-d-emploi-publiees-sur-les-sites", FolderSlug.From(need));
    }

    /// <summary>
    /// The cut backs up to the previous dash only while that keeps at least half the cap: a
    /// single overlong word is cut where the cap falls rather than shrinking the slug to the
    /// few characters before it.
    /// </summary>
    [Fact]
    public void An_overlong_word_is_cut_where_the_cap_falls()
    {
        Assert.Equal(new string('a', FolderSlug.MaxLength), FolderSlug.From(new string('a', 100)));
        Assert.Equal("ab-" + new string('c', FolderSlug.MaxLength - 3), FolderSlug.From("ab " + new string('c', 100)));
    }

    [Fact]
    public void No_slug_exceeds_the_cap_or_carries_a_stray_dash()
    {
        for (var words = 1; words <= 60; words++)
        {
            var text = string.Join(" ", Enumerable.Range(0, words).Select(i => new string((char)('a' + (i % 26)), 1 + (i % 9))));

            var slug = FolderSlug.From(text)!;

            Assert.True(slug.Length <= FolderSlug.MaxLength, $"{slug.Length} characters for {words} words");
            Assert.False(slug.StartsWith('-') || slug.EndsWith('-'), slug);
            Assert.DoesNotContain("--", slug, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_team_fallback_is_a_slug_of_its_own_rule()
    {
        Assert.Equal(FolderSlug.TeamFallback, FolderSlug.From(FolderSlug.TeamFallback));
    }

    /// <summary>The corpus the CLI and Studio suites also check, read against the rule itself.</summary>
    [Theory]
    [MemberData(nameof(FolderSlugCorpus.Entries), MemberType = typeof(FolderSlugCorpus))]
    public void The_shared_corpus_follows_the_rule(string name, string folder)
    {
        Assert.Equal(folder, FolderSlug.From(name));
    }
}
