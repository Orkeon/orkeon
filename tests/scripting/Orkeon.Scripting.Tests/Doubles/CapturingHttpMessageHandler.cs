using System.Net;
using System.Text;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// Answers every request with one JSON body and keeps the bodies it was sent, so a test reads
/// what a real provider put on the wire.
/// </summary>
internal sealed class CapturingHttpMessageHandler(string answer) : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(answer, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Hands out clients over one handler, which the clients do not dispose.</summary>
internal sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
