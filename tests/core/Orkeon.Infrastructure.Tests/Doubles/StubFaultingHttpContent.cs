using System.Net;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="HttpContent"/> whose body cannot be read: every read throws <see cref="Failure"/>.
/// <see cref="Disposed"/> tells whether the response that owns it was disposed (issue #328).
/// </summary>
public sealed class StubFaultingHttpContent(Exception failure) : HttpContent
{
    /// <summary>The exception each read throws.</summary>
    public Exception Failure { get; } = failure;

    /// <summary>True once the content was disposed.</summary>
    public bool Disposed;

    /// <summary>How many reads were attempted.</summary>
    public int Reads;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        Reads++;
        return Task.FromException(Failure);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        SerializeToStreamAsync(stream, context);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
