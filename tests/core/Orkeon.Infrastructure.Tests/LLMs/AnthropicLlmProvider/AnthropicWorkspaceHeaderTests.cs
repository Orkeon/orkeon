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

    /// <summary>
    /// #468: a provider that has no workspace concept does not send the id, and says so once with the
    /// structured unhonoured-option warning (event 110) rather than drop it in silence.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-468")]
    public async Task ShouldWarnOnce_WhenAProviderThatCannotSendTheWorkspaceIsGivenOne()
    {
        var warnings = new List<(int Id, string Message)>();
        var config = LlmConfig.Create("gpt-4o-mini", TestApiKey) with
        {
            BaseUrl = new Uri("https://api.openai.com/v1"),
            WorkspaceId = WorkspaceId,
        };
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""",
                System.Text.Encoding.UTF8, "application/json")
        });
        using var provider = new OpenAILlmProvider(config, _httpClientFactory, _noOpPolicy, new CollectingLogger<OpenAILlmProvider>(warnings));

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await provider.GenerateAsync("again", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(_handler.LastRequest!.Headers.Contains("anthropic-workspace-id"));
        var warning = Assert.Single(warnings, w => w.Message.Contains("'WorkspaceId'", StringComparison.Ordinal));
        Assert.Equal(110, warning.Id);
    }

    /// <summary>Anthropic sends the id, so it warns nothing.</summary>
    [Fact]
    [Trait("BR", "CH-468")]
    public async Task ShouldNotWarn_WhenAnthropicSendsTheWorkspace()
    {
        var warnings = new List<(int Id, string Message)>();
        var config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with { WorkspaceId = WorkspaceId };
        using var provider = new AnthropicLlmProvider(config, _httpClientFactory, _noOpPolicy, toolCallingStrategy: null, new CollectingLogger<AnthropicLlmProvider>(warnings));

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(warnings, w => w.Message.Contains("'WorkspaceId'", StringComparison.Ordinal));
    }

    private sealed class CollectingLogger<T>(List<(int Id, string Message)> warnings) : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                warnings.Add((eventId.Id, formatter(state, exception)));
        }
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}
