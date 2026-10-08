using System.Text.Json;
using Orkeon.Constants.Configuration;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon settings</c>: the settings a host reads, in the terminal — by category, by section,
/// by key or by a word —, from the catalogue the tool carries. The verb opens no settings file and
/// no connection: it answers the same on a machine whose file is refused, and it has no secret to
/// print.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class SettingsCommandTests
{
    private const int Columns = 100;

    private static readonly SettingsCatalog Complete = SettingsCatalog.Complete;

    // ── without a name: the categories ──

    [Fact]
    public async Task Without_a_name_the_eleven_categories_are_listed_each_with_its_counts()
    {
        var (exit, stdout, _) = await RunAsync("settings");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(11, SettingsCategories.All.Count);
        var lines = Lines(stdout);
        foreach (var category in SettingsCategories.All)
        {
            var sections = Complete.Sections.Where(section => section.Category == category.Id).ToList();
            var keys = sections.Sum(section => Complete.SettingsOf(section.Path).Count());
            var line = Assert.Single(lines, candidate => candidate.StartsWith(category.Title + "  ", StringComparison.Ordinal));
            Assert.Contains(category.Id, line, StringComparison.Ordinal);
            Assert.Contains($"{sections.Count} sections", line, StringComparison.Ordinal);
            Assert.Contains($"{keys} keys", line, StringComparison.Ordinal);
        }

        Assert.Contains($"{Complete.Settings.Count} keys in {Complete.Sections.Count} sections", stdout, StringComparison.Ordinal);
        Assert.Contains("RateLimiting", stdout, StringComparison.Ordinal);
    }

    // ── a section ──

    [Theory]
    [InlineData("AgentRequestsPerMinute", "20")]
    [InlineData("GlobalRequestsPerMinute", "60")]
    [InlineData("MaxConcurrentRequests", "0")]
    [InlineData("ProviderRequestsPerMinute", "30")]
    [InlineData("QueueLimit", "5")]
    public async Task A_section_gives_each_key_its_type_its_default_and_its_meaning(string key, string value)
    {
        var (exit, stdout, _) = await RunAsync("settings", "RateLimiting");

        Assert.Equal(Program.ExitOk, exit);
        var lines = Lines(stdout);
        var head = Array.FindIndex(lines, line => line.StartsWith($"  RateLimiting:{key} ", StringComparison.Ordinal));
        Assert.True(head >= 0, $"no line for RateLimiting:{key} in:{Environment.NewLine}{stdout}");
        Assert.Contains("integer", lines[head], StringComparison.Ordinal);
        Assert.EndsWith($"default: {value}", lines[head], StringComparison.Ordinal);

        // The meaning follows, indented under the key: the first words of the catalogue's sentence.
        var meaning = Complete.Setting($"RateLimiting:{key}")!.Description;
        Assert.StartsWith("      " + meaning[..20], lines[head + 1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_lists_its_keys_only_and_says_who_reads_it()
    {
        var (exit, stdout, _) = await RunAsync("settings", "ratelimiting");

        Assert.Equal(Program.ExitOk, exit);
        var keys = Lines(stdout).Where(line => line.StartsWith("  RateLimiting:", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, keys.Count);
        Assert.Contains("Read by: orkeon, orkeon-host, orkeon-repl", Lines(stdout));
        Assert.Contains("Rate and budgets", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolRateLimiting:", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_no_shipped_binary_reads_says_so_and_names_its_registration()
    {
        var (exit, stdout, _) = await RunAsync("settings", "ToolRateLimiting");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("Read by: a C# host only — AddOrkeonToolRateLimiting()", Lines(stdout));
    }

    [Fact]
    public async Task Every_section_no_shipped_binary_reads_says_so()
    {
        var orphans = Complete.Sections.Where(section => section.Hosts.Count == 0).ToList();
        Assert.NotEmpty(orphans);

        foreach (var section in orphans)
        {
            var (exit, stdout, _) = await RunAsync("settings", section.Path);

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains($"Read by: a C# host only — {section.Registration}()", Lines(stdout));
        }
    }

    // ── a key ──

    [Fact]
    public async Task A_key_gives_that_key_alone()
    {
        var (exit, stdout, _) = await RunAsync("settings", "RateLimiting:QueueLimit");

        Assert.Equal(Program.ExitOk, exit);
        var keys = Lines(stdout).Where(line => line.StartsWith("  RateLimiting:", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(keys);
        Assert.Contains("integer", line, StringComparison.Ordinal);
        Assert.EndsWith("default: 5", line, StringComparison.Ordinal);
        Assert.Contains("Read by: orkeon, orkeon-host, orkeon-repl", Lines(stdout));
    }

    /// <summary>A profile is named by the operator: the key is found under the name they gave it.</summary>
    [Fact]
    public async Task A_key_under_a_name_the_operator_chose_is_found_with_that_name()
    {
        var (exit, stdout, _) = await RunAsync("settings", "Llm:Profiles:fast:Model");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Single(Lines(stdout), line => line.StartsWith("  Llm:Profiles:<name>:Model ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_enumeration_lists_its_values_and_a_default_that_is_no_constant_is_said_in_words()
    {
        var (_, transport, _) = await RunAsync("settings", "MCP:Servers:<name>:Transport");
        Assert.Contains("one of: Stdio, Sse", transport, StringComparison.Ordinal);

        var (_, date, _) = await RunAsync("settings", "Orkeon:CostTracking:CustomPricings:<i>:EffectiveDate");
        Assert.Contains("default: the moment the value is read", date, StringComparison.Ordinal);
    }

    // ── a category ──

    [Theory]
    [InlineData("rate-and-budgets")]
    [InlineData("Rate and budgets")]
    [InlineData("RATE-AND-BUDGETS")]
    public async Task A_category_lists_its_sections_by_its_id_or_its_title(string name)
    {
        var (exit, stdout, _) = await RunAsync("settings", name);

        Assert.Equal(Program.ExitOk, exit);
        var lines = Lines(stdout);
        Assert.Contains("  RateLimiting — 5 keys", lines);
        Assert.Contains("  TokenBudget — 3 keys", lines);
        Assert.Contains("  ToolRateLimiting — 3 keys", lines);
        Assert.DoesNotContain("Llm", stdout, StringComparison.Ordinal);
    }

    // ── a word ──

    [Fact]
    public async Task A_word_that_names_nothing_exactly_gives_what_contains_it()
    {
        var (exit, stdout, _) = await RunAsync("settings", "rate");

        Assert.Equal(Program.ExitOk, exit);
        var lines = Lines(stdout);
        Assert.Contains(lines, line => line.StartsWith("  RateLimiting — 5 keys", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("  ToolRateLimiting — 3 keys", StringComparison.Ordinal));

        // The word is looked for where a word starts: `rate` is not in FallbackStrategy.
        Assert.DoesNotContain("Strategy", stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("limit", "  RateLimiting — 5 keys")]
    [InlineData("queuelimit", "  RateLimiting:QueueLimit ")]
    [InlineData("Llm:Profiles", "  Llm:Profiles:<name>:Model ")]
    [InlineData("api_key", "  BRAVE_API_KEY ")]
    public async Task A_word_is_found_inside_a_name_where_a_word_of_the_name_starts(string word, string line)
    {
        var (exit, stdout, _) = await RunAsync("settings", word);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(Lines(stdout), candidate => candidate.StartsWith(line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_word_is_looked_for_in_the_descriptions_too()
    {
        // No path holds the word; the description of the queue limit does.
        Assert.DoesNotContain(Complete.Settings, entry => entry.Path.Contains("refused", StringComparison.OrdinalIgnoreCase));

        var (exit, stdout, _) = await RunAsync("settings", "refused");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(Lines(stdout), line => line.StartsWith("  RateLimiting:QueueLimit ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_that_matches_nothing_exits_1_and_proposes_the_closest()
    {
        var (exit, stdout, stderr) = await RunAsync("settings", "Nimporte");

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Empty(stdout);
        Assert.Contains("'Nimporte'", stderr, StringComparison.Ordinal);
        Assert.Contains("The closest name is ", stderr, StringComparison.Ordinal);

        var (typoExit, _, typo) = await RunAsync("settings", "RateLimitng");

        Assert.Equal(Program.ExitScriptError, typoExit);
        Assert.Contains("The closest name is RateLimiting", typo, StringComparison.Ordinal);
    }

    // ── --all, --json, --host ──

    [Fact]
    public async Task All_gives_every_key_by_category_within_a_hundred_columns()
    {
        var (exit, stdout, _) = await RunAsync("settings", "--all");

        Assert.Equal(Program.ExitOk, exit);
        var lines = Lines(stdout);
        foreach (var entry in Complete.Settings)
            Assert.Contains(lines, line => line.StartsWith($"  {entry.Path} ", StringComparison.Ordinal) || line == $"  {entry.Path}");

        var position = 0;
        foreach (var category in SettingsCategories.All)
        {
            var next = stdout.IndexOf($"== {category.Title} ", position, StringComparison.Ordinal);
            Assert.True(next >= position, $"{category.Title} is missing or out of order");
            position = next;
        }

        Assert.All(lines, line => Assert.True(line.Length <= Columns, $"{line.Length} columns: {line}"));
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("settings", "rate")]
    [InlineData("settings", "models")]
    [InlineData("settings", "Orkeon:Rag:WebFallback")]
    [InlineData("settings", "Security:ToolResults:TrustedTools")]
    [InlineData("settings", "--help")]
    public async Task Every_view_holds_in_a_hundred_columns(params string[] args)
    {
        var (exit, stdout, _) = await RunAsync(args);

        Assert.Equal(Program.ExitOk, exit);
        Assert.All(Lines(stdout), line => Assert.True(line.Length <= Columns, $"{line.Length} columns: {line}"));
    }

    [Fact]
    public async Task Json_is_the_catalogue_as_it_is_sorted_and_without_a_date()
    {
        var (exit, stdout, stderr) = await RunAsync("settings", "--json");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Empty(stderr);
        Assert.Equal(Complete.ToJson(), stdout);

        using var document = JsonDocument.Parse(stdout);
        var sections = document.RootElement.GetProperty("sections").EnumerateArray()
            .Select(section => section.GetProperty("path").GetString()).ToList();
        var settings = document.RootElement.GetProperty("settings").EnumerateArray()
            .Select(entry => entry.GetProperty("path").GetString()).ToList();
        Assert.Equal(Complete.Sections.Select(section => section.Path), sections);
        Assert.Equal(Complete.Settings.Select(entry => entry.Path), settings);
        Assert.DoesNotMatch(@"\b20\d\d-\d\d-\d\d", stdout);
    }

    [Fact]
    public async Task Json_follows_the_name_and_the_host()
    {
        var (_, section, _) = await RunAsync("settings", "RateLimiting", "--json");
        Assert.Equal(Complete.Where(candidate => candidate.Path == "RateLimiting").ToJson(), section);

        var (_, host, _) = await RunAsync("settings", "--json", "--host", "host");
        Assert.Equal(Complete.ForHost(SettingsHosts.ServiceHost).ToJson(), host);

        var (_, key, _) = await RunAsync("settings", "--json", "RateLimiting:QueueLimit");
        using var document = JsonDocument.Parse(key);
        Assert.Equal("RateLimiting", Assert.Single(document.RootElement.GetProperty("sections").EnumerateArray()).GetProperty("path").GetString());
        Assert.Equal("RateLimiting:QueueLimit", Assert.Single(document.RootElement.GetProperty("settings").EnumerateArray()).GetProperty("path").GetString());

        var (missingExit, missing, _) = await RunAsync("settings", "--json", "Nimporte");
        Assert.Equal(Program.ExitScriptError, missingExit);
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("run", SettingsHosts.Cli)]
    [InlineData("host", SettingsHosts.ServiceHost)]
    [InlineData("repl", SettingsHosts.Repl)]
    [InlineData("orkeon-host", SettingsHosts.ServiceHost)]
    public async Task Host_narrows_the_listing_to_what_that_binary_reads(string value, string host)
    {
        var read = Complete.ForHost(host);

        var (exit, stdout, _) = await RunAsync("settings", "--host", value, "--all");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains($"{read.Settings.Count} keys in {read.Sections.Count} sections", stdout, StringComparison.Ordinal);
        var lines = Lines(stdout);
        foreach (var section in Complete.Sections)
        {
            var listed = lines.Any(line => line.StartsWith($"{section.Path} — ", StringComparison.Ordinal));
            Assert.Equal(read.Section(section.Path) is not null, listed);
        }
    }

    [Fact]
    public async Task Host_says_when_a_section_is_another_binarys()
    {
        var (exit, stdout, stderr) = await RunAsync("settings", "Orkeon:Host", "--host=run");

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Empty(stdout);
        Assert.Contains("Orkeon:Host is not read by orkeon", stderr, StringComparison.Ordinal);
        Assert.Contains("orkeon-host", stderr, StringComparison.Ordinal);

        var (unknownExit, _, unknown) = await RunAsync("settings", "--host", "studio");

        Assert.Equal(Program.ExitScriptError, unknownExit);
        Assert.Contains("run, host or repl", unknown, StringComparison.Ordinal);
    }

    // ── what the verb never does ──

    /// <summary>
    /// The machine's settings file carries an API key and its own queue limit: the verb answers as
    /// it does without the file — the default, not the machine's value —, and the key's value is
    /// nowhere in what it prints.
    /// </summary>
    [Fact]
    public async Task No_output_carries_a_secret_and_the_settings_file_of_the_machine_changes_nothing()
    {
        const string Secret = "sk-live-0123456789-do-not-print";
        var without = new List<string>();
        foreach (var args in Views)
            without.Add((await RunAsync(args)).Stdout);

        var directory = Path.Combine(Path.GetTempPath(), "orkeon-settings-verb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "appsettings.json");
        await File.WriteAllTextAsync(
            file,
            $$"""{ "Llm": { "ApiKey": "{{Secret}}" }, "RateLimiting": { "QueueLimit": 99 } }""",
            TestContext.Current.CancellationToken);
        RunnerSettings.GlobalSettingsPathOverride = file;
        try
        {
            for (var index = 0; index < Views.Length; index++)
            {
                var (exit, stdout, stderr) = await RunAsync(Views[index]);

                Assert.Equal(Program.ExitOk, exit);
                Assert.DoesNotContain(Secret, stdout + stderr, StringComparison.Ordinal);
                Assert.Equal(without[index], stdout);
            }
        }
        finally
        {
            RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;
            Directory.Delete(directory, recursive: true);
        }

        // A secret is marked, and has neither a default nor a value.
        var (_, key, _) = await RunAsync("settings", "Llm:ApiKey");
        Assert.Single(Lines(key), line => line.StartsWith("  Llm:ApiKey ", StringComparison.Ordinal) && line.EndsWith("secret", StringComparison.Ordinal));
    }

    private static readonly string[][] Views =
    [
        ["settings"],
        ["settings", "--all"],
        ["settings", "Llm"],
        ["settings", "RateLimiting"],
        ["settings", "--json"],
    ];

    /// <summary>
    /// A workshop keeps its settings under a <c>settings/</c> folder, and a word that is an existing
    /// folder reads as a crew to run: the verb is looked up first, so <c>orkeon settings</c> stays
    /// the verb there — and writes nothing in the folder it is run from.
    /// </summary>
    [Fact]
    public async Task Settings_stays_the_verb_where_a_settings_folder_exists()
    {
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "settings");
        var created = !Directory.Exists(folder);
        if (created)
            Directory.CreateDirectory(folder);

        try
        {
            Assert.True(CliUsage.LooksLikeCrewTarget("settings"));

            var (exit, stdout, stderr) = await RunAsync("settings");

            Assert.Equal(Program.ExitOk, exit);
            Assert.Empty(stderr);
            Assert.Contains("Rate and budgets", stdout, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        }
        finally
        {
            if (created)
                Directory.Delete(folder);
        }
    }

    // ── the grammar ──

    [Fact]
    public async Task Help_names_the_forms_and_an_unknown_option_is_refused()
    {
        var (helpExit, help, _) = await RunAsync("settings", "--help");

        Assert.Equal(Program.ExitOk, helpExit);
        foreach (var form in new[] { "--all", "--json", "--host", "<section>", "<key>", "<category>" })
            Assert.Contains(form, help, StringComparison.Ordinal);

        var (exit, stdout, stderr) = await RunAsync("settings", "--effective");

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Empty(stdout);
        Assert.Contains("unknown option '--effective'", stderr, StringComparison.Ordinal);

        var (hostExit, _, host) = await RunAsync("settings", "--host");

        Assert.Equal(Program.ExitScriptError, hostExit);
        Assert.Contains("--host needs", host, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_top_level_help_names_the_verb_and_ends_on_the_reference_page()
    {
        var (exit, stdout, _) = await RunAsync("--help");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(Lines(stdout), line => line.StartsWith("  settings ", StringComparison.Ordinal));
        Assert.EndsWith("reference/configuration.html", Lines(stdout)[^1], StringComparison.Ordinal);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        using var console = new TestConsole();
        var exit = await Program.DispatchAsync(args);
        return (exit, console.Stdout, console.Stderr);
    }

    private static string[] Lines(string text) =>
        text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}
