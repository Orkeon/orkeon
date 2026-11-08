using System.Text;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-17: the SSE reader hands out each event as it completes, not the last <c>data:</c> of a body.</summary>
public sealed class SseEventReaderTests
{
    /// <summary>A stream that hands out its chunks one read at a time, as a socket would.</summary>
    private sealed class ChunkedStream(params string[] chunks) : Stream
    {
        private readonly Queue<byte[]> _chunks = new(chunks.Select(Encoding.UTF8.GetBytes));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_chunks.Count == 0)
                return 0;
            var chunk = _chunks.Dequeue();
            Array.Copy(chunk, 0, buffer, offset, chunk.Length);
            return chunk.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_chunks.Count == 0)
                return ValueTask.FromResult(0);
            var chunk = _chunks.Dequeue();
            chunk.CopyTo(buffer);
            return ValueTask.FromResult(chunk.Length);
        }
    }

    private static async Task<List<SseEvent>> ReadAllAsync(params string[] chunks)
    {
        var events = new List<SseEvent>();
        await using var stream = new ChunkedStream(chunks);
        await foreach (var e in SseEventReader.ReadAsync(stream, TestContext.Current.CancellationToken))
            events.Add(e);
        return events;
    }

    [Fact]
    public async Task ShouldReadEachEventOfTheBody_NotOnlyTheLast()
    {
        var events = await ReadAllAsync("data: one\n\ndata: two\n\ndata: three\n\n");

        Assert.Equal(["one", "two", "three"], events.Select(e => e.Data));
    }

    [Fact]
    public async Task ShouldJoinSeveralDataLinesWithANewline()
    {
        var events = await ReadAllAsync("data: {\"a\":\ndata: 1}\n\n");

        var only = Assert.Single(events);
        Assert.Equal("{\"a\":\n1}", only.Data);
    }

    [Fact]
    public async Task ShouldRebuildAnEventSplitAcrossReads()
    {
        var events = await ReadAllAsync("id: 7\nev", "ent: message\nda", "ta: hel", "lo\n", "\ndata: next\n\n");

        Assert.Equal(2, events.Count);
        Assert.Equal("7", events[0].Id);
        Assert.Equal("message", events[0].Event);
        Assert.Equal("hello", events[0].Data);
        Assert.Equal("next", events[1].Data);
    }

    [Fact]
    public async Task ShouldAcceptCrLfLineEndings_CommentsAndDataWithoutASpace()
    {
        var events = await ReadAllAsync(": keep-alive\r\n\r\ndata:tight\r\n\r\n");

        var only = Assert.Single(events);
        Assert.Equal("tight", only.Data);
        Assert.Equal("message", only.Event);
    }

    [Fact]
    public async Task ShouldReadTheRetryHint_AndIgnoreANonNumericOne()
    {
        var events = await ReadAllAsync("retry: 1500\ndata: a\n\nretry: soon\ndata: b\n\n");

        Assert.Equal(1500, events[0].RetryMilliseconds);
        Assert.Null(events[1].RetryMilliseconds);
    }

    [Fact]
    public async Task ShouldDiscardTheLastEvent_WhenTheStreamEndsBeforeItsBlankLine()
    {
        // A connection cut in the middle of an event: its data may be truncated JSON.
        var events = await ReadAllAsync("data: whole\n\ndata: {\"half\":");

        var only = Assert.Single(events);
        Assert.Equal("whole", only.Data);
    }

    [Fact]
    public async Task ShouldHandOutAnEvent_BeforeTheStreamEnds()
    {
        await using var stream = new ChunkedStream("data: first\n\n");
        await using var enumerator = SseEventReader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("first", enumerator.Current.Data);
    }

    [Fact]
    public async Task ShouldHandOutAPrimingEvent_WithItsIdAndNoData()
    {
        // The priming event of the 2025-11-25 specification: an id and an empty data field.
        var events = await ReadAllAsync("id: 0\ndata:\n\ndata: real\n\n");

        Assert.Equal(2, events.Count);
        Assert.Equal("0", events[0].Id);
        Assert.Equal("", events[0].Data);
        Assert.Equal("real", events[1].Data);
    }

    [Fact]
    public async Task ShouldRefuseALineLongerThanTheBound()
    {
        await using var stream = new ChunkedStream("data: " + new string('x', 64) + "\n\n");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in SseEventReader.ReadAsync(stream, 32, TestContext.Current.CancellationToken))
            {
            }
        });
    }
}
