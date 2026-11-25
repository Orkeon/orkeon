using System.Net;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>An HTTP handler that answers as the test says and keeps what it was asked.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    public List<(string? Address, bool Authorized)> Requests { get; } = [];

    /// <summary>The answer; an empty 200 by default.</summary>
    public Func<CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc") });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.RequestUri?.ToString(), request.Headers.Authorization is not null));
        return Answer(cancellationToken);
    }
}
