using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// A section written <c>Provider: &lt;key&gt;</c> is built by the provider module registered with that
/// key, through the same layers as a built-in provider, and never by inference from its address or model.
/// </summary>
public sealed class LlmProviderModuleTests
{
    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => KeyValuePair.Create<string, string?>(p.Key, p.Value))).Build();

    private static LlmProviderFactory Factory(
        IReadOnlyList<ILlmProviderModule>? modules = null, ILlmUsageSink? sink = null, ILoggerFactory? loggers = null) =>
        new(new NoHttpClientFactory(), loggers ?? NullLoggerFactory.Instance, new FakeFileSystemService(),
            new LlmProviderFactoryOptions { Modules = modules, UsageSink = sink });

    [Fact]
    public void ShouldThrow_WhenProviderKeyHasNoModule()
    {
        var config = LlmSettings.ReadDefault(Settings(("Llm:Provider", "agent-cli"), ("Llm:Model", "opus")));

        var ex = Assert.Throws<NotSupportedException>(() => Factory().Create(config));

        Assert.Contains("Llm:Provider names a provider module this host does not register", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-cli", ex.Message, StringComparison.Ordinal); // #465: never the value
        Assert.Contains("none is registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNameRegisteredModules_WhenKeyIsUnknown()
    {
        var config = LlmSettings.ReadDefault(Settings(("Llm:Provider", "other-cli")));

        var ex = Assert.Throws<NotSupportedException>(() => Factory([new FakeLlmProviderModule()]).Create(config));

        Assert.Contains("registered: fake-cli", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldBuildThroughEntrance_WhenModuleRegistered()
    {
        var module = new FakeLlmProviderModule();
        var sink = new MockLlmUsageSink();
        var config = LlmSettings.ReadDefault(Settings(("Llm:Provider", "fake-cli"), ("Llm:Model", "opus")));

        var adapter = Assert.IsType<LlmProviderAdapter>(Factory([module], sink).Create(config));
        var response = await adapter.UnderlyingProvider.ChatAsync(
            [new LlmMessage { Role = "user", Content = "hello" }], config, TestContext.Current.CancellationToken);

        Assert.Equal("from the module", response.Content);
        var built = Assert.Single(module.Built);
        Assert.Same(built, MeteredLlmProvider.Unwrap(adapter.UnderlyingProvider));
        Assert.Single(built.ChatCalls);
        // The meter saw the call: the module's provider is on the metered path.
        Assert.Equal(12, sink.TotalTokens);
        Assert.Equal("opus", Assert.Single(module.Configs).Model);
    }

    [Fact]
    public void ShouldKeepInference_WhenProviderKeyAbsent()
    {
        var module = new FakeLlmProviderModule();
        var config = LlmSettings.ReadDefault(Settings(("Llm:Model", "claude-sonnet-4-5"), ("Llm:ApiKey", "sk-ant-test")));

        var adapter = Assert.IsType<LlmProviderAdapter>(Factory([module]).Create(config));

        Assert.Empty(module.Built);
        Assert.Equal("AnthropicLlmProvider", MeteredLlmProvider.Unwrap(adapter.UnderlyingProvider).GetType().Name);
    }

    [Theory]
    [InlineData("claude-opus-4-1")]
    [InlineData("gpt-5")]
    [InlineData("opus")]
    public void ShouldNotInferFromModelName_WhenProviderKeySet(string model)
    {
        var module = new FakeLlmProviderModule();
        var config = LlmSettings.ReadDefault(Settings(("Llm:Provider", "Fake-CLI"), ("Llm:Model", model)));

        Factory([module]).Create(config);

        Assert.Single(module.Built);
    }

    [Fact]
    public void ShouldBuildEachProfileByItsOwnRule_WhenOneNamesAModuleAndItsFallbackAVendor()
    {
        var module = new FakeLlmProviderModule();
        var profiles = LlmSettings.ReadProfiles(Settings(
            ("Llm:Profiles:main:Provider", "fake-cli"),
            ("Llm:Profiles:main:Model", "opus"),
            ("Llm:Profiles:main:Fallback:0", "api"),
            ("Llm:Profiles:api:Model", "claude-sonnet-4-5"),
            ("Llm:Profiles:api:ApiKey", "sk-ant-test")));
        var factory = Factory([module]);

        var providers = profiles.ToDictionary(p => p.Name, p => factory.Create(p.Config), StringComparer.Ordinal);

        Assert.Single(module.Built);
        Assert.Equal("fake-cli", MeteredLlmProvider.Unwrap(Assert.IsType<LlmProviderAdapter>(providers["main"]).UnderlyingProvider).Name);
        Assert.Equal("AnthropicLlmProvider",
            MeteredLlmProvider.Unwrap(Assert.IsType<LlmProviderAdapter>(providers["api"]).UnderlyingProvider).GetType().Name);
    }

    [Fact]
    public async Task ShouldBuildByExplicitKey_WhenCreateNamesModuleKey()
    {
        var module = new FakeLlmProviderModule();
        var sink = new MockLlmUsageSink();
        var config = LlmConfig.Create("opus");

        var adapter = Assert.IsType<LlmProviderAdapter>(Factory([module], sink).Create("fake-cli", config));
        await adapter.UnderlyingProvider.ChatAsync(
            [new LlmMessage { Role = "user", Content = "hello" }], config, TestContext.Current.CancellationToken);

        Assert.Single(module.Built);
        Assert.Equal(12, sink.TotalTokens);
    }

    [Fact]
    public void ShouldThrow_WhenCreateNamesAKeyNoModuleAnswers()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Factory([new FakeLlmProviderModule()]).Create("other-cli", LlmConfig.Create("opus")));

        Assert.Contains("other-cli", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldTrimAndLowercaseTheKey_WhenSettingsPadIt()
    {
        var module = new FakeLlmProviderModule();
        var config = LlmSettings.ReadDefault(Settings(("Llm:Provider", "  Fake-CLI  ")));

        Factory([module]).Create(config);

        Assert.Single(module.Built);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("google")]
    [InlineData("azure")]
    [InlineData("custom")]
    [InlineData("docker-model-runner")]
    [InlineData("Fake-CLI")]
    [InlineData(" fake-cli")]
    public void ShouldRefuse_WhenModuleKeyIsBuiltInOrNotInWireForm(string key)
    {
        var ex = Assert.Throws<ArgumentException>(() => Factory([new FakeLlmProviderModule { Key = key }]));

        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuse_WhenTwoModulesAnswerOneKey()
    {
        var ex = Assert.Throws<ArgumentException>(() => Factory([new FakeLlmProviderModule(), new FakeLlmProviderModule()]));

        Assert.Contains("fake-cli", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuse_WhenModuleAnswersMockKey()
    {
        Assert.Throws<ArgumentException>(() => Factory([new FakeLlmProviderModule { Key = "mock" }]));
    }

    [Fact]
    public void ShouldWarnBaseUrlIgnored_WhenModuleSectionCarriesAddress()
    {
        using var loggers = new RecordingLoggerFactory();
        var config = LlmSettings.ReadDefault(Settings(("Llm:Provider", "fake-cli"), ("Llm:BaseUrl", "https://api.anthropic.com")));

        Factory([new FakeLlmProviderModule()], loggers: loggers).Create(config);

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("BaseUrl is ignored", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldHandModuleTheFactoryServices_WhenBuilding()
    {
        var module = new FakeLlmProviderModule();
        var fileSystem = new FakeFileSystemService();
        var factory = new LlmProviderFactory(new NoHttpClientFactory(), NullLoggerFactory.Instance, fileSystem,
            new LlmProviderFactoryOptions { Modules = [module] });

        factory.Create(LlmSettings.ReadDefault(Settings(("Llm:Provider", "fake-cli"))));

        var context = Assert.Single(module.Contexts);
        Assert.Same(fileSystem, context.FileSystem);
        Assert.Same(NullLoggerFactory.Instance, context.LoggerFactory);
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("openai")]
    [InlineData("google")]
    [InlineData("custom")]
    [InlineData("docker-model-runner")]
    public void ShouldRefuseVendorKey_WhenProviderNamesCatalogue(string vendor)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadDefault(Settings(("Llm:Provider", vendor))));

        Assert.Contains("Llm:Provider", ex.Message, StringComparison.Ordinal);
        Assert.Contains("vendor of the catalogue", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseResponses_WhenProviderIsModule()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadDefault(
            Settings(("Llm:Provider", "fake-cli"), ("Llm:Responses", "/llm-mock/a.jsonl"))));

        Assert.Contains("Llm:Responses", ex.Message, StringComparison.Ordinal);
    }
}
