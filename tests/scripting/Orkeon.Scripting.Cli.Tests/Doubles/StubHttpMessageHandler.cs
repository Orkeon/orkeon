using System.Net;
using System.Text;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Answers every request with a canned body, and records what was asked.
/// </summary>
/// <remarks>
/// Written by hand rather than pulled from a mocking library, which is this repository's
/// convention: a double whose behaviour is three lines of plain code stays debuggable.
/// </remarks>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    /// <summary>Initializes a handler returning the given body.</summary>
    /// <param name="body">The response body.</param>
    /// <param name="status">The status code to return. Defaults to 200 OK.</param>
    public StubHttpMessageHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    /// <summary>The requests this handler received, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        });
    }
}
