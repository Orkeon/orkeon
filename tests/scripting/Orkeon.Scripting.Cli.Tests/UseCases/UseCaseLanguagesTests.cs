using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// The query's language decides whether meaning is fused in (STUDIO-38 D-02) and which title
/// the answer shows. <c>--lang</c> names it; without it, a sentence gives it away.
/// </summary>
public sealed class UseCaseLanguagesTests
{
    [Theory]
    [InlineData("je veux un résumé de mes mails chaque matin", "fr")]
    [InlineData("I want a summary of my emails every morning", "en")]
    [InlineData("quiero un resumen de mis correos cada mañana", "es")]
    [InlineData("Ich möchte jeden Morgen eine Zusammenfassung meiner E-Mails", "de")]
    [InlineData("每天早上总结我的邮件", "zh-Hans")]
    [InlineData("fraud detection on card payments", "en")]
    [InlineData("resume des mails", "fr")]
    public void The_language_of_a_sentence_is_detected(string text, string expected)
    {
        Assert.Equal(expected, UseCaseLanguages.Detect(text));
    }

    [Theory]
    [InlineData("kyc aml")]
    [InlineData("")]
    public void Keywords_without_a_telltale_word_are_not_detected(string text)
    {
        Assert.Null(UseCaseLanguages.Detect(text));
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("FR", "fr")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("de", "de")]
    public void A_language_option_is_read_in_any_case(string value, string expected)
    {
        Assert.True(UseCaseLanguages.TryParse(value, out var language));
        Assert.Equal(expected, language);
    }

    [Theory]
    [InlineData("it")]
    [InlineData("french")]
    [InlineData("")]
    public void A_language_outside_the_five_is_refused(string value)
    {
        Assert.False(UseCaseLanguages.TryParse(value, out _));
    }

    [Fact]
    public void The_five_languages_keep_the_manifest_order()
    {
        Assert.Equal(["fr", "en", "es", "de", "zh-Hans"], UseCaseLanguages.All);
    }
}
