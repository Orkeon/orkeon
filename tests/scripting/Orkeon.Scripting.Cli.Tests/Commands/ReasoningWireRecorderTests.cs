using System.Net;
using System.Text;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The wire recorder keeps the reasoning fields of what a provider really sends, for each
/// vendor dialect, and nothing else.
/// </summary>
public sealed class ReasoningWireRecorderTests
{
    /// <summary>Inner handler that records the body it received, proving the request still flows.</summary>
    private sealed class StubBodyHandler : HttpMessageHandler
    {
        public string? ReceivedBody { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static async Task<(string? Fields, StubBodyHandler Inner)> SendAsync(string body)
    {
        using var inner = new StubBodyHandler();
        using var recorder = new ReasoningWireRecorder(inner);
        using var client = new HttpClient(recorder, disposeHandler: false);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(new Uri("http://localhost/v1/chat"), content, TestContext.Current.CancellationToken);
        return (recorder.LastFields, inner);
    }

    [Theory]
    [InlineData("""{"model":"m","messages":[{"role":"user","content":"secret prompt"}],"reasoning_effort":"low"}""", """{"reasoning_effort":"low"}""")]
    [InlineData("""{"model":"m","thinking":{"type":"adaptive"},"output_config":{"effort":"high","format":"x"},"max_tokens":9}""", """{"thinking":{"type":"adaptive"},"output_config":{"effort":"high"}}""")]
    [InlineData("""{"model":"m","think":"medium","stream":false}""", """{"think":"medium"}""")]
    [InlineData("""{"model":"m","think":true}""", """{"think":true}""")]
    [InlineData("""{"model":"qwen","enable_thinking":true,"thinking_budget":1024}""", """{"enable_thinking":true,"thinking_budget":1024}""")]
    [InlineData("""{"model":"x","reasoning":{"effort":"high","max_tokens":1024,"exclude":false}}""", """{"reasoning":{"effort":"high","max_tokens":1024,"exclude":false}}""")]
    [InlineData("""{"model":"deepseek","thinking":{"type":"enabled"}}""", """{"thinking":{"type":"enabled"}}""")]
    public async Task ShouldKeepOnlyTheReasoningFields_ForEachDialect(string body, string expected)
    {
        var (fields, _) = await SendAsync(body);

        Assert.Equal(expected, fields);
    }

    [Fact]
    public async Task ShouldRecordNothing_WhenTheBodyHasNoReasoningField()
    {
        var (fields, _) = await SendAsync("""{"model":"m","messages":[]}""");

        Assert.Null(fields);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2]")]
    public async Task ShouldIgnoreANonJsonOrEmptyBody(string body)
    {
        var (fields, inner) = await SendAsync(body);

        Assert.Null(fields);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ShouldLetTheRequestReachTheInnerHandlerIntact()
    {
        const string body = """{"model":"m","reasoning_effort":"high","messages":[{"role":"user","content":"hi"}]}""";

        var (_, inner) = await SendAsync(body);

        Assert.Equal(body, inner.ReceivedBody);
    }

    [Fact]
    public async Task ShouldKeepTheLastRequest_AndForgetOnReset()
    {
        using var inner = new StubBodyHandler();
        using var recorder = new ReasoningWireRecorder(inner);
        using var client = new HttpClient(recorder, disposeHandler: false);

        using var first = new StringContent("""{"reasoning_effort":"low"}""", Encoding.UTF8, "application/json");
        using var r1 = await client.PostAsync(new Uri("http://localhost/a"), first, TestContext.Current.CancellationToken);
        using var second = new StringContent("""{"reasoning_effort":"high"}""", Encoding.UTF8, "application/json");
        using var r2 = await client.PostAsync(new Uri("http://localhost/a"), second, TestContext.Current.CancellationToken);

        Assert.Equal("""{"reasoning_effort":"high"}""", recorder.LastFields);

        recorder.Reset();
        Assert.Null(recorder.LastFields);
    }

    [Fact]
    public async Task ShouldNeverRecordAHeader()
    {
        using var inner = new StubBodyHandler();
        using var recorder = new ReasoningWireRecorder(inner);
        using var client = new HttpClient(recorder, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://localhost/a"))
        {
            Content = new StringContent("""{"think":"low"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Authorization", "Bearer sk-secret");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("sk-secret", recorder.LastFields, StringComparison.Ordinal);
    }
}
