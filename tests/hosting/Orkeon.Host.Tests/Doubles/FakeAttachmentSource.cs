using System.Text;
using Orkeon.Host.Gateway;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>The files of a message, by identifier.</summary>
internal sealed class FakeAttachmentSource : IAttachmentSource
{
    public Dictionary<string, Func<CancellationToken, Task<Stream>>> Files { get; } = [];

    public List<string> Opened { get; } = [];

    public FakeAttachmentSource With(string id, string content)
    {
        Files[id] = _ =>
        {
            Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return Task.FromResult(stream);
        };
        return this;
    }

    public Task<Stream> OpenAsync(InboundAttachment attachment, CancellationToken ct)
    {
        lock (Opened)
            Opened.Add(attachment.Id);
        return Files.TryGetValue(attachment.Id, out var open) ? open(ct) : throw new IOException("gone");
    }
}
