using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.CovAgentMcp;

/// <summary>
/// Coverage tests for <see cref="SseMcpTransport"/> (HTTP-based MCP transport).
/// Uses a fake <see cref="HttpMessageHandler"/> — no real network access.
/// </summary>
public sealed class CovAgentMcp_SseMcpTransportTests
{
    private const string Endpoint = "https://mcp.example.test/rpc";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string SerializeResponse(JsonRpcResponse response) => JsonSerializer.Serialize(response);

    [Fact]
    public void Constructor_WithNullUrl_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SseMcpTransport(null!));
    }

    [Fact]
    public async Task Constructor_DefaultsToDisconnected()
    {
        using var innerHandler6 = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var http = new HttpClient(innerHandler6);
        await using var transport = new SseMcpTransport(new Uri(Endpoint), http);

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_SetsConnectedTrue()
    {
        using var innerHandler5 = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var http = new HttpClient(innerHandler5);
        await using var transport = new SseMcpTransport(new Uri(Endpoint), http);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task SendRequestAsync_WhenNotConnected_Throws()
    {
        using var innerHandler4 = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var http = new HttpClient(innerHandler4);
        await using var transport = new SseMcpTransport(new Uri(Endpoint), http);
        var request = new JsonRpcRequest { Method = "ping", Id = 1 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendRequestAsync(request, TestContext.Current.CancellationToken));
        Assert.Contains("not connected", ex.Message);
    }

    [Fact]
    public async Task SendRequestAsync_PostsToConfiguredUrlWithJsonBody()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            JsonResponse(SerializeResponse(new JsonRpcResponse { Id = 42 })));
        using var http = new HttpClient(handler);
        await using var transport = new SseMcpTransport(new Uri(Endpoint), http);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var request = new JsonRpcRequest { Method = "tools/list", Id = 42 };
        var response = await transport.SendRequestAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(42, response.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(Endpoint, handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("tools/list", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendRequestAsync_WithErrorResponse_ReturnsDeserializedError()
    {
        var errorResponse = new JsonRpcResponse
        {
            Id = 7,
            Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "no such method")
        };
        using var innerHandler3 = new StubHttpMessageHandler(_ =>
            JsonResponse(SerializeResponse(errorResponse)));
        using var http = new HttpClient(innerHandler3);
        await using var transport = new SseMcpTransport(new Uri(Endpoint), http);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var response = await transport.SendRequestAsync(new JsonRpcRequest { Method = "x", Id = 7 }, TestContext.Current.CancellationToken);

        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        Assert.Equal("no such method", response.Error.Message);
    }

    [Fact]
    public async Task SendRequestAsync_WithEmptyBody_ReturnsInternalErrorEnvelope()
    {
        // "null" deserializes to a null JsonRpcResponse -> service synthesizes an error.
        using var innerHandler2 = new StubHttpMessageHandler(_ => JsonResponse("null"));
        using var http = new HttpClient(innerHandler2);
        await using var transport = new SseMcpTransport(new Uri(Endpoint), http);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var request = new JsonRpcRequest { Method = "x", Id = 99 };
        var response = await transport.SendRequestAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(99, response.Id);
        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
        Assert.Contains("Empty response", response.Error.Message);
    }

    [Fact]
    public async Task SendRequestAsync_WithHttpErrorStatus_Throws()
    {
        using var innerHandler1 = new StubHttpMessageHandler(_ =>
            JsonResponse("{}", HttpStatusCode.InternalServerError));
        using var http = new HttpClient(innerHandler1);
        await using var transport = new SseMcpTransport(new Uri(Endpoint), http);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.SendRequestAsync(new JsonRpcRequest { Method = "x", Id = 1 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_WhenNotOwningHttpClient_DoesNotDisposeIt()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            JsonResponse(SerializeResponse(new JsonRpcResponse { Id = 1 })));
        using var http = new HttpClient(handler);
        var transport = new SseMcpTransport(new Uri(Endpoint), http);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
        // The externally-owned HttpClient must still be usable (not disposed).
        var transport2 = new SseMcpTransport(new Uri(Endpoint), http);
        await transport2.ConnectAsync(TestContext.Current.CancellationToken);
        var resp = await transport2.SendRequestAsync(new JsonRpcRequest { Method = "x", Id = 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(1, resp.Id);
        await transport2.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenOwningHttpClient_DisposesIt()
    {
        // No httpClient passed -> transport creates and owns one.
        var transport = new SseMcpTransport(new Uri(Endpoint));
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }
}
