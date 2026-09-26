using System.Net.Sockets;
using System.Text;
using Orkeon.Tools.Email.Auth;

namespace Orkeon.Tools.Email.Tests.Auth;

/// <summary>The loopback listener that receives the browser redirect of a PKCE sign-in, over real TCP.</summary>
public sealed class LoopbackRedirectListenerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_return_the_code_and_state_of_the_redirect_and_answer_200()
    {
        using var listener = LoopbackRedirectListener.Start();
        var waiting = listener.WaitAsync("xyz", Token);

        var response = await BrowseAsync(listener.RedirectUri, "GET /?code=4%2F0AbCd&state=xyz&scope=mail HTTP/1.1");
        var redirect = await waiting;

        Assert.Equal(new AuthorizationRedirect("4/0AbCd", "xyz", null), redirect);
        Assert.StartsWith("HTTP/1.1 200 OK", response, StringComparison.Ordinal);
        Assert.Contains("You can close this tab", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_answer_404_to_anything_else_and_keep_waiting()
    {
        using var listener = LoopbackRedirectListener.Start();
        var waiting = listener.WaitAsync("xyz", Token);

        var favicon = await BrowseAsync(listener.RedirectUri, "GET /favicon.ico HTTP/1.1");
        var post = await BrowseAsync(listener.RedirectUri, "POST /?code=forged HTTP/1.1");
        Assert.False(waiting.IsCompleted);
        await BrowseAsync(listener.RedirectUri, "GET /?error=access_denied&state=xyz HTTP/1.1");

        var redirect = await waiting;
        Assert.StartsWith("HTTP/1.1 404 Not Found", favicon, StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 404 Not Found", post, StringComparison.Ordinal);
        Assert.Equal(new AuthorizationRedirect(null, "xyz", "access_denied"), redirect);
    }

    [Fact]
    public async Task Should_ignore_an_outcome_that_does_not_carry_the_state_of_this_sign_in()
    {
        using var listener = LoopbackRedirectListener.Start();
        var waiting = listener.WaitAsync("xyz", Token);

        var forgedError = await BrowseAsync(listener.RedirectUri, "GET /?error=access_denied&state=forged HTTP/1.1");
        var stolenCode = await BrowseAsync(listener.RedirectUri, "GET /?code=stolen HTTP/1.1");
        Assert.False(waiting.IsCompleted);
        await BrowseAsync(listener.RedirectUri, "GET /?code=genuine&state=xyz HTTP/1.1");

        Assert.StartsWith("HTTP/1.1 404 Not Found", forgedError, StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 404 Not Found", stolenCode, StringComparison.Ordinal);
        Assert.Equal(new AuthorizationRedirect("genuine", "xyz", null), await waiting);
    }

    [Fact]
    public async Task Should_survive_a_connection_that_sends_nothing()
    {
        using var listener = LoopbackRedirectListener.Start();
        var waiting = listener.WaitAsync("xyz", Token);

        using (var silent = new TcpClient())
            await silent.ConnectAsync("127.0.0.1", listener.RedirectUri.Port, Token);
        await BrowseAsync(listener.RedirectUri, "GET /?code=c&state=xyz HTTP/1.1");

        Assert.Equal("c", (await waiting).Code);
    }

    [Fact]
    public async Task Should_stop_waiting_When_cancelled()
    {
        using var listener = LoopbackRedirectListener.Start();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var waiting = listener.WaitAsync("xyz", cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
    }

    [Fact]
    public void Should_listen_on_the_loopback_address()
    {
        using var listener = LoopbackRedirectListener.Start();

        Assert.Equal("http", listener.RedirectUri.Scheme);
        Assert.Equal("127.0.0.1", listener.RedirectUri.Host);
        Assert.Equal("/", listener.RedirectUri.AbsolutePath);
        Assert.True(listener.RedirectUri.Port > 0);
    }

    [Theory]
    [InlineData("GET /callback?code=a%2Bb&state=s%20t HTTP/1.1", "a+b", "s t", true)]
    [InlineData("POST /?code=x&state=y HTTP/1.1", null, null, false)]
    [InlineData("GET", null, null, false)]
    [InlineData("", null, null, false)]
    [InlineData("GET http://evil.example/?code=x HTTP/1.1", null, null, false)]
    [InlineData("GET //[not-an-address/?code=x&state=y HTTP/1.1", null, null, false)]
    public void Should_parse_only_a_GET_request_line(string requestLine, string? code, string? state, bool outcome)
    {
        var redirect = LoopbackRedirectListener.ParseRequestLine(requestLine);

        Assert.Equal(code, redirect.Code);
        Assert.Equal(state, redirect.State);
        Assert.Equal(outcome, redirect.IsOutcome);
    }

    private static async Task<string> BrowseAsync(Uri redirect, string requestLine)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", redirect.Port, Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(requestLine + "\r\nHost: 127.0.0.1\r\n\r\n"), Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(Token);
    }
}
