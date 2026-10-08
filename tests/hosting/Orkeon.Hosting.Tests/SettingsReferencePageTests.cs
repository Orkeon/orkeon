using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The settings reference — <c>docs/reference/configuration.md</c> and its French mirror — lists
/// what the settings catalogue lists: every key has a row, no row names a key the catalogue does
/// not hold, and the tables are the ones the catalogue produces. A key added to the engine, a
/// default that changes or a comment that is reworded fails here until the tables are written
/// again; a key without its French sentence fails until someone writes it.
/// </summary>
public sealed class SettingsReferencePageTests
{
    private static SettingsCatalog Catalog => SettingsCatalog.Complete;

    private static SettingsReferenceSentences FrenchSentences() =>
        SettingsReferenceSentences.FromJson(
            ProducedFile.Read(SettingsReferenceSentences.FrenchFile)
            ?? throw new InvalidOperationException($"{SettingsReferenceSentences.FrenchFile} is missing."));

    private static SettingsReference Reference(string page) =>
        string.Equals(page, SettingsReferencePages.French, StringComparison.Ordinal)
            ? new SettingsReference(Catalog, SettingsReferenceLanguage.French(FrenchSentences()))
            : new SettingsReference(Catalog, SettingsReferenceLanguage.English);

    private static string Text(string page) =>
        ProducedFile.Read(page) ?? throw new InvalidOperationException($"{page} is missing.");

    [Theory]
    [InlineData(SettingsReferencePages.English)]
    [InlineData(SettingsReferencePages.French)]
    public void Every_key_of_the_catalogue_has_a_row_and_no_row_names_another(string page)
    {
        // A run that writes the tables has nothing to check yet: the test that writes may come after this one.
        if (ProducedFile.Writing)
            return;

        var listed = Reference(page).KeysListed(Text(page));
        var known = Catalog.Settings.Select(entry => entry.Path).ToList();

        var missing = known.Except(listed, StringComparer.Ordinal).ToList();
        var unknown = listed.Except(known, StringComparer.Ordinal).ToList();
        var twice = listed.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        Assert.True(
            missing.Count == 0 && unknown.Count == 0 && twice.Count == 0,
            $"{page} does not list the settings the code reads.\n"
            + $"{missing.Count} key(s) without a row: {string.Join(", ", missing)}\n"
            + $"{unknown.Count} row(s) for a key the catalogue does not hold: {string.Join(", ", unknown)}\n"
            + $"{twice.Count} key(s) listed twice: {string.Join(", ", twice)}\n"
            + $"Write the tables again: {SettingsReferencePages.Regenerate}");
    }

    [Theory]
    [InlineData(SettingsReferencePages.English)]
    [InlineData(SettingsReferencePages.French)]
    public void Every_section_and_every_category_has_its_place_in_the_page(string page)
    {
        var problems = Reference(page).Problems(Text(page));

        Assert.True(problems.Count == 0, $"{page}:\n" + string.Join("\n", problems));
    }

    [Theory]
    [InlineData(SettingsReferencePages.English)]
    [InlineData(SettingsReferencePages.French)]
    public void The_tables_of_the_page_are_the_ones_the_catalogue_produces(string page)
    {
        var reference = Reference(page);
        var text = Text(page);
        if (!ProducedFile.Writing)
        {
            // Named first: "line 612 differs" sends nobody to the options whose default changed.
            var stale = reference.StaleBlocks(text);
            Assert.True(
                stale.Count == 0,
                $"{page} is not what the settings catalogue produces for: {string.Join(", ", stale)}. A key, a default or a comment changed " +
                $"there since the page was written. Write the tables again: {SettingsReferencePages.Regenerate}");
        }

        ProducedFile.AssertCurrent(page, reference.Apply(text), SettingsReferencePages.Regenerate);
    }

    [Fact]
    public void A_key_added_or_a_default_changed_makes_the_table_of_its_section_stale_and_no_other()
    {
        if (ProducedFile.Writing)
            return;

        var page = Text(SettingsReferencePages.English);
        var queue = Catalog.Setting("RateLimiting:QueueLimit")!;

        var withOneMoreKey = new SettingsCatalog(
            Catalog.Sections,
            Catalog.Settings.Append(queue with { Path = "RateLimiting:BurstLimit" }));
        var withAnotherDefault = new SettingsCatalog(
            Catalog.Sections,
            Catalog.Settings.Select(entry => ReferenceEquals(entry, queue) ? queue with { Default = "6" } : entry));

        // The index counts the keys of each category: one more key moves it too.
        Assert.Equal(
            ["the index of the categories", "RateLimiting"],
            new SettingsReference(withOneMoreKey, SettingsReferenceLanguage.English).StaleBlocks(page));
        Assert.Equal(
            ["RateLimiting"],
            new SettingsReference(withAnotherDefault, SettingsReferenceLanguage.English).StaleBlocks(page));
        Assert.Empty(new SettingsReference(Catalog, SettingsReferenceLanguage.English).StaleBlocks(page));
    }

    [Fact]
    public void The_french_sentences_cover_every_key_and_no_other()
    {
        var problems = FrenchSentences().Problems(Catalog);

        Assert.True(
            problems.Count == 0,
            $"{SettingsReferenceSentences.FrenchFile} is kept by hand, one sentence per key of the settings catalogue:\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void The_reference_says_who_reads_a_section_no_shipped_binary_reads()
    {
        if (ProducedFile.Writing)
            return;

        var english = Text(SettingsReferencePages.English);

        Assert.All(
            Catalog.Sections.Where(section => section.Hosts.Count == 0),
            section => Assert.Contains(
                $"{SettingsReference.OpeningMarker(section.Path)}\n**Read by**: a host written in C# only",
                english,
                StringComparison.Ordinal));
        Assert.Contains("| `QueueLimit` | integer | `5` |", english, StringComparison.Ordinal);
    }
}
