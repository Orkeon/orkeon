using Orkeon.Infrastructure.MCP.Auth;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>An authenticator that adds nothing and records whether it was disposed.</summary>
internal sealed class StubDisposableRequestAuthenticator : IMcpRequestAuthenticator, IDisposable
{
    /// <summary>Whether <see cref="Dispose"/> was called.</summary>
    public bool Disposed;

    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default) => ValueTask.CompletedTask;

    public void Dispose() => Disposed = true;
}
