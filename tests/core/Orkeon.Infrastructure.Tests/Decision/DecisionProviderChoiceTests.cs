using Microsoft.Extensions.Configuration;
using Orkeon.Infrastructure.Decision.SystemOne;

namespace Orkeon.Infrastructure.Tests.Decision;

public class DecisionProviderChoiceTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    // An empty section: the key exists with no value, as the JSON provider reads {}.
    private static IConfiguration EmptySections(params string[] sections)
        => Config(sections.Select<string, (string, string?)>(x => ("Orkeon:" + x, null)).ToArray());

    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value);
        return name => map.GetValueOrDefault(name);
    }

    private static DecisionProviderDescriptor Descriptor(string key) => DecisionProviderDescriptor.Find(key)!;

    [Theory]
    [InlineData("typesafe")]
    [InlineData("liquid")]
    [InlineData("kev")]
    [Trait("BR", "BR-01")]
    [Trait("BR", "UC-01")]
    [Trait("BR", "UC-02")]
    public void ShouldChooseTheNamedProvider_WhenOneIsNamed(string name)
    {
        var choice = DecisionProviderChoice.Resolve(Config(("Orkeon:Decision:Provider", name)), Env());

        Assert.True(choice.HasProvider);
        Assert.Equal(Descriptor(name), choice.Provider);
        Assert.Equal(DecisionProviderSelection.Named, choice.How);
    }

    [Theory]
    [InlineData("TypeSafe")]
    [InlineData("Liquid")]
    [Trait("BR", "BR-02")]
    public void ShouldDeduceTheProvider_WhenOnlyItsSectionIsPresent(string section)
    {
        var choice = DecisionProviderChoice.Resolve(EmptySections(section), Env());

        Assert.Equal(DecisionProviderSelection.Deduced, choice.How);
        Assert.Equal(Descriptor(section), choice.Provider);
    }

    [Theory]
    [InlineData("typesafe", "TYPESAFE_API_KEY")]
    [InlineData("liquid", "LIQUID_API_KEY")]
    [Trait("BR", "BR-02")]
    public void ShouldDeduceTheProvider_WhenOnlyItsKeyIsSet(string key, string variable)
    {
        var choice = DecisionProviderChoice.Resolve(Config(), Env((variable, "secret")));

        Assert.Equal(DecisionProviderSelection.Deduced, choice.How);
        Assert.Equal(Descriptor(key), choice.Provider);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-15")]
    public void ShouldPreferTheSection_WhenTheOtherProviderHasOnlyAKey()
    {
        var choice = DecisionProviderChoice.Resolve(EmptySections("Liquid"), Env(("TYPESAFE_API_KEY", "secret")));

        Assert.Equal(DecisionProviderDescriptor.Liquid, choice.Provider);
        Assert.Empty(choice.IgnoredSections);
    }

    [Fact]
    [Trait("BR", "BR-03")]
    public void ShouldChooseNone_WhenNothingIsConfigured()
    {
        var choice = DecisionProviderChoice.Resolve(Config(), Env());

        Assert.False(choice.HasProvider);
        Assert.Null(choice.Provider);
    }

    [Fact]
    [Trait("BR", "BR-05")]
    [Trait("BR", "BR-15")]
    public void ShouldChooseNoneAndIgnoreKev_WhenOnlyAKevSectionIsPresent()
    {
        var choice = DecisionProviderChoice.Resolve(EmptySections("Kev"), Env());

        Assert.False(choice.HasProvider);
        Assert.Equal(["Orkeon:Kev"], choice.IgnoredSections);
    }

    [Fact]
    [Trait("BR", "BR-15")]
    public void ShouldListTheOtherSections_WhenAProviderIsNamed()
    {
        var configuration = Config(
            ("Orkeon:Decision:Provider", "kev"), ("Orkeon:TypeSafe", null), ("Orkeon:Liquid", null), ("Orkeon:Kev", null));

        var choice = DecisionProviderChoice.Resolve(configuration, Env());

        Assert.Equal(DecisionProviderDescriptor.Kev, choice.Provider);
        Assert.Equal(2, choice.IgnoredSections.Count);
        Assert.Contains("Orkeon:TypeSafe", choice.IgnoredSections);
        Assert.Contains("Orkeon:Liquid", choice.IgnoredSections);
        Assert.DoesNotContain("Orkeon:Kev", choice.IgnoredSections);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public void ShouldRefuseNamingBoth_WhenTwoHostedSectionsArePresent()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DecisionProviderChoice.Resolve(EmptySections("TypeSafe", "Liquid"), Env()));

        AssertNamesBothAndTheSetting(ex.Message);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public void ShouldRefuseNamingBoth_WhenBothKeysAreSetWithoutSection()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DecisionProviderChoice.Resolve(Config(), Env(("TYPESAFE_API_KEY", "a"), ("LIQUID_API_KEY", "b"))));

        AssertNamesBothAndTheSetting(ex.Message);
    }

    private static void AssertNamesBothAndTheSetting(string message)
    {
        Assert.Contains("TypeSafe", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Liquid", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orkeon:Decision:Provider", message);
    }

    [Fact]
    [Trait("BR", "BR-14")]
    public void ShouldRefuseListingTheValidNames_WhenTheProviderIsUnknown()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DecisionProviderChoice.Resolve(Config(("Orkeon:Decision:Provider", "gpt")), Env()));

        Assert.Contains("typesafe", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("liquid", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kev", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("config")]
    [InlineData("env")]
    [Trait("BR", "BR-17")]
    public void ShouldRefuse_WhenAKevKeyWouldTravelInClearToAnotherMachine(string keySource)
    {
        var configuration = keySource == "config"
            ? Config(("Orkeon:Decision:Provider", "kev"), ("Orkeon:Kev:BaseUrl", "http://10.0.0.5:8009/v1"), ("Orkeon:Kev:ApiKey", "secret"))
            : Config(("Orkeon:Decision:Provider", "kev"), ("Orkeon:Kev:BaseUrl", "http://10.0.0.5:8009/v1"));
        var environment = keySource == "env" ? Env(("KEV_API_KEY", "secret")) : Env();

        Assert.Throws<InvalidOperationException>(() => DecisionProviderChoice.Resolve(configuration, environment));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8009/v1")]
    [InlineData("http://localhost:8009/v1")]
    [InlineData("http://[::1]:8009/v1")]
    [InlineData("https://kev.example")]
    [Trait("BR", "BR-17")]
    public void ShouldAllow_WhenTheKevAddressIsLocalOrEncrypted(string baseUrl)
    {
        var configuration = Config(
            ("Orkeon:Decision:Provider", "kev"), ("Orkeon:Kev:BaseUrl", baseUrl), ("Orkeon:Kev:ApiKey", "secret"));

        var choice = DecisionProviderChoice.Resolve(configuration, Env());

        Assert.Equal(DecisionProviderDescriptor.Kev, choice.Provider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [Trait("BR", "BR-02")]
    public void ShouldTreatABlankProviderAsNotNamed_WhenTheNameIsEmptyOrWhitespace(string name)
    {
        var configuration = Config(("Orkeon:Decision:Provider", name), ("Orkeon:Liquid", null));

        var choice = DecisionProviderChoice.Resolve(configuration, Env());

        Assert.Equal(DecisionProviderDescriptor.Liquid, choice.Provider);
        Assert.Equal(DecisionProviderSelection.Deduced, choice.How);
    }

    [Theory]
    [InlineData("KEV", "kev")]
    [InlineData("Liquid", "liquid")]
    [InlineData("TypeSafe", "typesafe")]
    [Trait("BR", "BR-01")]
    public void ShouldMatchTheProviderNameIgnoringCase_WhenItIsNamed(string name, string key)
    {
        var choice = DecisionProviderChoice.Resolve(Config(("Orkeon:Decision:Provider", name)), Env());

        Assert.Equal(Descriptor(key), choice.Provider);
        Assert.Equal(DecisionProviderSelection.Named, choice.How);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [Trait("BR", "BR-17")]
    public void ShouldAllowTheDefaultLocalAddress_WhenKevBaseUrlIsBlankAndAKeyIsSet(string? baseUrl)
    {
        var configuration = baseUrl is null
            ? Config(("Orkeon:Decision:Provider", "kev"))
            : Config(("Orkeon:Decision:Provider", "kev"), ("Orkeon:Kev:BaseUrl", baseUrl));

        var choice = DecisionProviderChoice.Resolve(configuration, Env(("KEV_API_KEY", "secret")));

        Assert.Equal(DecisionProviderDescriptor.Kev, choice.Provider);
        Assert.Equal("http://127.0.0.1:8009/v1", DecisionProviderDescriptor.Kev.DefaultBaseUrl.ToString());
    }

    [Theory]
    [InlineData("kev-box:8009")]
    [InlineData("not a url")]
    [InlineData("/v1")]
    [Trait("BR", "BR-17")]
    public void ShouldRefuseNamingTheSetting_WhenTheKevAddressIsNotAbsolute(string baseUrl)
    {
        var configuration = Config(
            ("Orkeon:Decision:Provider", "kev"), ("Orkeon:Kev:BaseUrl", baseUrl), ("Orkeon:Kev:ApiKey", "secret"));

        var ex = Assert.Throws<InvalidOperationException>(() => DecisionProviderChoice.Resolve(configuration, Env()));

        Assert.Contains("Orkeon:Kev:BaseUrl", ex.Message);
    }
}
