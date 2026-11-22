using System.Collections;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Orkeon.Infrastructure.LLMs.Decision;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #450, #454 — what the configuration providers and the binder the hosts are built on give for a key
/// written without a value, and for a value they cannot convert inside a dictionary or a list. The
/// start validation (<see cref="SettingsValidation"/>) is written against these facts: a change of the
/// packages that changes one of them turns this class red before it turns a refusal silent.
/// </summary>
public sealed class ConfigurationProviderCharacterisationTests
{
    private static IConfigurationRoot Json(string json)
    {
        // Build reads the stream to its end, so it is released here.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    private sealed class Switches
    {
        public bool Enabled { get; set; } = true;

        public double Share { get; set; } = 0.5;
    }

    [Fact]
    public void ShouldReadAKeyWithoutValueOrChild_WhenTheJsonWritesNull()
    {
        var configuration = Json("""{ "A": { "Enabled": null } }""");
        var section = configuration.GetSection("A:Enabled");

        // Not an empty string: the same reading as an empty object, and Exists() says false.
        Assert.False(section.Exists());
        Assert.Null(section.Value);
        Assert.Contains(configuration.GetSection("A").GetChildren(), child => child.Key == "Enabled");
    }

    [Fact]
    public void ShouldReadAKeyWithoutValueOrChild_WhenTheJsonWritesAnEmptyObject()
    {
        var configuration = Json("""{ "A": { "Enabled": {} } }""");
        var section = configuration.GetSection("A:Enabled");

        Assert.Null(section.Value);
        Assert.Empty(section.GetChildren());
        // Listed among its parent's children, so the walk of the keys meets it.
        Assert.Contains(configuration.GetSection("A").GetChildren(), child => child.Key == "Enabled");
    }

    [Fact]
    public void ShouldReadAnEmptyString_WhenTheJsonWritesAnEmptyString()
    {
        Assert.Equal(string.Empty, Json("""{ "A": { "Enabled": "" } }""")["A:Enabled"]);
    }

    [Fact]
    public void ShouldReadAnEmptyString_WhenAnEnvironmentVariableIsSetEmpty()
    {
        // The variables are handed to the provider: a process cannot set one empty for itself
        // (Environment.SetEnvironmentVariable(name, "") removes it), a shell can (`X= orkeon run`).
        var provider = new EnvironmentVariablesConfigurationProvider("ORKCHAR_");
        var load = typeof(EnvironmentVariablesConfigurationProvider).GetMethod(
            "Load", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(IDictionary)]);
        Assert.NotNull(load);

        load.Invoke(provider, [new Hashtable { ["ORKCHAR_A__Enabled"] = string.Empty }]);

        Assert.True(provider.TryGet("A:Enabled", out var value));
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void ShouldBindTheTypesDefaultOverTheSettingsDefault_WhenASwitchIsWrittenNull()
    {
        var bound = new Switches();
        Json("""{ "A": { "Enabled": null, "Share": null } }""").GetSection("A").Bind(bound);

        Assert.False(bound.Enabled);
        Assert.Equal(0, bound.Share);
    }

    [Fact]
    public void ShouldBindTheTypesDefaultOverTheSettingsDefault_WhenASwitchIsWrittenAsAnEmptyObject()
    {
        var bound = new Switches();
        Json("""{ "A": { "Enabled": {}, "Share": {} } }""").GetSection("A").Bind(bound);

        Assert.False(bound.Enabled);
        Assert.Equal(0, bound.Share);
    }

    [Fact]
    public void ShouldRefuseToConvert_WhenASwitchIsWrittenAsAnEmptyString()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Json("""{ "A": { "Enabled": "" } }""").GetSection("A").Bind(new Switches()));

        Assert.Equal("Failed to convert configuration value '' at 'A:Enabled' to type 'System.Boolean'.", error.Message);
    }

    /// <summary>
    /// #469 — the shape the start check reads back (<c>BinderRefusal</c>): the value first, as written,
    /// apostrophes included, then the path, a dictionary key's apostrophes included, then the type; the
    /// same English sentence whatever the UI culture, since the runtime's libraries ship no localised
    /// messages; and the converter's exception inside it quotes the value too.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-469")]
    public void ShouldQuoteTheValueThenThePathThenTheType_WhateverTheCulture()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["A:l'equipe:Enabled"] = "v' at 'w" })
            .Build();
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        InvalidOperationException error;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            error = Assert.Throws<InvalidOperationException>(() => configuration.GetSection("A:l'equipe").Bind(new Switches()));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }

        Assert.Equal("Failed to convert configuration value 'v' at 'w' at 'A:l'equipe:Enabled' to type 'System.Boolean'.", error.Message);
        Assert.Contains("v' at 'w", error.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldDropTheStageSilently_WhenAStageValueCannotBeConverted()
    {
        var options = new DecisionProfilesOptions();
        Json("""
            { "Orkeon": { "Decision": { "Profiles": { "p": { "Target": "default", "Stages": [
              { "Kind": "guard", "Risks": { "x": "y" }, "BlockThreshold": "x" },
              { "Kind": "guard", "Risks": { "x": "y" } } ] } } } } }
            """).GetSection(DecisionProfilesOptions.SectionName).Bind(options);

        Assert.Single(options.Profiles["p"].Stages);
    }

    [Fact]
    public void ShouldDropTheProfileSilently_WhenItsFailClosedIsNoBoolean()
    {
        var options = new DecisionProfilesOptions();
        Json("""
            { "Orkeon": { "Decision": { "Profiles": { "p": { "Target": "default", "FailClosed": "x",
              "Stages": [ { "Kind": "guard", "Risks": { "x": "y" } } ] } } } } }
            """).GetSection(DecisionProfilesOptions.SectionName).Bind(options);

        Assert.False(options.Profiles.TryGetValue("p", out var profile) && profile.FailClosed);
    }

    [Fact]
    public void ShouldIgnoreTheProfile_WhenItIsASingleValue()
    {
        var options = new DecisionProfilesOptions();
        Json("""{ "Orkeon": { "Decision": { "Profiles": { "p": "x" } } } }""")
            .GetSection(DecisionProfilesOptions.SectionName).Bind(options);

        Assert.Empty(options.Profiles);
    }
}
