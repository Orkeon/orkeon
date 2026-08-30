using System.Net;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Anthropic's identity-linked API keys refuse every request that does not carry an
/// <c>anthropic-workspace-id</c> header (<c>"anthropic-workspace-id is required when
/// authenticating with an identity-linked API key"</c> — measured live, 2026-08-30, on the
/// Messages API and on <c>/v1/models</c> alike). Orkeon had no way to send that header at
/// all, so a user holding such a key could not use the provider, full stop. The id is
/// workspace scoping, not a secret: it travels as <see cref="LlmConfig.WorkspaceId"/>, the
/// same way <see cref="LlmConfig.ApiVersion"/> carries Azure's own out-of-band detail.
/// </summary>
public sealed class AnthropicWorkspaceHeaderTests : IDisposable
{
    private const string WorkspaceId = "wrkspc_01TestWorkspaceIdentifier";

    private const string MinimalSuccessResponse =
        """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public AnthropicWorkspaceHeaderTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    private AnthropicLlmProvider CreateProvider(LlmConfig config) =>
        new(config, _httpClientFactory, _noOpPolicy);

    [Fact]
    public async Task ShouldSendTheWorkspaceHeader_WhenTheConfigCarriesOne()
    {
        var config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with { WorkspaceId = WorkspaceId };
        using var provider = CreateProvider(config);

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(_handler.LastRequest);
        Assert.True(_handler.LastRequest!.Headers.TryGetValues("anthropic-workspace-id", out var values));
        Assert.Equal(WorkspaceId, Assert.Single(values!));
    }

    /// <summary>
    /// Classic keys are not workspace-scoped, and an empty header would be a different
    /// request than the one every campaign before 2026-08-30 sent. No id, no header.
    /// </summary>
    [Fact]
    public async Task ShouldSendNoWorkspaceHeader_WhenTheConfigCarriesNone()
    {
        var config = LlmConfig.Create(ModelClaude3Opus, TestApiKey);
        using var provider = CreateProvider(config);

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(_handler.LastRequest);
        Assert.False(_handler.LastRequest!.Headers.Contains("anthropic-workspace-id"));
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}
