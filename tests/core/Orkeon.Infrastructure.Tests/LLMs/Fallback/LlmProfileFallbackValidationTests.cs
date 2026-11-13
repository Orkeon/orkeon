using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.LLMs.Fallback;

/// <summary>
/// FWK-05: the registry judges the chains between its profiles when it is built, answers a call through
/// the chain it resolved, and warns about a fallback that can do less than its primary.
/// </summary>
public sealed class LlmProfileFallbackValidationTests
{
    private static readonly LlmMessage[] Hello = [new() { Role = "user", Content = "hi" }];

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("the mock provider makes no network call");
    }

    private static ServiceProvider Host(Action<IServiceCollection> profiles)
    {
        var services = new ServiceCollection();
        profiles(services);
        return services.BuildServiceProvider();
    }

    private static void Profile(IServiceCollection services, string name, FakeScriptedLlmProvider provider, params string[] fallback) =>
        services.AddOrkeonLlmProfile(name, _ => provider, null, fallback);

    [Fact]
    public async Task A_cycle_between_profiles_is_refused_when_the_registry_is_built()
    {
        await using var container = Host(s =>
        {
            Profile(s, "a", new FakeScriptedLlmProvider(), "b");
            Profile(s, "b", new FakeScriptedLlmProvider(), "c");
            Profile(s, "c", new FakeScriptedLlmProvider(), "a");
        });

        var ex = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<ILlmProfileRegistry>());

        Assert.Contains("a -> b -> c -> a", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_profile_that_names_itself_is_refused()
    {
        await using var container = Host(s => Profile(s, "a", new FakeScriptedLlmProvider(), "a"));

        var ex = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<ILlmProfileRegistry>());

        Assert.Contains("a -> a", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_fallback_name_is_refused_naming_the_profile_the_name_and_the_known_ones()
    {
        await using var container = Host(s =>
        {
            Profile(s, "a", new FakeScriptedLlmProvider(), "ghost");
            Profile(s, "b", new FakeScriptedLlmProvider());
        });

        var ex = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<ILlmProfileRegistry>());

        Assert.Contains("'a'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'ghost'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_name_in_the_configuration_refuses_the_registry_too()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:Profiles:a:Provider"] = "mock",
            ["Llm:Profiles:a:Responses"] = "/llm-mock/a.jsonl",
            ["Llm:Profiles:a:Fallback:0"] = "ghost",
        }).Build();
        var services = new ServiceCollection();
        services.AddOrkeonLlmProfiles(configuration);
        await using var container = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<ILlmProfileRegistry>());

        Assert.Contains("'ghost'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_configuration_key_Fallback_builds_the_chain_a_crew_resolves()
    {
        var files = new FakeFileSystemService().AddMount("/llm-mock")
            .AddFile("/llm-mock/a.jsonl", """{"match":"ping","content":"from-a"}""")
            .AddFile("/llm-mock/b.jsonl", """{"match":"ping","content":"from-b"}""");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:Profiles:a:Provider"] = "mock",
            ["Llm:Profiles:a:Responses"] = "/llm-mock/a.jsonl",
            ["Llm:Profiles:a:Fallback:0"] = "b",
            ["Llm:Profiles:b:Provider"] = "mock",
            ["Llm:Profiles:b:Responses"] = "/llm-mock/b.jsonl",
        }).Build();
        using var loggers = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(files);
        services.AddSingleton<IHttpClientFactory>(new NoHttpClientFactory());
        services.AddSingleton<ILoggerFactory>(loggers);
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
        services.AddOrkeonLlmProfiles(configuration);
        await using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        var profile = registry.Resolve("a");

        var chain = Assert.IsType<FallbackLlmProvider>(profile.Provider);
        Assert.Equal(["a", "b"], chain.Links.Select(l => l.Name));
        var answer = await profile.BasicProvider.ChatAsync("ping", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("from-a", answer);
        Assert.IsNotType<FallbackLlmProvider>(registry.Resolve("b").Provider);
    }

    [Fact]
    public async Task A_call_through_the_resolved_profile_switches_on_a_503_and_the_host_is_told()
    {
        var observer = new FakeLlmFallbackObserver();
        var a = new FakeScriptedLlmProvider { Name = "vendor-a", Response = FakeScriptedLlmProvider.Http(System.Net.HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { Name = "vendor-b", Response = new LlmResponse { Content = "from-b" } };
        await using var container = Host(s =>
        {
            s.AddSingleton<ILlmFallbackObserver>(observer);
            Profile(s, "a", a, "b");
            Profile(s, "b", b);
        });
        var profile = container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");
        var ct = TestContext.Current.CancellationToken;

        var text = await profile.BasicProvider.ChatAsync("hi", cancellationToken: ct);
        var viaClient = await profile.ChatClient.GetResponseAsync("hi", new ChatOptions(), ct);

        Assert.Equal("from-b", text);
        Assert.Equal("from-b", viaClient.Text);
        Assert.Equal(2, observer.Taken.Count);
        Assert.All(observer.Taken, t => Assert.Equal(("a", "a", "b", "server_error"), t));
    }

    [Fact]
    public async Task A_400_through_the_resolved_profile_does_not_switch()
    {
        var observer = new FakeLlmFallbackObserver();
        var a = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Http(System.Net.HttpStatusCode.BadRequest) };
        var b = new FakeScriptedLlmProvider();
        await using var container = Host(s =>
        {
            s.AddSingleton<ILlmFallbackObserver>(observer);
            Profile(s, "a", a, "b");
            Profile(s, "b", b);
        });
        var profile = container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        await profile.Provider.ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }

    [Fact]
    public async Task A_profile_can_fall_back_on_the_hosts_default_and_a_hidden_profile()
    {
        var host = new FakeScriptedLlmProvider { Name = "host", Response = new LlmResponse { Content = "from-host" } };
        var hidden = new FakeScriptedLlmProvider { Name = "hidden", Response = FakeScriptedLlmProvider.Http(System.Net.HttpStatusCode.BadGateway) };
        await using var container = Host(s =>
        {
            s.AddOrkeonLlmProvider(_ => host);
            Profile(s, "a", new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Http(System.Net.HttpStatusCode.BadGateway) }, "hidden");
            Profile(s, "hidden", hidden, "default");
            s.Configure<LlmProfileAccessOptions>(o => o.AllowedProfiles = ["a"]);
        });
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        var answer = await registry.Resolve("a").Provider.ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(registry.IsKnown("hidden"));
        Assert.Equal("from-host", answer.Content);
    }

    [Fact]
    public async Task A_fallback_that_supports_less_than_its_primary_is_warned_about_once()
    {
        using var loggers = new RecordingLoggerFactory();
        var rich = new LlmProviderCapabilities { Vision = true, GbnfGrammar = true };
        await using var container = Host(s =>
        {
            s.AddSingleton<ILoggerFactory>(loggers);
            Profile(s, "a", new FakeScriptedLlmProvider { Capabilities = rich }, "b");
            Profile(s, "b", new FakeScriptedLlmProvider { Capabilities = LlmProviderCapabilities.Unknown });
        });
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        registry.Resolve("a");
        registry.Resolve("a");

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("'b'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("vision", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_fallback_as_capable_as_its_primary_raises_no_warning()
    {
        using var loggers = new RecordingLoggerFactory();
        await using var container = Host(s =>
        {
            s.AddSingleton<ILoggerFactory>(loggers);
            Profile(s, "a", new FakeScriptedLlmProvider(), "b");
            Profile(s, "b", new FakeScriptedLlmProvider());
        });

        container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        Assert.DoesNotContain(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_fallback_whose_model_takes_no_audio_is_warned_about_by_the_table()
    {
        using var loggers = new RecordingLoggerFactory();
        await using var container = Host(s =>
        {
            s.AddSingleton<ILoggerFactory>(loggers);
            Profile(s, "a", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("gpt-4o-audio-preview", "k") }, "b");
            Profile(s, "b", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("gpt-4o", "k") });
        });

        container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("audio input", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fallback_whose_model_takes_fewer_efforts_is_warned_about_by_the_table()
    {
        using var loggers = new RecordingLoggerFactory();
        await using var container = Host(s =>
        {
            s.AddSingleton<ILoggerFactory>(loggers);
            Profile(s, "a", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("o3-mini", "k"), Capabilities = EffortOnly }, "b");
            Profile(s, "b", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("gpt-5-pro", "k"), Capabilities = EffortOnly });
        });

        container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("reasoning effort (low, medium)", warning.Message, StringComparison.Ordinal);
    }

    private static readonly LlmProviderCapabilities EffortOnly = new() { Thinking = ThinkingSupport.EffortOnly };
    private static readonly LlmProviderCapabilities Toggle = new() { Thinking = ThinkingSupport.Toggle };

    [Fact]
    public async Task A_fallback_whose_model_cannot_reason_is_warned_about_though_its_provider_can()
    {
        // The provider declares a switch, the model has no reasoning pass: the effective value is what a call gets (#356).
        using var loggers = new RecordingLoggerFactory();
        var table = new Orkeon.Infrastructure.LLMs.Capabilities.ModelCapabilityRegistry(
            new Dictionary<string, Orkeon.Infrastructure.LLMs.Capabilities.ModelCapabilityOverride>
            {
                ["acme-plain"] = new() { Thinking = ThinkingSupport.None },
            });
        await using var container = Host(s =>
        {
            s.AddSingleton<ILoggerFactory>(loggers);
            s.AddSingleton<Orkeon.Infrastructure.LLMs.Capabilities.IModelCapabilityRegistry>(table);
            Profile(s, "a", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("acme-thinker", "k"), Capabilities = Toggle }, "b");
            Profile(s, "b", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("acme-plain-1", "k"), Capabilities = Toggle });
        });

        container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("does not support thinking,", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fallback_whose_model_always_reasons_is_warned_about_the_switch()
    {
        using var loggers = new RecordingLoggerFactory();
        await using var container = Host(s =>
        {
            s.AddSingleton<ILoggerFactory>(loggers);
            Profile(s, "a", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("kimi-k3", "k"), Capabilities = Toggle }, "b");
            Profile(s, "b", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("kimi-k2.7-code", "k"), Capabilities = Toggle });
        });

        container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("thinking (Toggle -> EffortOnly)", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hosts_table_decides_what_a_fallback_lacks()
    {
        using var loggers = new RecordingLoggerFactory();
        var table = new Orkeon.Infrastructure.LLMs.Capabilities.ModelCapabilityRegistry(
            new Dictionary<string, Orkeon.Infrastructure.LLMs.Capabilities.ModelCapabilityOverride>
            {
                ["gpt-4o"] = new() { AudioInput = true },
            });
        await using var container = Host(s =>
        {
            s.AddSingleton<ILoggerFactory>(loggers);
            s.AddSingleton<Orkeon.Infrastructure.LLMs.Capabilities.IModelCapabilityRegistry>(table);
            Profile(s, "a", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("gpt-4o-audio-preview", "k") }, "b");
            Profile(s, "b", new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("gpt-4o", "k") });
        });

        container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        Assert.DoesNotContain(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
    }
}
