using Orkeon.Constants.Configuration;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The tables of the settings reference are produced from the catalogue: one row per key — key,
/// type, default, meaning —, a line that says who reads the section, and an index of the
/// categories. Shown here on a catalogue of three sections written by hand.
/// </summary>
public sealed class SettingsReferenceTests
{
    private static readonly SettingsCatalog s_catalog = new(
        [
            new SettingsCatalogSection
            {
                Path = "RateLimiting",
                Category = SettingsCategories.RateAndBudgets,
                Hosts = [SettingsHosts.Cli, SettingsHosts.ServiceHost],
                Registration = "AddOrkeonInfrastructure",
            },
            new SettingsCatalogSection
            {
                Path = "ToolRateLimiting",
                Category = SettingsCategories.RateAndBudgets,
                Registration = "AddOrkeonToolRateLimiting",
            },
            new SettingsCatalogSection
            {
                Path = "MCP",
                Category = SettingsCategories.Tools,
                Hosts = [SettingsHosts.Cli],
            },
        ],
        [
            new SettingsCatalogEntry
            {
                Path = "RateLimiting:QueueLimit",
                Section = "RateLimiting",
                Type = "integer",
                Default = "5",
                Description = "How many requests wait | at most, per <provider>: see `QueueLimit|x` and `<name>`.",
            },
            new SettingsCatalogEntry
            {
                Path = "RateLimiting:ApiKey",
                Section = "RateLimiting",
                Type = "string",
                Secret = true,
                Description = "A key.",
            },
            new SettingsCatalogEntry
            {
                Path = "ToolRateLimiting:ToolSpecificLimits:<name>",
                Section = "ToolRateLimiting",
                Type = "integer",
                DefaultNote = "WebScrapeTool = 10",
                Description = "Per-tool limits.",
            },
            new SettingsCatalogEntry
            {
                Path = "MCP:Servers:<name>:Transport",
                Section = "MCP",
                Type = "enum",
                Default = "\"Stdio\"",
                Values = ["Stdio", "Sse"],
                Description = "The transport.",
            },
            new SettingsCatalogEntry
            {
                Path = "MCP:Servers:<name>:Args",
                Section = "MCP",
                Type = "list of string",
                Default = "[\n  \"a\",\n  \"b\"\n]",
                DefaultNote = "under the default profile",
                Description = "The arguments.",
            },
        ]);

    private static readonly SettingsReference s_english = new(s_catalog, SettingsReferenceLanguage.English);

    private static readonly SettingsReference s_french = new(
        s_catalog,
        SettingsReferenceLanguage.French(SettingsReferenceSentences.FromJson(
            """
            {
              "categories": { "rate-and-budgets": "Débit et budgets", "tools": "Outils" },
              "defaultNotes": { "WebScrapeTool = 10": "WebScrapeTool = 10", "under the default profile": "sous le profil par défaut" },
              "settings": {
                "RateLimiting:QueueLimit": "Combien de requêtes attendent.",
                "RateLimiting:ApiKey": "Une clé.",
                "ToolRateLimiting:ToolSpecificLimits:<name>": "Les limites par outil.",
                "MCP:Servers:<name>:Transport": "Le transport.",
                "MCP:Servers:<name>:Args": "Les arguments."
              }
            }
            """)));

    [Fact]
    public void A_section_is_who_reads_it_then_one_row_per_key() =>
        Assert.Equal(
            """
            **Read by**: `orkeon`, `orkeon-host`.

            | Key | Type | Default | Meaning |
            |---|---|---|---|
            | `ApiKey` | string, secret | — | A key. |
            | `QueueLimit` | integer | `5` | How many requests wait \| at most, per &lt;provider&gt;: see `QueueLimit\|x` and `<name>`. |

            """,
            s_english.Section("RateLimiting"),
            ignoreLineEndingDifferences: true);

    [Fact]
    public void A_section_no_shipped_binary_reads_says_so_and_names_its_registration() =>
        Assert.Equal(
            """
            **Read by**: a host written in C# only, through `AddOrkeonToolRateLimiting()` — no shipped binary reads this section.

            | Key | Type | Default | Meaning |
            |---|---|---|---|
            | `ToolSpecificLimits:<name>` | integer | WebScrapeTool = 10 | Per-tool limits. |

            """,
            s_english.Section("ToolRateLimiting"),
            ignoreLineEndingDifferences: true);

    [Fact]
    public void A_section_with_a_closed_list_of_values_gains_a_column_and_a_list_is_one_line() =>
        Assert.Equal(
            """
            **Read by**: `orkeon`.

            | Key | Type | Default | Values | Meaning |
            |---|---|---|---|---|
            | `Servers:<name>:Args` | list of string | `["a", "b"]` — under the default profile |  | The arguments. |
            | `Servers:<name>:Transport` | enum | `"Stdio"` | `Stdio`, `Sse` | The transport. |

            """,
            s_english.Section("MCP"),
            ignoreLineEndingDifferences: true);

    [Fact]
    public void The_french_tables_take_their_sentences_from_the_file_and_write_a_name_nom() =>
        Assert.Equal(
            """
            **Lue par** : `orkeon`.

            | Clé | Type | Défaut | Valeurs | Sens |
            |---|---|---|---|---|
            | `Servers:<nom>:Args` | liste de chaînes | `["a", "b"]` — sous le profil par défaut |  | Les arguments. |
            | `Servers:<nom>:Transport` | énumération | `"Stdio"` | `Stdio`, `Sse` | Le transport. |

            """,
            s_french.Section("MCP"),
            ignoreLineEndingDifferences: true);

    [Fact]
    public void The_index_links_each_category_and_each_section_and_counts_the_keys() =>
        Assert.Equal(
            """
            | Category | Sections | Keys |
            |---|---|---|
            | [Rate and budgets](#rate-and-budgets) | [`RateLimiting`](#ratelimiting), [`ToolRateLimiting`](#toolratelimiting) | 3 |
            | [Tools](#tools) | [`MCP`](#mcp) | 2 |

            3 sections, 5 keys. Read by no shipped binary, only by a host written in C# (1): [`ToolRateLimiting`](#toolratelimiting).

            """,
            s_english.Index(),
            ignoreLineEndingDifferences: true);

    [Theory]
    [InlineData("`Orkeon:CostTracking`", "orkeoncosttracking")]
    [InlineData("`BRAVE_API_KEY`", "brave_api_key")]
    [InlineData("Mémoire et vecteurs", "mémoire-et-vecteurs")]
    [InlineData("Service host and A2A", "service-host-and-a2a")]
    public void An_anchor_is_the_one_a_renderer_gives_the_heading(string heading, string anchor) =>
        Assert.Equal(anchor, SettingsReference.Anchor(heading));

    [Fact]
    public void Applying_writes_every_block_again_and_nothing_else()
    {
        var page = Page("stale\n", "| `Old` | integer | `1` | gone |\n");

        var applied = s_english.Apply(page);

        Assert.Equal(Page(s_english.Index(), s_english.Section("RateLimiting")), applied);
        Assert.Equal(applied, s_english.Apply(applied));
    }

    [Fact]
    public void A_page_without_a_place_for_a_section_or_with_a_block_for_none_is_reported()
    {
        var problems = s_english.Problems(Page("x\n", "y\n") + "\n<!-- settings:Nimporte -->\n<!-- /settings -->\n");

        Assert.Contains(problems, problem => problem.Contains("'ToolRateLimiting' has no table", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("'MCP' has no heading", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("category 'tools' has no heading", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("settings:Nimporte", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Contains("'RateLimiting'", StringComparison.Ordinal));
    }

    [Fact]
    public void The_keys_a_page_lists_are_read_back_as_the_catalogue_writes_them()
    {
        var page = "<!-- settings:MCP -->\n" + s_french.Section("MCP") + "<!-- /settings -->\n"
            + "<!-- settings:RateLimiting -->\n" + s_french.Section("RateLimiting") + "<!-- /settings -->\n";

        string[] expected = ["MCP:Servers:<name>:Args", "MCP:Servers:<name>:Transport", "RateLimiting:ApiKey", "RateLimiting:QueueLimit"];
        Assert.Equal(expected, s_french.KeysListed(page));
    }

    [Fact]
    public void The_sentences_of_a_language_are_held_to_the_catalogue()
    {
        var sentences = SettingsReferenceSentences.FromJson(
            """
            {
              "categories": { "rate-and-budgets": "Débit et budgets", "rag": "RAG" },
              "defaultNotes": { "WebScrapeTool = 10": "WebScrapeTool = 10" },
              "settings": { "ratelimiting:queuelimit": "Combien.", "RateLimiting:ApiKey": " ", "RateLimiting:Gone": "Plus là." }
            }
            """);

        var problems = sentences.Problems(s_catalog);

        Assert.Contains(problems, problem => problem.StartsWith("settings: no sentence for 'RateLimiting:ApiKey'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("settings: no sentence for 'MCP:Servers:<name>:Args'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("settings: 'RateLimiting:Gone' is no key", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("categories: no title for 'tools'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("categories: 'rag' is no category", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("defaultNotes: no sentence for \"under the default profile\"", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Contains("QueueLimit", StringComparison.Ordinal));
    }

    private static string Page(string index, string rateLimiting) =>
        $"""
        # A page

        Prose that stays.

        {SettingsReference.IndexOpeningMarker}
        {index}{SettingsReference.IndexClosingMarker}

        ## Rate and budgets

        ### `RateLimiting`

        More prose.

        {SettingsReference.OpeningMarker("RateLimiting")}
        {rateLimiting}{SettingsReference.ClosingMarker}

        The end.

        """.ReplaceLineEndings("\n");
}
