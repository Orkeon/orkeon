using System.Text;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>A content whose first byte comes at once, and the rest when it is told to.</summary>
internal sealed class LateStream(string content, TaskCompletionSource started, Task rest) : MemoryStream(Encoding.UTF8.GetBytes(content))
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Position == 0)
            return await base.ReadAsync(buffer[..1], cancellationToken);
        started.TrySetResult();
        await rest.WaitAsync(cancellationToken);
        return await base.ReadAsync(buffer, cancellationToken);
    }
}
