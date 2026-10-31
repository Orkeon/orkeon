using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Session;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ISessionBufferService"/> the loop writes to while a tool reads it: every
/// <see cref="GetMessages"/> appends <see cref="Growth"/> to the buffer before it answers the snapshot, so
/// two reads of the buffer never see the same messages. Everything else is an <see cref="InMemorySessionBufferService"/>.
/// </summary>
public sealed class FakeGrowingSessionBufferService : ISessionBufferService
{
    private readonly InMemorySessionBufferService _inner = new();

    /// <summary>The message appended before each snapshot.</summary>
    public SessionMessage Growth { get; set; } = new() { Role = "user", Content = new string('g', 396) };

    /// <summary>The snapshots answered so far.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<SessionMessage> GetMessages()
    {
        Reads++;
        _inner.ReplaceMessages([.. _inner.GetMessages(), Growth]);
        return _inner.GetMessages();
    }

    /// <inheritdoc />
    public void ReplaceMessages(IReadOnlyList<SessionMessage> messages) => _inner.ReplaceMessages(messages);

    /// <inheritdoc />
    public bool AppendNote(string note) => _inner.AppendNote(note);

    /// <inheritdoc />
    public void AppendMessage(SessionMessage message) => _inner.AppendMessage(message);

    /// <inheritdoc />
    public SessionMetadata GetMetadata() => _inner.GetMetadata();

    /// <inheritdoc />
    public bool SetTitle(string title) => _inner.SetTitle(title);

    /// <inheritdoc />
    public int Truncate(int retainCount) => _inner.Truncate(retainCount);

    /// <inheritdoc />
    public int Reset() => _inner.Reset();

    /// <inheritdoc />
    public int EstimateTokenCount() => _inner.EstimateTokenCount();

    /// <inheritdoc />
    public int MessageCount => _inner.MessageCount;

    /// <inheritdoc />
    public string? GetState(string key) => _inner.GetState(key);

    /// <inheritdoc />
    public void SetState(string key, string? value) => _inner.SetState(key, value);
}
