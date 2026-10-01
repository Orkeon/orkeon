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
        Assert.Equal(0.2, claude.Temperature, precision: 3);
        Assert.Equal(2048, claude.MaxTokens);
        Assert.True(claude.Thinking!.Enabled);
        Assert.Equal("deepseek-chat", LlmSettings.ReadDefault(configuration).Model);
        Assert.True(LlmSettings.HasDefault(configuration));
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
