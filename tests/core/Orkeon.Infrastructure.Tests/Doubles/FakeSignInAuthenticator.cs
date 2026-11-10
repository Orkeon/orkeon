using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Orkeon.Infrastructure.MCP.Auth;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An authenticator that counts how it is used: the requests it signed, the sign-ins it was asked for, and the
/// applications that took what it held. It can be made to fail the application that never signs in.
/// </summary>
internal sealed class FakeSignInAuthenticator : IMcpSignInAuthenticator
{
    private int _applied;
    private int _signIns;
    private int _quiet;

    /// <summary>Makes <see cref="ApplyWithoutSignInAsync"/> throw this, as an authenticator that cannot renew a credential would.</summary>
    public Exception? QuietFailure { get; set; }

    public int Applied => Volatile.Read(ref _applied);

    public int SignIns => Volatile.Read(ref _signIns);

    public int QuietApplications => Volatile.Read(ref _quiet);

    /// <summary>
    /// Every credential applied, in order: the verb of the request and whether the quiet path (no sign-in) carried it.
    /// A background GET may apply its own while a test runs, so a test about one request reads its verb here.
    /// </summary>
    public ConcurrentQueue<(HttpMethod Verb, bool Quiet)> Applications { get; } = new();

    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _applied);
        Applications.Enqueue((request.Method, false));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return ValueTask.CompletedTask;
    }

    public ValueTask EnsureSignedInAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _signIns);
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyWithoutSignInAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _quiet);
        Applications.Enqueue((request.Method, true));
        if (QuietFailure is { } failure)
            throw failure;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return ValueTask.CompletedTask;
    }
}
