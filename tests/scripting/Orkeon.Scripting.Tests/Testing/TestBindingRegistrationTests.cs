using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Tests.Testing;

/// <summary>
/// The global <c>test</c> exists only under <c>orkeon run --test</c> (<see cref="JsEngineFactory.EnableTestNamespace"/>),
/// and there an expectation of <c>test.mockLlm</c> is served before the configured profile.
/// </summary>
public sealed class TestBindingRegistrationTests
{
    [Fact]
    public void Test_global_is_absent_without_the_test_switch()
    {
        var engine = new JsEngineFactory().Create();

        Assert.Equal("undefined", engine.Evaluate("typeof test").AsString());
    }

    [Fact]
    public void Test_global_is_present_with_the_test_switch()
    {
        var engine = new JsEngineFactory { EnableTestNamespace = true }.Create();

        Assert.Equal("object", engine.Evaluate("typeof test").AsString());
        Assert.Equal("function", engine.Evaluate("typeof test.mockTool").AsString());
    }

    [Fact]
    public async Task Mock_expectation_is_served_before_the_profile_and_the_rest_goes_to_it()
    {
        var configured = new StubLlmProvider().RespondWith(new LlmResponse { Content = "from-profile" });
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var host = new ScriptHost(
            new DiskBackedFileSystemService(fixtures, "/scripts"),
            new JsEngineFactory(llmProvider: configured) { EnableTestNamespace = true });

        var raw = await host.RunAsync("/scripts/test-override.ork.ts", TestContext.Current.CancellationToken);

        var map = Assert.IsType<IDictionary<string, object?>>(raw, exactMatch: false);
        Assert.Equal("from-test|from-profile", map["answers"]);
        Assert.Equal(["other"], configured.GenerateCalls);
    }

    [Fact]
    public async Task Without_a_configured_provider_an_unmatched_prompt_is_answered_empty()
    {
        var provider = new Orkeon.Scripting.Testing.TestOverrideLlmProvider(null);
        provider.Mock.When("ping").Respond("pong");

        var hit = await provider.GenerateAsync("ping", null, TestContext.Current.CancellationToken);
        var miss = await provider.GenerateAsync("other", null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", hit.Content);
        Assert.Equal(string.Empty, miss.Content);
    }

    [Fact]
    public async Task The_override_keeps_the_streaming_of_the_configured_provider_for_unmatched_prompts()
    {
        var inner = new FakeStreamingLlmProvider("He", "llo");
        var provider = new Orkeon.Scripting.Testing.TestOverrideLlmProvider(inner);
        var streaming = Assert.IsType<Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider>(provider, exactMatch: false);
        provider.Mock.When("ping").Respond("pong");

        var chunks = new List<string>();
        await foreach (var chunk in streaming.GenerateStreamingAsync("other", null, TestContext.Current.CancellationToken))
            chunks.Add(chunk);

        Assert.True(streaming.SupportsStreaming);
        Assert.True(streaming.StreamsNatively);
        Assert.Equal(["He", "llo"], chunks);
        Assert.Equal(1, inner.StreamingCalls);
    }

    [Fact]
    public async Task A_matched_prompt_streams_the_mock_answer_in_one_chunk_without_reaching_the_configured_provider()
    {
        var inner = new FakeStreamingLlmProvider("never");
        var provider = new Orkeon.Scripting.Testing.TestOverrideLlmProvider(inner);
        var streaming = Assert.IsType<Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider>(provider, exactMatch: false);
        provider.Mock.When("ping").Respond("pong");
        var events = new List<LlmStreamEvent>();

        await foreach (var e in streaming.ChatStreamingAsync(
            [new LlmMessage { Role = "user", Content = "ping" }], null, TestContext.Current.CancellationToken))
            events.Add(e);

        Assert.Equal("pong", events[0].Delta);
        Assert.Equal("pong", events[^1].FinalResponse!.Content);
        Assert.Equal(0, inner.StreamingCalls);
        Assert.Single(provider.Mock.ChatCalls);
    }

    [Fact]
    public void An_override_over_a_buffered_provider_does_not_claim_streaming()
    {
        var provider = new Orkeon.Scripting.Testing.TestOverrideLlmProvider(new StubLlmProvider());

        var streaming = Assert.IsType<Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider>(provider, exactMatch: false);

        Assert.False(streaming.SupportsStreaming);
    }
}
