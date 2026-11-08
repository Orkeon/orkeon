using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.LLMs.Replay;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Infrastructure.Tests.LLMs.Replay.CassetteFixtures;

namespace Orkeon.Infrastructure.Tests.LLMs.Replay;

/// <summary>FWK-11: the factory puts the cassette decorators under the call-mode layer.</summary>
public sealed class LlmProviderFactoryReplayTests
{
    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("a replay makes no network call");
    }

    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => KeyValuePair.Create<string, string?>(p.Key, p.Value))).Build();

    private static LlmConfig OfflineProfile() =>
        Assert.Single(LlmSettings.ReadProfiles(Settings(("Llm:Profiles:offline:Provider", "mock"), ("Llm:Profiles:offline:Responses", "/llm-mock/a.jsonl")))).Config;

    private static FakeFileSystemService WithResponses() =>
        NewFileSystem().AddMount("/llm-mock").AddFile("/llm-mock/a.jsonl", """{"match":"ping","content":"pong"}""");

    private static LlmProviderFactory Factory(FakeFileSystemService fs, LlmReplayMode mode) =>
        new(new NoHttpClientFactory(), NullLoggerFactory.Instance, fs, replay: new LlmReplayOptions { Mode = mode, Cassette = CassettePath });

    private static ILlmProvider Provider(IBasicLlmProvider created) => Assert.IsType<LlmProviderAdapter>(created).UnderlyingProvider;

    [Fact]
    public async Task A_provider_built_in_record_mode_writes_its_calls_under_its_profile()
    {
        var fs = WithResponses();
        var provider = Provider(Factory(fs, LlmReplayMode.Record).Create(OfflineProfile()));

        var response = await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", response.Content);
        var line = Assert.Single(Lines(await ReadAsync(fs)));
        Assert.Contains("\"profile\":\"offline\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_built_in_replay_mode_answers_from_the_cassette_and_never_calls_the_provider_underneath()
    {
        var recording = WithResponses();
        await Provider(Factory(recording, LlmReplayMode.Record).Create(OfflineProfile()))
            .ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);
        var cassette = await ReadAsync(recording);

        // The responses file is gone: a mock reached underneath would answer with an error.
        var replaying = NewFileSystem().AddFile(CassettePath, cassette);
        var provider = Provider(Factory(replaying, LlmReplayMode.Replay).Create(OfflineProfile()));

        var response = await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", response.Content);
        Assert.Null(response.Error);
        await Assert.ThrowsAsync<ReplayMissException>(
            () => provider.ChatAsync(User("never recorded"), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Without_a_mode_the_factory_adds_no_layer()
    {
        var provider = Provider(Factory(WithResponses(), LlmReplayMode.Off).Create(OfflineProfile()));

        Assert.IsNotType<RecordingLlmProvider>(MeteredLlmProvider.Unwrap(provider));
        Assert.IsType<Orkeon.Infrastructure.LLMs.Mock.JsonlMockLlmProvider>(MeteredLlmProvider.Unwrap(provider));
    }

    [Theory]
    [InlineData(LlmReplayMode.Record)]
    [InlineData(LlmReplayMode.Replay)]
    public void A_vendor_provider_is_still_found_under_the_cassette_layer(LlmReplayMode mode)
    {
        var config = LlmConfig.Create("claude-3-opus");

        var provider = Provider(Factory(new FakeFileSystemService(), mode).Create(config));

        Assert.IsType<AnthropicLlmProvider>(MeteredLlmProvider.Unwrap(provider));
    }

    [Fact]
    public void A_named_profile_carries_its_name_and_the_default_does_not()
    {
        var named = OfflineProfile();
        var fallback = LlmSettings.ReadDefault(Settings(("Llm:Provider", "mock"), ("Llm:Responses", "/llm-mock/a.jsonl")));

        Assert.Equal("offline", named.Profile);
        Assert.Null(fallback.Profile);
    }

    [Fact]
    public void The_replay_section_is_read_and_is_not_a_default_profile()
    {
        var configuration = Settings(("Llm:Replay:Mode", "record"), ("Llm:Replay:Cassette", "/llm-cassette/run.jsonl"));
        var services = new ServiceCollection();
        services.AddOrkeonLlmProfiles(configuration);
        using var container = services.BuildServiceProvider();

        var options = container.GetRequiredService<LlmReplayOptions>();

        Assert.Equal(LlmReplayMode.Record, options.Mode);
        Assert.Equal("/llm-cassette/run.jsonl", options.Cassette);
        Assert.False(LlmSettings.HasDefault(configuration));
    }

    [Fact]
    public void A_mode_without_a_cassette_or_an_unknown_mode_refuses_the_start_naming_the_key()
    {
        var noCassette = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadReplay(Settings(("Llm:Replay:Mode", "replay"))));
        var unknown = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadReplay(Settings(("Llm:Replay:Mode", "tape"), ("Llm:Replay:Cassette", "/c/x.jsonl"))));

        Assert.Contains("Llm:Replay:Cassette", noCassette.Message, StringComparison.Ordinal);
        Assert.Contains("Llm:Replay:Mode", unknown.Message, StringComparison.Ordinal);
    }
}
