using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.Configuration;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The settings catalogue lists what a host reads: every section under a category, every key with
/// its type, its default and a sentence, every section with the hosts that read it. Read here from
/// what is embedded in <c>Orkeon.Hosting</c> — the catalogue a user of an installed binary gets.
/// </summary>
public sealed partial class SettingsCatalogTests
{
    [GeneratedRegex(@"\b(?!AES-|SHA-|ADR-|RFC-|ISO-|UTF-|TLS-)[A-Z]{2,}-\d+\b|plan §")]
    private static partial Regex Trace();

    private static readonly string[] s_types =
        ["string", "integer", "number", "boolean", "duration", "uri", "date-time", "enum", SettingsCatalogBuilder.Any];

    private static SettingsCatalog Complete => SettingsCatalog.Complete;

    private static SettingsCatalog OfARun => Complete.ForHost(SettingsHosts.Cli);

    [Theory]
    [InlineData("GlobalRequestsPerMinute", "60")]
    [InlineData("ProviderRequestsPerMinute", "30")]
    [InlineData("AgentRequestsPerMinute", "20")]
    [InlineData("MaxConcurrentRequests", "0")]
    [InlineData("QueueLimit", "5")]
    public void The_catalogue_of_a_run_holds_each_rate_limit_with_its_type_its_default_and_a_sentence(string key, string @default)
    {
        var entry = OfARun.Setting($"RateLimiting:{key}");

        Assert.NotNull(entry);
        Assert.Equal("RateLimiting", entry.Section);
        Assert.Equal("integer", entry.Type);
        Assert.Equal(@default, entry.Default);
        Assert.NotEqual(string.Empty, entry.Description);
    }

    [Fact]
    public void The_rate_limits_are_five_keys_filed_under_rate_and_budgets_and_read_by_the_three_binaries()
    {
        var section = Complete.Section("RateLimiting");

        Assert.NotNull(section);
        Assert.Equal(SettingsCategories.RateAndBudgets, section.Category);
        Assert.Equal(SettingsHosts.All, section.Hosts);
        Assert.Equal(5, Complete.SettingsOf("RateLimiting").Count());
    }

    [Theory]
    [InlineData("Llm:Profiles:<name>:Model")]
    [InlineData("MCP:Servers:<name>:Env:<name>")]
    [InlineData("Orkeon:Tools:Email:Accounts:<name>:Auth:PasswordEnvVar")]
    public void A_name_the_operator_chooses_is_written_as_a_placeholder(string path) =>
        Assert.NotNull(OfARun.Setting(path));

    [Fact]
    public void The_defaults_of_the_llm_section_are_those_its_reader_applies()
    {
        Assert.Equal(LlmDefaults.DefaultTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), OfARun.Setting("Llm:TimeoutSeconds")?.Default);
        Assert.Equal("30", OfARun.Setting("Llm:TimeoutSeconds")?.Default);
        Assert.Equal(LlmDefaults.DefaultMaxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture), OfARun.Setting("Llm:MaxRetries")?.Default);
        Assert.Equal("10", OfARun.Setting("Llm:MaxRetries")?.Default);
        Assert.Equal("false", OfARun.Setting("Llm:Grammar")?.Default);

        // No model, no address and no temperature unless the operator writes one.
        Assert.Null(OfARun.Setting("Llm:Model")?.Default);
        Assert.Null(OfARun.Setting("Llm:BaseUrl")?.Default);
        Assert.Null(OfARun.Setting("Llm:Temperature")?.Default);
    }

    [Fact]
    public void Every_section_has_a_category_and_every_category_a_section()
    {
        var known = SettingsCategories.All.Select(category => category.Id).ToList();

        Assert.Empty(Complete.Sections.Where(section => !known.Contains(section.Category)).Select(section => section.Path));
        Assert.Empty(known.Except(Complete.Sections.Select(section => section.Category)));
    }

    [Fact]
    public void The_table_of_categories_files_no_section_the_catalogue_does_not_hold()
    {
        var sections = Complete.Sections.Select(section => section.Path).ToList();

        // A line of the table is a section, or the root of sections declared below it (Orkeon:Cli).
        var orphans = SettingsCategories.BySection.Keys
            .Where(filed => !sections.Any(path =>
                path.Equals(filed, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(filed + ":", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Every_section_orkeon_reads_under_its_own_names_is_in_the_catalogue()
    {
        var sections = Complete.Sections.Select(section => section.Path).ToList();
        var groups = SettingsSections.Containers;

        var missing = SettingsSections.Known
            .Where(path => !groups.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Where(path => !sections.Any(section =>
                section.Equals(path, StringComparison.OrdinalIgnoreCase)
                || section.StartsWith(path + ":", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void No_key_and_no_section_is_without_a_sentence()
    {
        Assert.Empty(Complete.Settings.Where(entry => string.IsNullOrWhiteSpace(entry.Description)).Select(entry => entry.Path));
        Assert.Empty(Complete.Sections.Where(section => string.IsNullOrWhiteSpace(section.Description)).Select(section => section.Path));
    }

    [Fact]
    public void No_sentence_names_a_work_item_or_a_plan_a_reader_cannot_look_up()
    {
        // "GAP-40", "plan §8.1": the trace of why a line was written. A standard's or a decision
        // record's number (AES-256, ADR-012) is something a reader can look up.
        var trace = Trace();

        Assert.Empty(Complete.Settings.Where(entry => trace.IsMatch(entry.Description)).Select(entry => $"{entry.Path}: {entry.Description}"));
        Assert.Empty(Complete.Sections.Where(section => trace.IsMatch(section.Description)).Select(section => $"{section.Path}: {section.Description}"));
    }

    [Fact]
    public void Every_key_has_a_type_the_catalogue_knows_and_belongs_to_a_section_of_the_catalogue()
    {
        Assert.Empty(Complete.Settings
            .Where(entry => !s_types.Contains(entry.Type.StartsWith("list of ", StringComparison.Ordinal) ? entry.Type["list of ".Length..] : entry.Type))
            .Select(entry => $"{entry.Path}: {entry.Type}"));
        Assert.Empty(Complete.Settings.Where(entry => Complete.Section(entry.Section) is null).Select(entry => entry.Path));
        Assert.Empty(Complete.Settings.Where(entry => entry.Type == "enum" && entry.Values.Count == 0).Select(entry => entry.Path));
    }

    [Fact]
    public void A_section_no_shipped_host_reads_says_so_and_names_the_registration_that_reads_it()
    {
        var toolLimits = Complete.Section("ToolRateLimiting");

        Assert.NotNull(toolLimits);
        Assert.Empty(toolLimits.Hosts);
        Assert.Equal(nameof(ToolRateLimitingExtensions.AddOrkeonToolRateLimiting), toolLimits.Registration);
        Assert.Equal(3, Complete.SettingsOf("ToolRateLimiting").Count());
        Assert.Null(OfARun.Section("ToolRateLimiting"));
        Assert.Null(Complete.ForShippedHosts().Section("ToolRateLimiting"));
    }

    [Fact]
    public void Every_section_is_read_by_a_shipped_host_or_through_a_public_registration() =>
        Assert.Empty(Complete.Sections
            .Where(section => section.Hosts.Count == 0 && section.Registration is null)
            .Select(section => section.Path));

    [Fact]
    public void The_section_of_the_service_host_is_read_by_the_service_host_alone()
    {
        var host = Complete.Section("Orkeon:Host");

        Assert.NotNull(host);
        Assert.Equal([SettingsHosts.ServiceHost], host.Hosts);
        Assert.Equal(SettingsCategories.ServiceHostAndA2A, host.Category);
        Assert.NotEmpty(Complete.SettingsOf("Orkeon:Host"));
        Assert.Null(OfARun.Section("Orkeon:Host"));
        Assert.NotNull(Complete.ForHost(SettingsHosts.ServiceHost).Section("RateLimiting"));
    }

    [Fact]
    public void The_console_s_own_section_is_read_by_the_console_alone() =>
        Assert.Equal([SettingsHosts.Repl], Complete.Section("Orkeon:Cli:Tui")?.Hosts);

    [Fact]
    public void A_default_that_depends_on_the_moment_is_said_in_words()
    {
        var pricedFrom = Complete.Setting("Orkeon:CostTracking:CustomPricings:<i>:EffectiveDate");

        Assert.NotNull(pricedFrom);
        Assert.Null(pricedFrom.Default);
        Assert.Equal("the moment the value is read", pricedFrom.DefaultNote);
    }

    [Fact]
    public void No_default_holds_a_path_a_date_or_a_name_of_the_machine_that_produced_the_catalogue()
    {
        string[] ofTheMachine =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            Environment.MachineName,
            DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-",
        ];

        Assert.Empty(Complete.Settings
            .Where(entry => entry.Default is { } value && ofTheMachine.Any(part => part.Length > 1 && value.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => $"{entry.Path} = {entry.Default}"));
    }

    [Fact]
    public void A_secret_is_marked_and_never_carries_a_default()
    {
        Assert.True(Complete.Setting("Llm:ApiKey")?.Secret);
        Assert.True(Complete.Setting("Llm:Profiles:<name>:ApiKey")?.Secret);
        Assert.True(Complete.Setting("BRAVE_API_KEY")?.Secret);
        Assert.True(Complete.Setting("Secrets:<name>")?.Secret);
        Assert.False(Complete.Setting("Llm:ApiKeyEnvVar")?.Secret);
        Assert.False(Complete.Setting("Llm:MaxTokens")?.Secret);

        Assert.Empty(Complete.Settings
            .Where(entry => entry.Secret && (entry.Default is not null || entry.DefaultNote is not null))
            .Select(entry => entry.Path));
    }

    [Fact]
    public void The_json_of_the_catalogue_is_stable_sorted_and_carries_nothing_of_the_machine()
    {
        var json = Complete.ToJson();

        Assert.Equal(json, SettingsCatalog.FromJson(json).ToJson());
        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), json, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(json);
        var sections = document.RootElement.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("path").GetString()!).ToList();
        var settings = document.RootElement.GetProperty("settings").EnumerateArray().Select(entry => entry.GetProperty("path").GetString()!).ToList();

        Assert.Equal(Complete.Sections.Select(section => section.Path), sections);
        Assert.Equal(Complete.Settings.Select(entry => entry.Path), settings);
        Assert.Equal(sections.Count, sections.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(settings.Count, settings.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Categories in the order of the table, then sections by path; keys by section, then by path.
        var ranks = SettingsCategories.All.Select((category, index) => (category.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        var order = Complete.Sections.Select(section => (ranks[section.Category], section.Path.ToUpperInvariant())).ToList();
        Assert.Equal(order.Order().ToList(), order);
    }

    [Fact]
    public void A_container_s_own_catalogue_is_read_off_what_its_registrations_declare()
    {
        var services = new ServiceCollection();
        services.AddOrkeonToolRateLimiting();

        var catalog = SettingsCatalogBuilder.Describe(services, SettingsLibraryProducer.Documentation());

        Assert.Equal(["TokenBudget", "ToolRateLimiting"], catalog.Sections.Select(section => section.Path));
        Assert.All(catalog.Sections, section => Assert.Equal(SettingsCategories.RateAndBudgets, section.Category));
        Assert.Equal(Complete.SettingsOf("ToolRateLimiting").Select(entry => entry.Path), catalog.SettingsOf("ToolRateLimiting").Select(entry => entry.Path));
    }

    [Fact]
    public void A_path_is_covered_by_the_deepest_section_above_it()
    {
        Assert.Equal("Orkeon:CodeSandbox:Docker", Complete.SectionCovering("Orkeon:CodeSandbox:Docker:Image")?.Path);
        Assert.Equal("Orkeon:CodeSandbox", Complete.SectionCovering("Orkeon:CodeSandbox:Anything")?.Path);
        Assert.Null(Complete.SectionCovering("Unheard:Of"));
    }
}
