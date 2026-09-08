using Jint;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// Call-time LLM settings (<c>{ responseFormat }</c>, <c>{ llm: { model } }</c>) must PATCH the
/// provider's own <see cref="ILlmProvider.BaseConfig"/>, not replace it.
/// </summary>
/// <remarks>
/// Downstream a per-call config is substituted wholesale — <c>HttpLlmProviderBase.CreateHttpClient</c>
/// resolves <c>requestConfig ?? Config</c> — so a call-time config built from
/// <see cref="LlmConfig.Default"/> reaches the transport with no API key, no base URL and the
/// default timeout. A script asking for a JSON response would lose its credentials as a side
/// effect. Stub providers ignore those fields, which is why only these assertions catch it.
/// </remarks>
public sealed class JsLlmFacadeCallConfigTests
{
    private const string Key = "sk-test-key";
    private static readonly Uri Endpoint = new("https://api.example.test/v1");

    private static StubLlmProvider Configured() => new()
    {
#pragma warning disable CS0618 // Direct ApiKey: this is exactly the field the defect dropped.
        BaseConfig = LlmConfig.Create("boot-model", Key) with { BaseUrl = Endpoint, TimeoutSeconds = 120 },
#pragma warning restore CS0618
    };

    private static void AssertCredentialsSurvived(LlmConfig? config)
    {
        Assert.NotNull(config);
#pragma warning disable CS0618
        Assert.Equal(Key, config!.ApiKey);
#pragma warning restore CS0618
        Assert.Equal(Endpoint, config.BaseUrl);
        Assert.Equal(120, config.TimeoutSeconds);
    }

    [Fact]
    public async Task complete_ResponseFormatOverride_KeepsCredentialsAndModel()
    {
        using var engine = new Engine();
        var provider = Configured();
        provider.RespondWith(new LlmResponse { Content = "{}" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var opts = await engine.EvaluateAsync("({ responseFormat: 'json_object' })", cancellationToken: TestContext.Current.CancellationToken);
        await facade.complete("Return as json", opts);

        AssertCredentialsSurvived(provider.LastConfig);
        Assert.Equal("boot-model", provider.LastConfig!.Model);
        Assert.Equal("json_object", provider.LastConfig.ResponseFormat!.Type);
    }

    [Fact]
    public async Task chat_ModelOverride_ChangesOnlyTheModel()
    {
        using var engine = new Engine();
        var provider = Configured();
        provider.RespondToChatWith(new LlmResponse { Content = "ok" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var msgs = await engine.EvaluateAsync("[{role:'user', content:'hi'}]", cancellationToken: TestContext.Current.CancellationToken);
        var opts = await engine.EvaluateAsync("({ llm: { model: 'other-model' } })", cancellationToken: TestContext.Current.CancellationToken);
        await facade.chat(msgs, opts);

        AssertCredentialsSurvived(provider.LastConfig);
        Assert.Equal("other-model", provider.LastConfig!.Model);
    }

    [Fact]
    public async Task act_ModelOverride_ReachesTheProvider_WithCredentialsIntact()
    {
        // An interactive loop hot-swaps the model per turn by passing
        // `{ llm: { model } }` to act(); the agent's boot provider must keep its credentials.
        using var engine = new Engine();
        var provider = Configured();
        provider.RespondToChatWith(new LlmResponse { Content = "done" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var opts = await engine.EvaluateAsync("({ maxIterations: 1, llm: { model: 'turn-model' } })", cancellationToken: TestContext.Current.CancellationToken);
        await facade.act("do the thing", opts);

        AssertCredentialsSurvived(provider.LastConfig);
        Assert.Equal("turn-model", provider.LastConfig!.Model);
    }

    [Fact]
    public async Task act_WithoutModelOverride_KeepsTheBootModel()
    {
        using var engine = new Engine();
        var provider = Configured();
        provider.RespondToChatWith(new LlmResponse { Content = "done" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var opts = await engine.EvaluateAsync("({ maxIterations: 1 })", cancellationToken: TestContext.Current.CancellationToken);
        await facade.act("do the thing", opts);

        // No LLM setting in the bag ⇒ no per-call config; act() falls back to BaseConfig itself.
        Assert.Equal("boot-model", provider.LastConfig!.Model);
        AssertCredentialsSurvived(provider.LastConfig);
    }

    [Fact]
    public async Task complete_BothOverrides_ApplyOnTopOfTheSameBase()
    {
        using var engine = new Engine();
        var provider = Configured();
        provider.RespondWith(new LlmResponse { Content = "{}" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var opts = await engine.EvaluateAsync(
            "({ responseFormat: 'json_object', llm: { model: 'other-model' } })",
            cancellationToken: TestContext.Current.CancellationToken);
        await facade.complete("p", opts);

        AssertCredentialsSurvived(provider.LastConfig);
        Assert.Equal("other-model", provider.LastConfig!.Model);
        Assert.Equal("json_object", provider.LastConfig.ResponseFormat!.Type);
    }

    [Fact]
    public async Task complete_NoOptions_SendsNoPerCallConfig()
    {
        // Unchanged contract: an empty bag must not manufacture a config at all, otherwise
        // every provider would receive a patched copy of its own settings for nothing.
        using var engine = new Engine();
        var provider = Configured();
        provider.RespondWith(new LlmResponse { Content = "ok" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        await facade.complete("p", null);

        Assert.Null(provider.LastConfig);
    }

    [Fact]
    public async Task complete_ProviderWithoutBaseConfig_StillFallsBackToDefault()
    {
        // A third-party ILlmProvider that does not expose BaseConfig (the interface default is
        // null) must keep working — the override then applies to LlmConfig.Default() as before.
        using var engine = new Engine();
        var provider = new StubLlmProvider();
        provider.RespondWith(new LlmResponse { Content = "{}" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var opts = await engine.EvaluateAsync("({ responseFormat: 'json_object' })", cancellationToken: TestContext.Current.CancellationToken);
        await facade.complete("p", opts);

        Assert.Equal("json_object", provider.LastConfig!.ResponseFormat!.Type);
    }
}
