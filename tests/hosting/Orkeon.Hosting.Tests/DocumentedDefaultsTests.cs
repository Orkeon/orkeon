namespace Orkeon.Hosting.Tests;

/// <summary>
/// A default a page quotes beside a key is the one of the code. The reference page takes its
/// defaults from the settings catalogue; every other page that says "`Llm:MaxRetries` (10 by
/// default)" copied it by hand, and nothing said so when the engine's changed. Every such quote —
/// in the prose, and in the tables that have a "Default" column — is found and held to the
/// catalogue, in English and in French; the quotes that name something else than the setting of
/// that name are listed here, each with why.
/// </summary>
public sealed class DocumentedDefaultsTests
{
    /// <summary>
    /// The quotes that are not about the key the catalogue knows by that name: the page, what it
    /// writes, and what it is speaking of.
    /// </summary>
    private static readonly (string Page, string Written, string Quoted, string Why)[] s_notThatSetting =
    [
        ("docs/architecture/rag-pipeline.md", "BatchSize", "16",
            "the option of the ONNX reranker, set in C# by AddOrkeonOnnxReranker and bound on no section — not Orkeon:Embeddings:BatchSize"),
        ("docs/fr/architecture/rag-pipeline.md", "BatchSize", "16",
            "the option of the ONNX reranker, set in C# by AddOrkeonOnnxReranker and bound on no section — not Orkeon:Embeddings:BatchSize"),
    ];

    private static SettingsCatalog Catalog => SettingsCatalog.Complete;

    [Fact]
    public void A_default_a_page_quotes_beside_a_key_is_the_one_of_the_code()
    {
        var problems = new List<string>();
        var quotes = 0;
        var excepted = new HashSet<(string, string, string)>();
        foreach (var page in DocumentationPages.All())
        {
            foreach (var quote in DocumentedDefaults.In(DocumentationPages.Text(page), Catalog))
            {
                if (s_notThatSetting.Any(exception => exception.Page == page && exception.Written == quote.Written && exception.Quoted == quote.Quoted.Trim('`')))
                {
                    excepted.Add((page, quote.Written, quote.Quoted.Trim('`')));
                    continue;
                }

                quotes++;
                if (quote.Agrees)
                    continue;

                var code = DocumentedDefaults.CodeDefault(quote.Entry) is { } value ? $"`{value}`" : "none a page can quote";
                problems.Add(
                    $"{page}:{quote.Line}: `{quote.Written}` is given the default {quote.Quoted}; the code's is {code} ({quote.Entry.Path}). " +
                    $"Correct the page, or link to the key's row instead of copying it: docs/reference/configuration.md#{SettingsReference.Anchor(quote.Entry.Section)}.");
            }
        }

        Assert.True(
            problems.Count == 0,
            "A page quotes a default the code does not have (a quote that is about something else than the setting of that name " +
            $"is listed in {nameof(DocumentedDefaultsTests)}, with why):\n" + string.Join("\n", problems));
        // A pattern that stops matching would turn this control into a silent pass.
        Assert.True(quotes >= 60, $"{quotes} quoted defaults were found.");
        // And an exception whose quote is gone is removed with it.
        Assert.Equal(s_notThatSetting.Length, excepted.Count);
    }

    [Fact]
    public void A_quote_is_found_in_the_prose_and_in_a_table_and_not_in_what_the_catalogue_produces()
    {
        const string Page = """
            The budget is `Llm:MaxRetries` (10 by default), then `QueueLimit` (default `7`) holds, and
            `ResolveSymlinks`, default `true`. `Enabled` (default `true`) could be any key.

            | Key | Default | Meaning |
            |---|---|---|
            | `Orkeon:Rag:Rerank:TopN` | 5 | Chunks kept |
            | `Orkeon:Rag:Retrieval:MinScore` | none | No floor |
            | `Orkeon:Rag:Profile` | built-in | The preset |

            <!-- settings:RateLimiting -->
            | Key | Type | Default | Meaning |
            |---|---|---|---|
            | `RateLimiting:QueueLimit` | integer | `9` | Produced, not written |
            <!-- /settings -->

            ```json
            { "note": "`Llm:MaxRetries` (3 by default)" }
            ```
            """;

        var quotes = DocumentedDefaults.In(Page, Catalog);

        Assert.Equal(
            [
                ("Llm:MaxRetries", "10", true),
                ("RateLimiting:QueueLimit", "`7`", false),
                ("PathSecurity:ResolveSymlinks", "`true`", true),
                ("Orkeon:Rag:Rerank:TopN", "5", true),
                ("Orkeon:Rag:Profile", "built-in", false),
            ],
            quotes.Select(quote => (quote.Entry.Path, quote.Quoted, quote.Agrees)));
        Assert.Equal([1, 1, 2, 6, 8], quotes.Select(quote => quote.Line));
    }

    [Theory]
    [InlineData("PathSecurity:MaxFileSizeBytes", "50 MB", true)]
    [InlineData("PathSecurity:MaxFileSizeBytes", "52,428,800", true)]
    [InlineData("PathSecurity:MaxFileSizeBytes", "50", false)]
    [InlineData("Orkeon:Rag:WebFallback:Timeout", "10 s", true)]
    [InlineData("Orkeon:Rag:WebFallback:Timeout", "`00:00:10`", true)]
    [InlineData("Orkeon:Rag:WebFallback:Timeout", "30 s", false)]
    [InlineData("Orkeon:Rag:Profile", "`FAST`", true)]
    [InlineData("Llm:Model", "`gpt`", false)]
    public void A_quote_agrees_whatever_the_unit_and_the_separators_it_is_written_with(string key, string quoted, bool agrees) =>
        Assert.Equal(agrees, DocumentedDefaults.Same(Catalog.Setting(key)!, quoted));
}
