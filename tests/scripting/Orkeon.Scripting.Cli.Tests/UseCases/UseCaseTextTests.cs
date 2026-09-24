using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// The normalization shared by the indexed text and the query (STUDIO-38 D-01). BM25 matches
/// terms by exact spelling and its own tokenizer neither folds accents nor segments Chinese, so
/// whatever the index and the query disagree on here is a match that silently never happens.
/// </summary>
public sealed class UseCaseTextTests
{
    [Fact]
    public void Resume_and_résumé_are_one_term()
    {
        Assert.Equal(UseCaseText.Tokenize("resume"), UseCaseText.Tokenize("Résumé"));
        Assert.Equal(["resume"], UseCaseText.Tokenize("RÉSUMÉ"));
    }

    [Theory]
    [InlineData("Tägliche Zusammenfassung", new[] { "tagliche", "zusammenfassung" })]
    [InlineData("Straße", new[] { "strasse" })]
    [InlineData("Cada mañana", new[] { "cada", "manana" })]
    [InlineData("Cœur d'œuvre", new[] { "coeur", "d", "oeuvre" })]
    [InlineData("Façade à l'intégration", new[] { "facade", "a", "l", "integration" })]
    public void Case_and_accents_fold_in_every_latin_language(string text, string[] expected)
    {
        Assert.Equal(expected, UseCaseText.Tokenize(text));
    }

    [Fact]
    public void A_chinese_run_becomes_character_bigrams()
    {
        Assert.Equal(["每日", "日邮", "邮件", "件摘", "摘要"], UseCaseText.Tokenize("每日邮件摘要"));
    }

    [Fact]
    public void A_lone_chinese_character_stays_a_term()
    {
        Assert.Equal(["2024", "年"], UseCaseText.Tokenize("2024年"));
    }

    [Fact]
    public void Latin_letters_and_chinese_split_at_the_script_boundary()
    {
        Assert.Equal(["csv", "数据", "据报", "报表"], UseCaseText.Tokenize("CSV数据报表"));
    }

    [Fact]
    public void Chinese_punctuation_and_full_width_forms_separate_terms()
    {
        Assert.Equal(["邮件", "摘要"], UseCaseText.Tokenize("邮件，摘要。"));
        Assert.Equal(["pdf"], UseCaseText.Tokenize("ＰＤＦ"));
    }

    [Fact]
    public void Tool_names_and_ids_split_into_words()
    {
        Assert.Equal(["email", "parser"], UseCaseText.Tokenize("email_parser"));
        Assert.Equal(["03", "email", "pipeline"], UseCaseText.Tokenize("03-email-pipeline"));
    }

    [Fact]
    public void Blank_text_has_no_term()
    {
        Assert.Empty(UseCaseText.Tokenize(null));
        Assert.Empty(UseCaseText.Tokenize("  ,;  "));
    }

    /// <summary>
    /// The indexed text goes through <c>Bm25Index</c>'s own tokenizer after ours: the normalized
    /// form must survive it unchanged, or the two would disagree again one layer down.
    /// </summary>
    [Fact]
    public void The_normalized_form_is_the_terms_separated_by_spaces()
    {
        Assert.Equal("resume quotidien 每日 日邮 邮件", UseCaseText.Normalize("Résumé quotidien — 每日邮件"));
    }
}
