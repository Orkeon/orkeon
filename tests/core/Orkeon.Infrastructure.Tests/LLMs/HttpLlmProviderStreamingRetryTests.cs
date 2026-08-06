using System.Net;
using System.Net.Sockets;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// <see cref="HttpLlmProviderBase.SendStreamingRequestAsync"/> connect-phase retry: the
/// buffered path runs under the Polly policy, but the streaming path used to be a single
/// bare SendAsync — one transient socket failure ("Resource temporarily unavailable",
/// the live /analyze incident) killed the whole turn.
/// </summary>
public sealed class HttpLlmProviderStreamingRetryTests
{
    private static readonly Uri Endpoint = new("https://api.example.test/v1/chat/completions");

    [Fact]
    public async Task A_transient_connect_failure_is_retried_and_the_stream_proceeds()
    {
        using var handler = new MockHttpMessageHandler();
        var calls = 0;
        handler.SetResponseFactory(_ =>
        {
            calls++;
            if (calls == 1)
                throw new HttpRequestException(
                    "Resource temporarily unavailable (api.example.test:443)",
                    new SocketException(11));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: ok") };
        });
        using var client = new HttpClient(handler);
        using var provider = new ProbeProvider();

        using var response = await provider.SendStreaming(client, Endpoint, "{}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_persistent_connect_failure_surfaces_after_the_attempt_budget()
    {
        using var handler = new MockHttpMessageHandler();
        var calls = 0;
        handler.SetResponseFactory(_ =>
        {
            calls++;
            throw new HttpRequestException("connection refused", new SocketException(111));
        });
        using var client = new HttpClient(handler);
        using var provider = new ProbeProvider();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.SendStreaming(client, Endpoint, "{}", CancellationToken.None));

        Assert.Equal(3, calls); // the full attempt budget, then the failure surfaces
    }

    [Fact]
    public async Task A_retriable_status_is_retried_and_a_success_wins()
    {
        using var handler = new MockHttpMessageHandler();
        var calls = 0;
        handler.SetResponseFactory(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: ok") };
        });
        using var client = new HttpClient(handler);
        using var provider = new ProbeProvider();

        using var response = await provider.SendStreaming(client, Endpoint, "{}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_non_retriable_status_returns_immediately_for_the_caller_to_handle()
    {
        using var handler = new MockHttpMessageHandler();
        var calls = 0;
        handler.SetResponseFactory(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("bad key") };
        });
        using var client = new HttpClient(handler);
        using var provider = new ProbeProvider();

        using var response = await provider.SendStreaming(client, Endpoint, "{}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls); // a 401 is not transient — never retried
    }

    /// <summary>Minimal concrete provider exposing the protected streaming send.</summary>
    private sealed class ProbeProvider : HttpLlmProviderBase
    {
        private readonly MockHttpClientFactory _factory;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Ownership transferred: the factory is stored by the chained constructor and disposed by Dispose(bool).")]
        public ProbeProvider()
            : this(new MockHttpClientFactory())
        {
        }

        private ProbeProvider(MockHttpClientFactory factory)
            : base(LlmConfig.Default(), factory)
        {
            _factory = factory;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _factory.Dispose();
            base.Dispose(disposing);
        }

        public override string Name => "probe";

        public override Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = "" });

        public Task<HttpResponseMessage> SendStreaming(
            HttpClient client, Uri endpoint, string payload, CancellationToken ct)
            => SendStreamingRequestAsync(client, endpoint, payload, ct);
    }
}
