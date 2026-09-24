namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IHttpClientFactory"/>: every client it hands out sends through the
/// one handler it was given — how a host's real providers are pointed at a canned vendor.
/// </summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
