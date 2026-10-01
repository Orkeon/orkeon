using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class LlmDefaultsTests
{
    private static T EvalWithProvider<T>(ILoggerFactory? lf, ILlmProvider? provider, string js)
    {
        var engine = new JsEngineFactory(loggerFactory: lf, llmProvider: provider).Create();
        return (T)engine.Evaluate(js).ToObject()!;
    }

    private sealed class StubLlmProvider : ILlmProvider
    {
        public StubLlmProvider(string name, string? model = null) { Name = name; BaseConfig = model is null ? null : LlmConfig.Create(model); }
        public string Name { get; }
        public LlmConfig? BaseConfig { get; }
        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Empty });
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Empty });
    }

    [Fact]
    public void llm_default_is_the_host_provider_on_its_configured_model()
    {
        // GAP-12: llm.default_ says what an agent will really use — the host's provider, and the
        // model that provider is configured with. It used to report the platform default model
        // for a provider bound through DI, which `.llm(llm.default_)` then forced onto the host.
        using var lf = new RecordingLoggerFactory();
        var def = EvalWithProvider<JsLlmConfig>(lf, new StubLlmProvider("deepseek", "deepseek-v4-flash"), "llm.default_");

        Assert.Equal("deepseek", def.provider);
        Assert.Equal("deepseek-v4-flash", def.model);
        Assert.DoesNotContain(lf.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void llm_default_without_a_host_provider_is_the_undefined_echo_and_warns()
    {
        using var lf = new RecordingLoggerFactory();

        var def = EvalWithProvider<JsLlmConfig>(lf, null, "llm.default_");

        Assert.Equal("undefined", def.provider);
        Assert.True(lf.Logger.HasEntry(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("llm.default_", StringComparison.Ordinal)));
    }

    [Fact]
    public void llm_default_skips_UndefinedLlmProvider_when_DI_only_has_the_echo()
    {
        // The host may bind UndefinedLlmProvider explicitly (lean-runtime path
        // without API key). That's not a real default — fall back to the warning
        // so the operator notices.
        using var lf = new RecordingLoggerFactory();

        var def = EvalWithProvider<JsLlmConfig>(lf, new UndefinedLlmProvider(), "llm.default_");

        Assert.Equal("undefined", def.provider);
        Assert.True(lf.Logger.HasEntry(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public void llm_default_with_overrides_keeps_provider_and_swaps_settings()
    {
        var tweaked = EvalWithProvider<JsLlmConfig>(null, new StubLlmProvider("openai", "gpt-4o-mini"),
            "llm.default_.with({ model: 'gpt-4o', temperature: 0.0, maxTokens: 1000 });");

        Assert.Equal("openai", tweaked.provider);
        Assert.Equal("gpt-4o", tweaked.model);
        Assert.Equal(0.0, tweaked.temperature);
        Assert.Equal(1000, tweaked.maxTokens);
    }

    [Fact]
    public void llm_model_is_the_default_on_another_model()
    {
        var cfg = EvalWithProvider<JsLlmConfig>(null, new StubLlmProvider("openai", "gpt-4o-mini"),
            "llm.model('gpt-4o', { maxTokens: 200 });");

        Assert.Equal("openai", cfg.provider);
        Assert.Equal("gpt-4o", cfg.model);
        Assert.Equal(200, cfg.maxTokens);
    }

    [Theory]
    [InlineData("llm.default_.with({ baseUrl: 'http://elsewhere' })", "baseUrl")]
    [InlineData("llm.default_.with({ provider: 'anthropic' })", "provider")]
    [InlineData("llm.model('gpt-4o', { apiKey: 'k' })", "apiKey")]
    [InlineData("llm.model(42)", "llm.model(name)")]
    [InlineData("llm.default_.with({ model: '' })", "model name")]
    public void An_llm_setting_the_runtime_does_not_apply_is_refused(string js, string named)
    {
        var ex = Assert.ThrowsAny<Exception>(() => EvalWithProvider<JsLlmConfig>(null, new StubLlmProvider("openai", "gpt-4o-mini"), js));

        Assert.Contains(named, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndefinedLlmProvider_complete_echoes_the_prompt()
    {
        var p = new UndefinedLlmProvider();

        var resp = await p.GenerateAsync("hello world", null, CancellationToken.None);

        Assert.Equal("hello world", resp.Content);
    }

    [Fact]
    public async Task UndefinedLlmProvider_chat_returns_last_user_message()
    {
        var p = new UndefinedLlmProvider();
        var msgs = new[]
        {
            LlmMessage.System("you are helpful"),
            LlmMessage.User("first ask"),
            LlmMessage.Assistant("response"),
            LlmMessage.User("second ask"),
        };

        var resp = await p.ChatAsync(msgs, null, CancellationToken.None);

        Assert.Equal("second ask", resp.Content);
    }
}
