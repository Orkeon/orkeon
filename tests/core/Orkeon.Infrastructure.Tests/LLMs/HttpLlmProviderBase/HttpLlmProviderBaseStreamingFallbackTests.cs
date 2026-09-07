using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// D5-03: <see cref="HttpLlmProviderBase.GenerateStreamingAsync"/>'s default implementation is the
/// buffered fallback every provider without a native SSE path inherits. It used to yield the
/// buffered content only when it was non-empty, so a refusal — no content, the reason in the
/// metadata — reached the caller as a stream that ended normally on silence.
/// </summary>
public class HttpLlmProviderBaseStreamingFallbackTests
{
    private const string MissingKeyError = "TestProvider API key is required";

    [Fact]
    public async Task Default_fallback_fails_when_the_buffered_answer_carries_a_provider_error()
    {
        using var factory = new MockHttpClientFactory();
        using var provider = new BufferedOnlyProvider(factory, new LlmResponse
        {
            Content = "",
            Metadata = new Dictionary<string, object> { ["error"] = MissingKeyError },
        });

        var chunks = new List<string>();
        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var chunk in provider.GenerateStreamingAsync(
                               "p", cancellationToken: TestContext.Current.CancellationToken))
                chunks.Add(chunk);
        });

        Assert.Contains(MissingKeyError, failure.Message, StringComparison.Ordinal);
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task Default_fallback_yields_the_buffered_content_as_one_chunk()
    {
        using var factory = new MockHttpClientFactory();
        using var provider = new BufferedOnlyProvider(factory, new LlmResponse { Content = "one shot" });

        var chunks = new List<string>();
        await foreach (var chunk in provider.GenerateStreamingAsync(
                           "p", cancellationToken: TestContext.Current.CancellationToken))
            chunks.Add(chunk);

        Assert.Equal("one shot", Assert.Single(chunks));
    }

    /// <summary>
    /// Smallest provider that inherits the base class without overriding streaming: it answers one
    /// canned <see cref="LlmResponse"/> and never touches HTTP.
    /// </summary>
    private sealed class BufferedOnlyProvider : HttpLlmProviderBase
    {
        private readonly LlmResponse _answer;

        public BufferedOnlyProvider(IHttpClientFactory httpClientFactory, LlmResponse answer)
            : base(LlmConfig.Default(), httpClientFactory)
            => _answer = answer;

        public override string Name => "TestProvider";

        public override Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_answer);
    }
}
