using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Bindings;

/// <summary>
/// LLM Response Format — call-time integration via <see cref="JsLlmFacade"/>.
/// Verifies that <c>llm.complete(prompt, { responseFormat: "json_object" })</c>
/// reaches the captured <see cref="StubLlmProvider"/> as an
/// <see cref="LlmConfig"/> whose <see cref="LlmConfig.ResponseFormat"/> is set.
/// </summary>
public sealed class LlmNamespaceBindingResponseFormatTests
{
    [Fact]
    public async Task complete_ForwardsResponseFormat_FromOpts()
    {
        using var engine = new Engine();
        var provider = new StubLlmProvider();
        provider.RespondWith(new LlmResponse { Content = "{}" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var opts = await engine.EvaluateAsync("({ responseFormat: 'json_object' })", cancellationToken: TestContext.Current.CancellationToken);
        await facade.complete("Return as json", opts);

        Assert.NotNull(provider.LastConfig);
        Assert.NotNull(provider.LastConfig!.ResponseFormat);
        Assert.Equal("json_object", provider.LastConfig.ResponseFormat!.Type);
    }

    [Fact]
    public async Task chat_ForwardsResponseFormat_FromOpts()
    {
        using var engine = new Engine();
        var provider = new StubLlmProvider();
        provider.RespondToChatWith(new LlmResponse { Content = "{}", TokensUsed = 0 });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var msgs = await engine.EvaluateAsync("[{role:'user', content:'hi json'}]", cancellationToken: TestContext.Current.CancellationToken);
        var opts = await engine.EvaluateAsync("({ responseFormat: 'json_object' })", cancellationToken: TestContext.Current.CancellationToken);
        await facade.chat(msgs, opts);

        Assert.NotNull(provider.LastConfig?.ResponseFormat);
        Assert.Equal("json_object", provider.LastConfig!.ResponseFormat!.Type);
    }

    [Fact]
    public async Task complete_LeavesResponseFormatNull_WhenOptsOmitIt()
    {
        using var engine = new Engine();
        var provider = new StubLlmProvider();
        provider.RespondWith(new LlmResponse { Content = "ok" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        await facade.complete("p", null);

        // No opts → no config injection (ConfigFrom returns null → provider receives null config)
        Assert.Null(provider.LastConfig?.ResponseFormat);
    }
}
