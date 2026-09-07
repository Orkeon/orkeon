using System.Net;
using System.Text;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double: answers one fixed HTML page and records every request it
/// was asked to send, so a test can assert that no fetch was ever issued.
/// </summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private const string DefaultPage =
        "<html><head><title>Allowed</title></head><body><p>Allowed body</p></body></html>";

    /// <summary>URIs the handler was asked to fetch, in order.</summary>
    public List<Uri> Requests { get; } = [];

    /// <summary>Body served for every request.</summary>
    public string Page { get; set; } = DefaultPage;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        });
    }
}
