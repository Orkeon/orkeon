using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class LlmDefaultsTests
{
    private static IConfiguration Cfg(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    private static T Eval<T>(IConfiguration? cfg, ILoggerFactory? lf, string js)
    {
        var engine = new JsEngineFactory(loggerFactory: lf, configuration: cfg).Create();
        return (T)engine.Evaluate(js).ToObject()!;
    }

    private static T EvalWithProvider<T>(IConfiguration? cfg, ILoggerFactory? lf, ILlmProvider? provider, string js)
    {
        var engine = new JsEngineFactory(
            loggerFactory: lf, configuration: cfg, llmProvider: provider).Create();
        return (T)engine.Evaluate(js).ToObject()!;
    }

    private sealed class StubLlmProvider : ILlmProvider
    {
        public StubLlmProvider(string name) { Name = name; }
        public string Name { get; }
        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Empty });
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Empty });
    }

    [Fact]
    public void llm_openai_returns_provider_named_openai_with_supplied_model()
    {
        var built = Eval<JsLlmConfig>(null, null, """llm.openai({ model: "gpt-4o", temperature: 0.2 });""");

        Assert.Equal("openai", built.provider);
        Assert.Equal("gpt-4o", built.model);
        Assert.Equal(0.2, built.temperature);
    }

    [Fact]
    public void llm_grok_returns_provider_named_grok_with_its_default_model()
    {
        var built = Eval<JsLlmConfig>(null, null, """llm.grok({});""");

        Assert.Equal("grok", built.provider);
        Assert.Equal("grok-4.6", built.model);
    }

    [Fact]
    public void llm_minimax_returns_provider_named_minimax_with_its_default_model()
    {
        var built = Eval<JsLlmConfig>(null, null, """llm.minimax({});""");

        Assert.Equal("minimax", built.provider);
        Assert.Equal("MiniMax-M2", built.model);
    }

    [Fact]
    public void llm_default_resolves_OpenAI_when_configured()
    {
        var cfg = Cfg(("Orkeon:DefaultLlmProvider", "openai"));
        var def = Eval<JsLlmConfig>(cfg, null, "llm.default");

        Assert.Equal("openai", def.provider);
    }

    [Fact]
    public void llm_default_resolves_Anthropic_when_configured()
    {
        var cfg = Cfg(("Orkeon:DefaultLlmProvider", "anthropic"));
        var def = Eval<JsLlmConfig>(cfg, null, "llm.default");

        Assert.Equal("anthropic", def.provider);
    }

    [Fact]
    public void llm_default_with_no_config_falls_back_to_undefined_and_logs_warning()
    {
        using var lf = new RecordingLoggerFactory();

        var def = Eval<JsLlmConfig>(null, lf, "llm.default");

        Assert.Equal("undefined", def.provider);
        Assert.True(lf.Logger.HasEntry(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("DefaultLlmProvider")));
    }

    [Fact]
    public void llm_default_underscore_alias_resolves_same_value_as_llm_default()
    {
        // `orkeon-script` typings ship `llm.default_` because the conventional twin in
        // .ork.ts authoring avoids the `default` keyword. The runtime must accept both
        // names so transpiled scripts that read `llm.default_` (as the experiment-07
        // fixtures do) don't crash with "Cannot read property 'with' of undefined".
        var cfg = Cfg(("Orkeon:DefaultLlmProvider", "anthropic"));
        var viaAlias = Eval<JsLlmConfig>(cfg, null, "llm.default_");
        var viaCanonical = Eval<JsLlmConfig>(cfg, null, "llm.default");

        Assert.Equal(viaCanonical.provider, viaAlias.provider);
        Assert.Equal(viaCanonical.model, viaAlias.model);
    }

    [Fact]
    public void llm_default_with_overrides_keeps_provider_and_swaps_temperature()
    {
        var cfg = Cfg(("Orkeon:DefaultLlmProvider", "openai"));
        var tweaked = Eval<JsLlmConfig>(cfg, null, "llm.default.with({ temperature: 0.0, maxTokens: 1000 });");

        Assert.Equal("openai", tweaked.provider);
        Assert.Equal(0.0, tweaked.temperature);
        Assert.Equal(1000, tweaked.maxTokens);
    }

    [Fact]
    public void llm_default_resolves_from_DI_provider_when_no_config_key()
    {
        // Friction #9c: when the host's DI binds an ILlmProvider (the same one the
        // orchestrator uses), `llm.default_` in a .ork.ts script must report that
        // provider — no UndefinedLlm echo, no warning.
        using var lf = new RecordingLoggerFactory();
        var provider = new StubLlmProvider("deepseek-v4-flash");

        var def = EvalWithProvider<JsLlmConfig>(null, lf, provider, "llm.default_");

        Assert.Equal("deepseek-v4-flash", def.provider);
        Assert.DoesNotContain(lf.Logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DefaultLlmProvider"));
    }

    [Fact]
    public void llm_default_DI_provider_loses_to_explicit_config_key()
    {
        // Operator override: `Orkeon:DefaultLlmProvider` pins the logical provider
        // name independently of which ILlmProvider DI happens to bind. Useful for
        // testing or for environments where multiple providers are registered.
        var cfg = Cfg(("Orkeon:DefaultLlmProvider", "anthropic"));
        var provider = new StubLlmProvider("deepseek-v4-flash");

        var def = EvalWithProvider<JsLlmConfig>(cfg, null, provider, "llm.default_");

        Assert.Equal("anthropic", def.provider);
    }

    [Fact]
    public void llm_default_skips_UndefinedLlmProvider_when_DI_only_has_the_echo()
    {
        // The host may bind UndefinedLlmProvider explicitly (lean-runtime path
        // without API key). That's not a real default — fall back to the warning
        // so the operator notices.
        using var lf = new RecordingLoggerFactory();
        var provider = new UndefinedLlmProvider();

        var def = EvalWithProvider<JsLlmConfig>(null, lf, provider, "llm.default_");

        Assert.Equal("undefined", def.provider);
        Assert.True(lf.Logger.HasEntry(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("DefaultLlmProvider")));
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
