using Xunit;

namespace Orkeon.Tests.Shared.UseCases;

/// <summary>
/// Texts and the search terms each one becomes (STUDIO-38 D-01), checked by both suites that
/// normalize a use-case text: the CLI's search, which indexes the sheets and reads the queries,
/// and Studio's, which counts how many sheets share the terms the CLI answers with (STUDIO-39).
/// The two sides cannot reference each other, and a term they spell differently is a suggestion
/// that silently never comes — so an entry here is a promise both keep.
/// </summary>
public static class UseCaseTermsCorpus
{
    /// <summary>The text, then its terms in order.</summary>
    public static TheoryData<string, string[]> Entries => new()
    {
        { "Résumé", ["resume"] },
        { "RÉSUMÉ quotidien", ["resume", "quotidien"] },
        { "Tägliche Zusammenfassung", ["tagliche", "zusammenfassung"] },
        { "Straße", ["strasse"] },
        { "Cada mañana", ["cada", "manana"] },
        { "Cœur d'œuvre", ["coeur", "d", "oeuvre"] },
        { "Façade à l'intégration", ["facade", "a", "l", "integration"] },
        { "Ørsted Æble", ["orsted", "aeble"] },
        { "每日邮件摘要", ["每日", "日邮", "邮件", "件摘", "摘要"] },
        { "2024年", ["2024", "年"] },
        { "CSV数据报表", ["csv", "数据", "据报", "报表"] },
        { "邮件，摘要。", ["邮件", "摘要"] },
        { "ＰＤＦ", ["pdf"] },
        { "email_parser", ["email", "parser"] },
        { "03-email-pipeline", ["03", "email", "pipeline"] },
        { "Je veux un résumé de mes e-mails, chaque matin !", ["je", "veux", "un", "resume", "de", "mes", "e", "mails", "chaque", "matin"] },
        { "  ,;  ", [] },
    };
}
