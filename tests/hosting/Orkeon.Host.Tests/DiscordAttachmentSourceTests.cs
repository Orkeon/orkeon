using System.Net;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>What the host asks Discord's file server, and what it refuses to ask anyone else.</summary>
public sealed class DiscordAttachmentSourceTests
{
    private const string Address = "https://cdn.discordapp.com/attachments/1/7/a.txt?ex=signed-part";
    private static readonly InboundAttachment Attachment = new() { Id = "7", Name = "a.txt" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DiscordAttachmentSource Source(HttpClient http, string address) =>
        new(http, new Dictionary<string, string> { ["7"] = address });

    [Fact]
    public async Task ShouldReadTheFile_WithoutAnyCredential()
    {
        using var handler = new StubHttpHandler();
        using var http = new HttpClient(handler);

        await using var stream = await Source(http, Address).OpenAsync(Attachment, Ct);
        using var reader = new StreamReader(stream);

        Assert.Equal("abc", await reader.ReadToEndAsync(Ct));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(Address, request.Address);
        Assert.False(request.Authorized);
    }

    [Theory]
    [InlineData("http://cdn.discordapp.com/a")]
    [InlineData("https://cdn.discordapp.com:8443/a")]
    [InlineData("https://cdn.discordapp.com.evil.example/a")]
    [InlineData("https://evil.example/cdn.discordapp.com")]
    [InlineData("https://cdn.discordapp.com@evil.example/a")]
    [InlineData("https://user:pw@cdn.discordapp.com/a")]
    [InlineData("https://127.0.0.1/a")]
    [InlineData("https://media.discordapp.net/a")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/a")]
    [InlineData("")]
    public async Task ShouldAskNobody_WhenTheAddressIsNotDiscords(string address)
    {
        using var handler = new StubHttpHandler();
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<IOException>(() => Source(http, address).OpenAsync(Attachment, Ct));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task ShouldRefuse_WhenTheAnswerIsNotTheFile(HttpStatusCode status)
    {
        using var handler = new StubHttpHandler
        {
            Answer = _ =>
            {
                var response = new HttpResponseMessage(status);
                response.Headers.Location = new Uri("https://evil.example/");
                return Task.FromResult(response);
            },
        };
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<IOException>(() => Source(http, Address).OpenAsync(Attachment, Ct));

        Assert.Single(handler.Requests);
        Assert.DoesNotContain("signed-part", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameNoAddress_WhenTheTransportFails()
    {
        using var handler = new StubHttpHandler { Answer = _ => throw new HttpRequestException($"failed on {Address}") };
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<IOException>(() => Source(http, Address).OpenAsync(Attachment, Ct));

        Assert.DoesNotContain("signed-part", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldStop_WhenTheRunIsStopped()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var handler = new StubHttpHandler
        {
            Answer = async ct =>
            {
                await stop.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage();
            },
        };
        using var http = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(http, Address).OpenAsync(Attachment, stop.Token));
    }

    [Fact]
    public async Task ShouldGiveUp_WhenTheFileStopsComing()
    {
        using var handler = new StubHttpHandler
        {
            Answer = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SilentStream()) }),
        };
        using var http = new HttpClient(handler);
        var source = new DiscordAttachmentSource(http, new Dictionary<string, string> { ["7"] = Address }, TimeSpan.FromMilliseconds(20));

        await using var stream = await source.OpenAsync(Attachment, Ct);

        await Assert.ThrowsAsync<IOException>(async () => await stream.ReadAsync(new byte[8], Ct));
    }

    [Fact]
    public void ShouldFollowNoRedirectionAndLeaveNoTrace_InTheHandlerTheHostUses()
    {
        using var handler = DiscordAttachmentSource.NewHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.ActivityHeadersPropagator);
    }

    /// <summary>A stream that gives nothing until it is cancelled.</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
