using System.Reflection;
using Orkeon.Constants.Configuration;
using Orkeon.Hosting;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// The environment variables Orkeon reads are named once, in
/// <see cref="EnvironmentVariableNames"/>, and the reference page lists them all: a variable a
/// component starts to read by its name has its constant, its row in the table of the code and its
/// row in the two pages, or this fails — naming the variable and the page.
/// </summary>
public sealed class EnvironmentVariablesReferenceTests
{
    private const string EnglishPage = "docs/reference/configuration.md";
    private const string FrenchPage = "docs/fr/reference/configuration.md";

    /// <summary>The binaries a row may name: the three that read settings, and the three of Orkeon Studio.</summary>
    private static readonly string[] Binaries =
        [.. SettingsHosts.All, "orkeon-studio", "orkeon-studio-config", "orkeon-studio-run"];

    // ── the table of the code ──

    [Fact]
    public void Every_constant_is_a_variable_with_a_row_or_one_of_the_two_that_carry_a_setting()
    {
        var constants = typeof(EnvironmentVariableNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);
        var rows = EnvironmentVariableNames.ReadByName.Select(entry => entry.Name).ToList();

        Assert.Equal(rows.Count, rows.Distinct(StringComparer.Ordinal).Count());
        var withoutRow = constants
            .Where(constant => constant.Key is not (nameof(EnvironmentVariableNames.SettingsPrefix)
                or nameof(EnvironmentVariableNames.LlmApiKeySetting)))
            .Where(constant => !rows.Contains(constant.Value, StringComparer.Ordinal))
            .Select(constant => $"{constant.Key} ({constant.Value})");
        Assert.Empty(withoutRow);
        Assert.DoesNotContain(rows, name => !constants.ContainsValue(name));
        Assert.StartsWith(
            EnvironmentVariableNames.SettingsPrefix, EnvironmentVariableNames.LlmApiKeySetting, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_row_says_who_reads_the_variable_what_it_takes_and_what_it_does()
    {
        Assert.All(EnvironmentVariableNames.ReadByName, entry =>
        {
            Assert.NotEmpty(entry.ReadBy);
            Assert.All(entry.ReadBy, binary => Assert.Contains(binary, Binaries));
            Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Name);
            Assert.EndsWith(".", entry.Effect, StringComparison.Ordinal);
            // A cell of a Markdown table: the text holds no column separator and no line break.
            Assert.DoesNotContain('|', entry.Value + entry.Effect);
            Assert.DoesNotContain('\n', entry.Value + entry.Effect);
        });
    }

    // ── the two pages ──

    /// <summary>
    /// Every variable a binary reads by its name has its row in the second table of the part, and no
    /// row names a variable the code does not know. The English row is the row of the code, word for
    /// word — what <c>orkeon settings env</c> prints —; the French one keeps the name and the binaries.
    /// </summary>
    [Theory]
    [InlineData(EnglishPage)]
    [InlineData(FrenchPage)]
    public void Every_variable_read_by_its_name_has_its_row_in_the_reference(string page)
    {
        var rows = Rows(Tables(page)[1]);
        var known = EnvironmentVariableNames.ReadByName.ToDictionary(entry => entry.Name, StringComparer.Ordinal);

        var missing = known.Keys.Where(name => !rows.ContainsKey(name)).ToList();
        Assert.True(missing.Count == 0, $"{page}, \"Environment variables\": no row for {string.Join(", ", missing)}.");
        var unknown = rows.Keys.Where(name => !known.ContainsKey(name)).ToList();
        Assert.True(
            unknown.Count == 0,
            $"{page}: {string.Join(", ", unknown)} — a row for a variable {nameof(EnvironmentVariableNames)} does not name.");
        Assert.Equal(known.Keys, rows.Keys);

        foreach (var (name, cells) in rows)
        {
            var entry = known[name];
            Assert.Equal(string.Join(", ", entry.ReadBy.Select(binary => $"`{binary}`")), cells[1]);
            if (page == EnglishPage)
            {
                Assert.Equal(entry.Value, cells[2]);
                Assert.Equal(entry.Effect, cells[3]);
            }
        }
    }

    /// <summary>
    /// A settings key that holds the name of a variable has its row in the third table, and no row
    /// names a key the catalogue does not carry.
    /// </summary>
    [Theory]
    [InlineData(EnglishPage)]
    [InlineData(FrenchPage)]
    public void Every_setting_that_names_a_variable_has_its_row_in_the_reference(string page)
    {
        var french = page == FrenchPage;
        var naming = NamingKeys().Select(path => french ? path.Replace("<name>", "<nom>", StringComparison.Ordinal) : path);

        Assert.Equal(naming.Order(StringComparer.Ordinal), Rows(Tables(page)[2]).Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>The first table gives the rule and its three examples, the secret chain included.</summary>
    [Theory]
    [InlineData(EnglishPage)]
    [InlineData(FrenchPage)]
    public void The_variables_that_carry_a_setting_are_the_rule_and_its_examples(string page)
    {
        var table = Tables(page)[0];

        foreach (var example in new[]
                 {
                     "ORKEON_Llm__Model", "ORKEON_RateLimiting__MaxConcurrentRequests", "ORKEON_Orkeon__Rag__Profile",
                     EnvironmentVariableNames.LlmApiKeySetting,
                 })
        {
            Assert.Contains($"`{example}`", table, StringComparison.Ordinal);
        }

        Assert.Contains(EnvironmentVariableNames.SettingsPrefix + "<", table, StringComparison.Ordinal);
    }

    /// <summary>The settings keys that hold the name of a variable, as the catalogue writes them.</summary>
    internal static IReadOnlyList<string> NamingKeys() =>
    [
        .. SettingsCatalog.Complete.Settings
            .Select(setting => setting.Path)
            .Where(path => EnvironmentVariableNames.NamingKeySuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal))),
    ];

    // ── reading a page ──

    /// <summary>The three sub-parts of "Environment variables", in the order of the page.</summary>
    private static string[] Tables(string page)
    {
        var text = (ProducedFile.Read(page) ?? throw new InvalidOperationException($"{page} is missing."))
            .ReplaceLineEndings("\n");
        var heading = page == FrenchPage ? "\n## Variables d'environnement\n" : "\n## Environment variables\n";
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{page} has no part \"{heading.Trim()}\".");

        var part = text[(start + heading.Length)..];
        var end = part.IndexOf("\n## ", StringComparison.Ordinal);
        if (end >= 0)
            part = part[..end];

        var tables = part.Split("\n### ").Skip(1).ToArray();
        Assert.True(tables.Length == 3, $"{page}: {tables.Length} sub-parts under \"{heading.Trim()}\", three are expected.");
        return tables;
    }

    /// <summary>The rows of the table of a sub-part, by the name in the code span of their first cell.</summary>
    private static Dictionary<string, string[]> Rows(string table)
    {
        var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var line in table.Split('\n'))
        {
            if (!line.StartsWith("| `", StringComparison.Ordinal))
                continue;

            var cells = line.Trim().Trim('|').Split(" | ").Select(cell => cell.Trim()).ToArray();
            var name = cells[0].Trim('`');
            Assert.True(rows.TryAdd(name, cells), $"two rows for {name}");
        }

        return rows;
    }
}
