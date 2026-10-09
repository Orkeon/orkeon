using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// GAP-17: the registry behind <c>Llm:Profiles</c> — one provider per profile, built once and
/// metered; the default profile is the host's own provider; a host allow-list hides the profiles
/// it refuses; the settings reader validates a profile at startup.
/// </summary>
public sealed class LlmProfileRegistryTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public async Task A_profile_is_built_once_metered_and_answers_on_its_three_surfaces()
    {
        var built = 0;
        var provider = new MockLlmProvider { Name = "vendor-a" };
        provider.SetChatResult(new LlmResponse { Content = "a", PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 });
        var sink = new MockLlmUsageSink();
        var services = new ServiceCollection();
        services.AddSingleton<ILlmUsageSink>(sink);
        services.AddOrkeonLlmProfile("a", _ => { built++; return provider; }, LlmConfig.Create("model-a"));
        await using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();
        var ct = TestContext.Current.CancellationToken;

        var profile = registry.Resolve("a");
        Assert.Same(profile, registry.Resolve(" A "));
        Assert.IsType<MeteredLlmProvider>(profile.Provider);

        await profile.BasicProvider.ChatAsync("hi", cancellationToken: ct);
        await profile.ChatClient.GetResponseAsync("hi", new ChatOptions(), ct);

        Assert.Equal(1, built);
        Assert.Equal(2, provider.ChatCallCount);
        Assert.Equal(2, sink.Recorded.Count);
        Assert.All(sink.Recorded, usage => Assert.Equal("vendor-a", usage.Provider));
        Assert.Equal("model-a", provider.LastChatConfig!.Model);
    }

    [Fact]
    public async Task The_default_profile_is_the_hosts_own_provider()
    {
        var host = new MockLlmProvider { Name = "host" };
        var services = new ServiceCollection();
        services.AddOrkeonLlmProvider(_ => host);
        services.AddOrkeonLlmProfile("a", _ => new MockLlmProvider());
        await using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        Assert.Same(container.GetRequiredService<IChatClient>(), registry.Resolve(null).ChatClient);
        Assert.Same(container.GetRequiredService<ILlmProvider>(), registry.Resolve("default").Provider);
        Assert.True(registry.IsKnown("DEFAULT"));
    }

    [Fact]
    public async Task An_unknown_profile_names_the_known_ones()
    {
        var services = new ServiceCollection();
        services.AddOrkeonLlmProfile("claude", _ => new MockLlmProvider());
        services.AddOrkeonLlmProfile("local", _ => new MockLlmProvider());
        await using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        Assert.Equal(["claude", "local"], registry.Names);
        Assert.False(registry.IsKnown("gpt"));
        var error = Assert.Throws<InvalidOperationException>(() => registry.Resolve("gpt"));
        Assert.Contains("Known profiles: default, claude, local.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_allow_list_hides_the_profiles_it_refuses()
    {
        var services = new ServiceCollection();
        services.AddOrkeonLlmProfile("claude", _ => new MockLlmProvider());
        services.AddOrkeonLlmProfile("local", _ => new MockLlmProvider());
        services.Configure<LlmProfileAccessOptions>(o => o.AllowedProfiles = ["local", "default"]);
        await using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        Assert.Equal(["local"], registry.Names);
        Assert.False(registry.IsKnown("claude"));
        Assert.True(registry.IsKnown("default"));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("claude"));
    }

    [Fact]
    public void The_default_name_is_reserved()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new LlmProfileRegistry(new ServiceCollection().BuildServiceProvider(),
                [new LlmProfileRegistration("Default", _ => new MockLlmProvider())]));

        Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Profiles_are_read_with_the_shape_of_the_Llm_section()
    {
        var configuration = Configuration(
            ("Llm:Model", "deepseek-chat"),
            ("Llm:Profiles:claude:BaseUrl", "https://api.anthropic.com/v1"),
            ("Llm:Profiles:claude:ApiKey", "sk-ant"),
            ("Llm:Profiles:claude:Model", "claude-sonnet-5"),
            ("Llm:Profiles:claude:Temperature", "0.2"),
            ("Llm:Profiles:claude:MaxTokens", "2048"),
            ("Llm:Profiles:claude:Thinking:Enabled", "true"),
            ("Llm:Profiles:local:BaseUrl", "http://localhost:11434"));

        var profiles = LlmSettings.ReadProfiles(configuration);

        Assert.Equal(["claude", "local"], profiles.Select(p => p.Name));
        var claude = profiles[0].Config;
        Assert.Equal("claude-sonnet-5", claude.Model);
        Assert.Equal(new Uri("https://api.anthropic.com/v1"), claude.BaseUrl);
#pragma warning disable CS0618
        Assert.Equal("sk-ant", claude.ApiKey);
#pragma warning restore CS0618
        Assert.Equal(0.2, Assert.NotNull(claude.Temperature), precision: 3);
        Assert.Equal(2048, claude.MaxTokens);
        Assert.True(claude.Thinking!.Enabled);
        Assert.Equal("deepseek-chat", LlmSettings.ReadDefault(configuration).Model);
        Assert.True(LlmSettings.HasDefault(configuration));
    }

    [Fact]
    public void A_section_without_Temperature_sets_none()
    {
        // GAP-36: absent, nothing is sent and the model applies its own. The reader filled in the
        // engine's 0.7, which the default models of OpenAI and Anthropic refuse.
        var configuration = Configuration(
            ("Llm:Model", "gpt-5.6-sol"),
            ("Llm:Profiles:claude:Model", "claude-sonnet-5"));

        Assert.Null(LlmSettings.ReadDefault(configuration).Temperature);
        Assert.Null(Assert.Single(LlmSettings.ReadProfiles(configuration)).Config.Temperature);
    }

    /// <summary>
    /// GAP-18: a section that names no model is the provider's own default model — not OpenAI's,
    /// which the reader used to fill in and a DeepSeek or Anthropic endpoint then refused.
    /// </summary>
    [Fact]
    public void A_section_naming_no_model_leaves_the_model_to_its_provider()
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.deepseek.com"),
            ("Llm:Profiles:claude:BaseUrl", "https://api.anthropic.com/v1"));

        Assert.Equal(string.Empty, LlmSettings.ReadDefault(configuration).Model);
        Assert.Equal(string.Empty, Assert.Single(LlmSettings.ReadProfiles(configuration)).Config.Model);
    }

    [Fact]
    public void An_Llm_section_holding_profiles_alone_configures_no_default()
    {
        var configuration = Configuration(("Llm:Profiles:local:BaseUrl", "http://localhost:11434"));

        Assert.False(LlmSettings.HasDefault(configuration));
        Assert.Equal(["local"], LlmSettings.ProfileNames(configuration));
    }

    [Theory]
    [InlineData("Llm:Profiles:default:Model", "x", "reserved name")]
    [InlineData("Llm:Profiles:claude:BaseUrl", "not a url", "Llm:Profiles:claude:BaseUrl")]
    [InlineData("Llm:Profiles:claude:Temperature", "warm", "Llm:Profiles:claude:Temperature")]
    [InlineData("Llm:Profiles:claude:MaxTokens", "lots", "Llm:Profiles:claude:MaxTokens")]
    public void An_invalid_profile_fails_the_host_start_with_its_key(string key, string value, string expected)
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddOrkeonLlmProfiles(Configuration((key, value))));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-40, decision 4 — the default section is read as strictly as a profile, and a switch is a
    /// boolean everywhere: a <c>TimeoutSeconds</c> of <c>"600s"</c> ran on 30 s, a temperature that
    /// was none on the default, and <c>"yes"</c> read as « not set », in silence.
    /// </summary>
    [Theory]
    [InlineData("Llm:TimeoutSeconds", "600s")]
    [InlineData("Llm:StreamIdleSeconds", "5s")]
    [InlineData("Llm:Temperature", "warm")]
    [InlineData("Llm:MaxRetries", "a few")]
    [InlineData("Llm:Thinking:Enabled", "yes")]
    [InlineData("Llm:Grammar", "yes")]
    public void A_default_value_that_is_none_is_refused_by_its_key(string key, string value)
    {
        var configuration = Configuration(("Llm:BaseUrl", "http://localhost:11434"), (key, value));

        var error = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadDefault(configuration));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
        Assert.Contains(value, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Llm:Profiles:a:Grammar", "yes")]
    [InlineData("Llm:Profiles:a:Thinking:Enabled", "on")]
    public void A_profile_switch_that_is_no_boolean_fails_the_host_start_with_its_key(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddOrkeonLlmProfiles(Configuration(("Llm:Profiles:a:BaseUrl", "http://localhost:11434"), (key, value))));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-40 — a number that parses but is not finite: <c>"NaN"</c>, <c>"Infinity"</c>, and
    /// <c>1e400</c>, which the parser rounds to infinity. The strict read took it, and every request
    /// then failed to serialise: JSON writes no such number. Refused at the start, by its key, in the
    /// default section and in a profile alike.
    /// </summary>
    [Theory]
    [InlineData("Llm:Temperature", "NaN")]
    [InlineData("Llm:Temperature", "Infinity")]
    [InlineData("Llm:Temperature", "-Infinity")]
    [InlineData("Llm:Temperature", "1e400")]
    [InlineData("Llm:Profiles:a:Temperature", "NaN")]
    [InlineData("Llm:Profiles:a:Temperature", "1e400")]
    public void A_temperature_that_is_no_finite_number_is_refused_by_its_key(string key, string value)
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "http://localhost:11434"), ("Llm:Profiles:a:BaseUrl", "http://localhost:11434"), (key, value));

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = LlmSettings.ReadDefault(configuration);
            new ServiceCollection().AddOrkeonLlmProfiles(configuration);
        });

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
        Assert.Contains(value, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_switch_is_read_whatever_its_case()
    {
        var config = LlmSettings.ReadDefault(Configuration(
            ("Llm:BaseUrl", "http://localhost:11434"), ("Llm:Grammar", "TRUE"), ("Llm:Thinking:Enabled", "False")));

        Assert.True(config.GrammarEnabled);
        Assert.False(config.Thinking!.Enabled);
    }

    [Fact]
    public async Task A_configured_profile_is_built_by_the_provider_factory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<Application.Interfaces.Ports.ILlmProviderFactory, LlmProviderFactory>();
        services.AddOrkeonLlmProfiles(Configuration(
            ("Llm:Profiles:claude:BaseUrl", "https://api.anthropic.com/v1"),
            ("Llm:Profiles:claude:ApiKey", "sk-ant"),
            ("Llm:Profiles:claude:Model", "claude-sonnet-5")));
        await using var container = services.BuildServiceProvider();

        var profile = container.GetRequiredService<ILlmProfileRegistry>().Resolve("claude");

        Assert.IsType<AnthropicLlmProvider>(MeteredLlmProvider.Unwrap(profile.Provider));
        Assert.Equal("claude-sonnet-5", profile.Provider.BaseConfig!.Model);
    }
}
