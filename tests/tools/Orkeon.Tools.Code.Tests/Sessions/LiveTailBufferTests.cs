using System.Text;
using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary><see cref="LiveTailBuffer"/>: the last 64 KiB of a job's output, and whether bytes were dropped.</summary>
public sealed class LiveTailBufferTests
{
    [Fact]
    [Trait("BR", "RT-11")]
    public void ShouldReportTruncated_WhenMultibyteOutputExceedsCapacity()
    {
        var buffer = new LiveTailBuffer();
        var all = new List<byte>();
        var chunk = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("日本語\n", 1000)));
        for (var i = 0; i < 10; i++)
        {
            buffer.Append(chunk);
            all.AddRange(chunk);
        }

        Assert.True(all.Count > LiveTailBuffer.Capacity);
        var snapshot = buffer.Snapshot();

        Assert.True(snapshot.Truncated);
        var lastBytes = all.GetRange(all.Count - LiveTailBuffer.Capacity, LiveTailBuffer.Capacity).ToArray();
        Assert.Equal(new UTF8Encoding(false).GetString(lastBytes), snapshot.Text);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public void ShouldNotReportTruncated_WhenOutputIsExactlyCapacityBytes()
    {
        var buffer = new LiveTailBuffer();
        var head = "first-line\n";
        buffer.Append(Encoding.ASCII.GetBytes(head));
        buffer.Append(Encoding.ASCII.GetBytes(new string('x', LiveTailBuffer.Capacity - head.Length - 1) + "\n"));

        var snapshot = buffer.Snapshot();

        Assert.False(snapshot.Truncated);
        Assert.Equal(LiveTailBuffer.Capacity, snapshot.Text.Length);
        Assert.StartsWith("first-line\n", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public void ShouldKeepReportingTruncated_WhenMoreOutputFollowsACut()
    {
        var buffer = new LiveTailBuffer();
        buffer.Append(Encoding.ASCII.GetBytes(new string('a', LiveTailBuffer.Capacity + 10)));
        buffer.Append(Encoding.ASCII.GetBytes("tail1\n"));
        buffer.Append(Encoding.ASCII.GetBytes("tail2\n"));

        var snapshot = buffer.Snapshot();

        Assert.True(snapshot.Truncated);
        Assert.EndsWith("tail1\ntail2\n", snapshot.Text, StringComparison.Ordinal);
    }
}
