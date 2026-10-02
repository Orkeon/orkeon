using System.Net;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose every client answers with one canned body and keeps
/// the requests it was asked to send — headers included, which is what a test of the key that
/// reaches the wire reads. Each named client is created once and reused, like the factory's own.
/// </summary>
public sealed class CapturingHttpClientFactory(string answer) : IHttpClientFactory, IDisposable
{
    private readonly CapturingHandler _handler = new(answer);
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.Ordinal);

    /// <summary>Every request sent through a client of this factory, in order.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _handler.Requests;

    /// <summary>The clients handed out, by name — their <see cref="HttpClient.Timeout"/> is the one a call set.</summary>
    public IReadOnlyDictionary<string, HttpClient> Clients => _clients;

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        lock (_clients)
        {
            if (!_clients.TryGetValue(name, out var client))
            {
                client = new HttpClient(_handler, disposeHandler: false);
                _clients[name] = client;
            }

            return client;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _handler.Dispose();
    }

    private sealed class CapturingHandler(string answer) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get
            {
                lock (_requests)
                    return [.. _requests];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // A buffered copy: the caller disposes the request and its content once sent.
            var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content is not null)
                copy.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));

            lock (_requests)
                _requests.Add(copy);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
        }
    }
}
