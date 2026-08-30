using System.Net;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// With <c>reasoning_effort</c> on, Mistral answers with <c>message.content</c> as an ARRAY
/// of typed chunks — <c>{"type":"thinking","thinking":[{"type":"text",...}]}</c> for the
/// trace, <c>{"type":"text","text":...}</c> for the visible answer — while the shared parse
/// only accepted a string and read the whole message as empty. Measured 2026-08-30: M7
/// archived <c>accepted, no reasoning trace returned, tokens=243</c> with a Failed verdict —
/// 243 tokens billed, response and trace both dropped. A user enabling thinking on Mistral
/// lost the entire answer.
/// </summary>
public sealed class MistralReasoningContentParseTests : IDisposable
{
    /// <summary>Verbatim shape of a mistral-medium-2604 reasoning reply (2026-08-30).</summary>
    private const string ReasoningResponse =
        """
        {"choices":[{"message":{"role":"assistant","tool_calls":null,"content":[
          {"type":"thinking","thinking":[{"type":"text","text":"Rayleigh scattering favours short wavelengths."}]},
          {"type":"text","text":"The sky is blue because of Rayleigh scattering."}
        ]}}],"usage":{"prompt_tokens":10,"completion_tokens":50,"total_tokens":60}}
        """;

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public MistralReasoningContentParseTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReasoningResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    [Fact]
    public async Task ShouldReadTheVisibleAnswer_FromAChunkedContentArray()
    {
        var config = LlmConfig.Create("mistral-medium-2604", TestApiKey);
        using var provider = new MistralLlmProvider(config, _httpClientFactory, _noOpPolicy);

        var response = await provider.GenerateAsync(
            "Why is the sky blue?", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("The sky is blue because of Rayleigh scattering.", response.Content);
        Assert.Equal(60, response.TokensUsed);
    }

    [Fact]
    public async Task ShouldSurfaceTheThinkingChunks_AsTheReasoningTrace()
    {
        var config = LlmConfig.Create("mistral-medium-2604", TestApiKey);
        using var provider = new MistralLlmProvider(config, _httpClientFactory, _noOpPolicy);

        var response = await provider.GenerateAsync(
            "Why is the sky blue?", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.Metadata.TryGetValue("reasoning_content", out var trace));
        Assert.Contains("Rayleigh scattering favours short wavelengths.", (string)trace!, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}
