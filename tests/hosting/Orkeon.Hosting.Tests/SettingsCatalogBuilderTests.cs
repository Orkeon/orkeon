using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// How the settings catalogue reads a section's type: a public property is a key, with the type
/// the binder converts it to and the value it has on a new instance; a dictionary takes a name, a
/// list of sections an index; what the binder cannot set is no key.
/// </summary>
public sealed class SettingsCatalogBuilderTests
{
    private static SettingsCatalog Described() =>
        SettingsCatalogBuilder.Describe([new SettingsSource("Sample", typeof(Sample))], SettingsDocumentation.Empty);

    private static SettingsCatalogEntry Key(string path) =>
        Described().Setting(path) ?? throw new Xunit.Sdk.XunitException($"{path} is not in: {string.Join(", ", Described().Settings.Select(entry => entry.Path))}");

    [Theory]
    [InlineData("Sample:Count", "integer", "3")]
    [InlineData("Sample:Ratio", "number", "0.5")]
    [InlineData("Sample:Enabled", "boolean", "true")]
    [InlineData("Sample:Label", "string", "\"plain\"")]
    [InlineData("Sample:Wait", "duration", "\"00:01:30\"")]
    [InlineData("Sample:Level", "enum", "\"High\"")]
    [InlineData("Sample:Tags", "list of string", "[\"a\", \"b\"]")]
    [InlineData("Sample:Nested:Size", "integer", "7")]
    public void A_key_has_the_type_the_binder_reads_and_the_value_of_a_new_instance(string path, string type, string @default)
    {
        Assert.Equal(type, Key(path).Type);
        Assert.Equal(@default, Key(path).Default);
    }

    [Theory]
    [InlineData("Sample:Name", "string")]
    [InlineData("Sample:Address", "uri")]
    [InlineData("Sample:Limit", "integer")]
    public void A_key_left_null_on_a_new_instance_has_no_default(string path, string type)
    {
        Assert.Equal(type, Key(path).Type);
        Assert.Null(Key(path).Default);
        Assert.Null(Key(path).DefaultNote);
    }

    [Fact]
    public void An_enumeration_lists_its_names() =>
        Assert.Equal(["Low", "High"], Key("Sample:Level").Values);

    [Fact]
    public void A_dictionary_of_values_is_one_key_under_a_name_and_says_what_it_holds_by_default()
    {
        var limits = Key("Sample:Limits:<name>");

        Assert.Equal("integer", limits.Type);
        Assert.Null(limits.Default);
        Assert.Equal("first = 1, second = 2", limits.DefaultNote);
    }

    [Fact]
    public void A_dictionary_of_sections_puts_each_key_under_a_name_and_a_list_of_sections_under_an_index()
    {
        Assert.Equal("7", Key("Sample:Children:<name>:Size").Default);
        Assert.Equal("7", Key("Sample:Items:<i>:Size").Default);
    }

    [Fact]
    public void A_key_takes_the_name_the_binder_reads_it_under()
    {
        Assert.Equal("string", Key("Sample:renamed").Type);
        Assert.Null(Described().Setting("Sample:Other"));
    }

    [Fact]
    public void A_value_without_a_setter_is_computed_not_read()
    {
        Assert.Null(Described().Setting("Sample:Computed"));
        // A section or a collection without a setter is filled in place: its keys are keys.
        Assert.NotNull(Described().Setting("Sample:Nested:Size"));
        Assert.NotNull(Described().Setting("Sample:Tags"));
    }

    [Fact]
    public void A_secret_is_marked_and_its_default_withheld()
    {
        var key = Key("Sample:ApiKey");

        Assert.True(key.Secret);
        Assert.Null(key.Default);
    }

    [Fact]
    public void A_value_taken_as_written_and_a_type_that_holds_itself_are_open()
    {
        Assert.Equal(SettingsCatalogBuilder.Any, Key("Sample:Raw").Type);
        Assert.Equal(SettingsCatalogBuilder.Any, Key("Sample:Self").Type);
    }

    [Fact]
    public void A_default_that_is_a_date_is_said_in_words()
    {
        var key = Key("Sample:Since");

        Assert.Equal("date-time", key.Type);
        Assert.Null(key.Default);
        Assert.Equal("the moment the value is read", key.DefaultNote);
    }

    [Fact]
    public void A_shape_that_cannot_be_instantiated_gives_its_keys_without_defaults()
    {
        var catalog = SettingsCatalogBuilder.Describe([new SettingsSource("Abstract", typeof(NeverBuilt))], SettingsDocumentation.Empty);

        Assert.Equal(["Abstract:Size"], catalog.Settings.Select(entry => entry.Path));
        Assert.Null(catalog.Settings[0].Default);
    }

    [Fact]
    public void Two_readers_of_one_section_give_one_entry_per_key_and_a_section_below_another_keeps_its_own_keys()
    {
        var catalog = SettingsCatalogBuilder.Describe(
            [
                new SettingsSource("Sample:Nested", typeof(Child)),
                new SettingsSource("Sample", typeof(Sample)),
                new SettingsSource("Sample", typeof(Extra)),
            ],
            SettingsDocumentation.Empty);

        Assert.Equal(["Sample", "Sample:Nested"], catalog.Sections.Select(section => section.Path).Order(StringComparer.Ordinal));
        Assert.Single(catalog.Settings, entry => entry.Path == "Sample:Count");
        Assert.Equal("Sample", catalog.Setting("Sample:More")?.Section);
        Assert.Equal("Sample:Nested", catalog.Setting("Sample:Nested:Size")?.Section);
        Assert.Equal("Sample", catalog.Setting("Sample:Children:<name>:Size")?.Section);
    }

    [Fact]
    public void A_section_one_key_wide_is_its_own_key()
    {
        var catalog = SettingsCatalogBuilder.Describe(
            [new SettingsSource("ONE_KEY", typeof(string)) { Description = "One key.", Secret = true }],
            SettingsDocumentation.Empty);

        var key = Assert.Single(catalog.Settings);
        Assert.Equal("ONE_KEY", key.Path);
        Assert.Equal("ONE_KEY", key.Section);
        Assert.Equal("One key.", key.Description);
        Assert.True(key.Secret);
    }

    [Fact]
    public void The_sources_of_a_collection_are_its_declarations_and_what_it_registered_as_read()
    {
        var services = new ServiceCollection();
        services.AddOptions<Child>().DeclareSettings("Declared");
        services.DeclareSettingsShape("Shaped", typeof(Child));
        services.AddSingleton(new SettingsSource("Read", typeof(Child)));

        Assert.Equal(["Declared", "Shaped", "Read"], SettingsCatalogBuilder.SourcesOf(services).Select(source => source.Path));
    }

    [Fact]
    public void A_source_registered_as_read_is_not_judged_by_the_start_validation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Read:Unheard"] = "1" })
            .Build());
        services.AddSingleton(new SettingsSource("Read", typeof(Child)));
        using var provider = services.BuildServiceProvider();

        Assert.Empty(SettingsValidation.Refusals(provider));
    }

    private enum Level
    {
        Low,
        High,
    }

    private sealed class Child
    {
        public int Size { get; set; } = 7;
    }

    private sealed class Extra
    {
        public int Count { get; set; } = 3;

        public string? More { get; set; }
    }

    private abstract class NeverBuilt
    {
        public int Size { get; set; } = 7;
    }

    private sealed class Sample
    {
        public int Count { get; set; } = 3;

        public double Ratio { get; set; } = 0.5;

        public bool Enabled { get; set; } = true;

        public string Label { get; set; } = "plain";

        public string? Name { get; set; }

        public Uri? Address { get; set; }

        public int? Limit { get; set; }

        public TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(90);

        public Level Level { get; set; } = Level.High;

        public List<string> Tags { get; } = ["a", "b"];

        public Dictionary<string, int> Limits { get; } = new() { ["second"] = 2, ["first"] = 1 };

        public Dictionary<string, Child> Children { get; } = [];

        public List<Child> Items { get; } = [];

        public Child Nested { get; } = new();

        public bool Computed => Count > 0;

        [ConfigurationKeyName("renamed")]
        public string? Other { get; set; }

        public string? ApiKey { get; set; } = "leaked";

        public object? Raw { get; set; }

        public Sample? Self { get; set; }

        public DateTimeOffset Since { get; set; } = DateTimeOffset.UtcNow;
    }
}
