using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.LLMs.Mock;

public sealed class MockProfileFactoryTests
{
    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("the mock provider makes no network call");
    }

    private static LlmProviderFactory Factory() => new(
        new NoHttpClientFactory(),
        NullLoggerFactory.Instance,
        fileSystem: new FakeFileSystemService().AddMount("/llm-mock").AddFile("/llm-mock/a.jsonl", """{"match":"ping","content":"pong"}"""));

    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => KeyValuePair.Create<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public async Task Profile_with_provider_mock_resolves_the_jsonl_provider()
    {
        var configuration = Settings(("Llm:Profiles:offline:Provider", "mock"), ("Llm:Profiles:offline:Responses", "/llm-mock/a.jsonl"));
        var (name, config) = Assert.Single(LlmSettings.ReadProfiles(configuration));

        var provider = Factory().Create(config);

        Assert.Equal("offline", name);
        var adapter = Assert.IsType<LlmProviderAdapter>(provider);
        Assert.Equal("mock", adapter.UnderlyingProvider.Name);
        var response = await adapter.UnderlyingProvider.GenerateAsync("ping", config, TestContext.Current.CancellationToken);
        Assert.Equal("pong", response.Content);
    }

    [Fact]
    public void Default_section_with_provider_mock_resolves_the_jsonl_provider()
    {
        var configuration = Settings(("Llm:Provider", "mock"), ("Llm:Responses", "/llm-mock/a.jsonl"));

        var config = LlmSettings.ReadDefault(configuration);

        Assert.True(LlmSettings.HasDefault(configuration));
        Assert.Equal("mock", Assert.IsType<LlmProviderAdapter>(Factory().Create(config)).UnderlyingProvider.Name);
    }

    [Fact]
    public void Mock_without_responses_is_refused_with_the_key_to_fix()
    {
        var configuration = Settings(("Llm:Profiles:offline:Provider", "mock"));

        var ex = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadProfiles(configuration));

        Assert.Contains("Llm:Profiles:offline:Responses", ex.Message);
    }

    [Fact]
    public void Building_a_mock_provider_warns_once_naming_the_profile_and_the_file()
    {
        using var loggers = new Orkeon.Tests.Shared.Doubles.RecordingLoggerFactory();
        var factory = new LlmProviderFactory(
            new NoHttpClientFactory(),
            loggers,
            new FakeFileSystemService().AddMount("/llm-mock").AddFile("/llm-mock/a.jsonl", """{"match":"ping","content":"pong"}"""));
        var (_, config) = Assert.Single(LlmSettings.ReadProfiles(
            Settings(("Llm:Profiles:offline:Provider", "mock"), ("Llm:Profiles:offline:Responses", "/llm-mock/a.jsonl"))));

        factory.Create(config);

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("offline", warning.Message, StringComparison.Ordinal);
        Assert.Contains("/llm-mock/a.jsonl", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_section_is_named_default_in_the_warning()
    {
        using var loggers = new Orkeon.Tests.Shared.Doubles.RecordingLoggerFactory();
        var factory = new LlmProviderFactory(new NoHttpClientFactory(), loggers, new FakeFileSystemService());

        factory.Create(LlmSettings.ReadDefault(Settings(("Llm:Provider", "mock"), ("Llm:Responses", "/llm-mock/a.jsonl"))));

        var warning = Assert.Single(loggers.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("default", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_key_other_than_mock_is_refused_naming_the_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadProfiles(
            Settings(("Llm:Profiles:a:Provider", "openai"))));

        Assert.Contains("Llm:Profiles:a:Provider", ex.Message, StringComparison.Ordinal);
        Assert.Contains("mock", ex.Message, StringComparison.Ordinal);
    }
}
