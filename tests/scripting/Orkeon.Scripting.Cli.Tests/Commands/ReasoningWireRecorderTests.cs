using System.Net;
using System.Text;
using System.Text.Json.Nodes;
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

    private static async Task<(string? Fields, string? Received)> SendOverriddenAsync(
        string body, string fragment, string mediaType = "application/json")
    {
        using var inner = new StubBodyHandler();
        using var recorder = new ReasoningWireRecorder(inner);
        recorder.Override(JsonNode.Parse(fragment)!.AsObject());
        using var client = new HttpClient(recorder, disposeHandler: false);
        using var content = new StringContent(body, Encoding.UTF8, mediaType);
        using var response = await client.PostAsync(new Uri("http://localhost/v1/chat"), content, TestContext.Current.CancellationToken);
        return (recorder.LastFields, inner.ReceivedBody);
    }

    [Fact]
    public async Task ShouldReplaceReasoningEffort_WhenOverridingAnOpenAiBody()
    {
        var (fields, received) = await SendOverriddenAsync(
            """{"model":"m","reasoning_effort":"low","max_tokens":9}""", """{"reasoning_effort":"xhigh"}""");

        Assert.Equal("""{"reasoning_effort":"xhigh"}""", fields);
        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.Equal("xhigh", (string?)sent["reasoning_effort"]);
        Assert.Equal("m", (string?)sent["model"]);
        Assert.Equal(9, (int?)sent["max_tokens"]);
    }

    [Fact]
    public async Task ShouldStripThinkingAndEffort_KeepingTheOutputFormat_ForAnAnthropicBody()
    {
        var (fields, received) = await SendOverriddenAsync(
            """{"model":"m","thinking":{"type":"adaptive"},"output_config":{"effort":"high","format":{"type":"json"}}}""",
            """{"thinking":{"type":"enabled","budget_tokens":2048}}""");

        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.Equal("enabled", (string?)sent["thinking"]!["type"]);
        Assert.Equal(2048, (int?)sent["thinking"]!["budget_tokens"]);
        Assert.Null(sent["output_config"]!["effort"]);
        Assert.Equal("json", (string?)sent["output_config"]!["format"]!["type"]);
        Assert.Equal("""{"thinking":{"type":"enabled","budget_tokens":2048}}""", fields);
    }

    [Fact]
    public async Task ShouldDropAnEmptiedOutputConfig()
    {
        var (_, received) = await SendOverriddenAsync(
            """{"model":"m","output_config":{"effort":"high"}}""", """{"think":true}""");

        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.False(sent.ContainsKey("output_config"));
        Assert.True((bool?)sent["think"]);
    }

    [Fact]
    public async Task ShouldReplaceTheReasoningObject_ForAnOpenRouterBody()
    {
        var (fields, received) = await SendOverriddenAsync(
            """{"model":"m","reasoning":{"effort":"high","exclude":false}}""",
            """{"reasoning":{"max_tokens":512}}""");

        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.Equal(512, (int?)sent["reasoning"]!["max_tokens"]);
        Assert.Null(sent["reasoning"]!["effort"]);
        Assert.Null(sent["reasoning"]!["exclude"]);
        Assert.Equal("""{"reasoning":{"max_tokens":512}}""", fields);
    }

    [Fact]
    public async Task ShouldStripEnableThinkingAndBudget_ForAQwenBody()
    {
        var (_, received) = await SendOverriddenAsync(
            """{"model":"qwen","enable_thinking":true,"thinking_budget":1024}""", """{"enable_thinking":false}""");

        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.False((bool?)sent["enable_thinking"]);
        Assert.False(sent.ContainsKey("thinking_budget"));
    }

    [Fact]
    public async Task ShouldStripTheTemplateEnableThinking_KeepingOtherKwargs_ForAHuggingFaceBody()
    {
        var (fields, received) = await SendOverriddenAsync(
            """{"model":"m","chat_template_kwargs":{"enable_thinking":true,"other":1}}""",
            """{"reasoning_effort":"low"}""");

        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.Null(sent["chat_template_kwargs"]!["enable_thinking"]);
        Assert.Equal(1, (int?)sent["chat_template_kwargs"]!["other"]);
        Assert.Equal("""{"reasoning_effort":"low"}""", fields);
    }

    [Fact]
    public async Task ShouldRecordTheTemplateEnableThinking_WhenTheFragmentSetsIt()
    {
        var (fields, received) = await SendOverriddenAsync(
            """{"model":"m","chat_template_kwargs":{"enable_thinking":true,"other":1}}""",
            """{"chat_template_kwargs":{"enable_thinking":false}}""");

        Assert.Equal("""{"chat_template_kwargs":{"enable_thinking":false}}""", fields);
        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.Equal(1, (int?)sent["chat_template_kwargs"]!["other"]);
    }

    [Fact]
    public async Task ShouldMergeNestedObjects_AndReplaceOtherValues()
    {
        var (_, received) = await SendOverriddenAsync(
            """{"model":"m","extra":{"a":1,"b":{"c":2}},"list":[1]}""",
            """{"extra":{"b":{"d":3}},"list":[2,3]}""");

        var sent = JsonNode.Parse(received!)!.AsObject();
        Assert.Equal(1, (int?)sent["extra"]!["a"]);
        Assert.Equal(2, (int?)sent["extra"]!["b"]!["c"]);
        Assert.Equal(3, (int?)sent["extra"]!["b"]!["d"]);
        Assert.Equal(2, sent["list"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public async Task ShouldLeaveANonObjectBodyUntouched_UnderAnOverride(string body)
    {
        var (_, received) = await SendOverriddenAsync(body, """{"reasoning_effort":"low"}""");

        Assert.Equal(body, received);
    }

    [Fact]
    public async Task ShouldKeepTheBodyByteIdentical_WhenNoOverrideIsSet()
    {
        const string body = """{"model":"m", "reasoning_effort": "high"}""";

        var (_, inner) = await SendAsync(body);

        Assert.Equal(body, inner.ReceivedBody);
    }

    [Fact]
    public async Task ShouldStopRewriting_WhenTheOverrideIsCleared()
    {
        using var inner = new StubBodyHandler();
        using var recorder = new ReasoningWireRecorder(inner);
        recorder.Override(new JsonObject { ["think"] = true });
        recorder.Override(null);
        using var client = new HttpClient(recorder, disposeHandler: false);
        const string body = """{"reasoning_effort":"low"}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("http://localhost/a"), content, TestContext.Current.CancellationToken);

        Assert.Equal(body, inner.ReceivedBody);
    }

    /// <summary>Inner handler that answers every request with a fixed status and JSON body.</summary>
    private sealed class StubAnswerHandler(HttpStatusCode status, string answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            });
    }

    [Theory]
    [InlineData("""{"choices":[{"message":{"content":"0.05","reasoning_content":"The ball is x."},"finish_reason":"stop"}],"usage":{"completion_tokens_details":{"reasoning_tokens":42}}}""", true, 42L, "stop")]
    [InlineData("""{"choices":[{"message":{"content":"0.05","reasoning":"x"},"finish_reason":"length"}]}""", true, null, "length")]
    [InlineData("""{"choices":[{"message":{"content":"<think>x</think>0.05"},"finish_reason":"stop"}]}""", true, null, "stop")]
    [InlineData("""{"choices":[{"message":{"content":"0.05","reasoning_content":""},"finish_reason":"stop"}]}""", false, null, "stop")]
    [InlineData("""{"content":[{"type":"thinking","thinking":"Let x be","signature":"s"},{"type":"text","text":"0.05"}],"stop_reason":"end_turn"}""", true, null, "end_turn")]
    [InlineData("""{"content":[{"type":"thinking","thinking":"","signature":"s"},{"type":"text","text":"0.05"}],"stop_reason":"end_turn"}""", false, null, "end_turn")]
    [InlineData("""{"content":[{"type":"redacted_thinking","data":"x"},{"type":"text","text":"0.05"}],"stop_reason":"max_tokens"}""", true, null, "max_tokens")]
    [InlineData("""{"status":"completed","output":[{"type":"reasoning","summary":[{"type":"summary_text","text":"x"}]}],"usage":{"output_tokens_details":{"reasoning_tokens":7}}}""", true, 7L, "completed")]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[{"type":"reasoning","summary":[]}]}""", false, null, "max_output_tokens")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":"x","thought":true},{"text":"0.05"}]},"finishReason":"STOP"}],"usageMetadata":{"thoughtsTokenCount":12}}""", true, 12L, "STOP")]
    [InlineData("""{"message":{"role":"assistant","content":"0.05","thinking":"x"},"done_reason":"stop"}""", true, null, "stop")]
    [InlineData("""{"choices":[{"message":{"content":[{"type":"thinking","thinking":[{"type":"text","text":"x"}]},{"type":"text","text":"208"}]},"finish_reason":"stop"}]}""", true, null, "stop")]
    [InlineData("""{"choices":[{"message":{"content":[{"type":"text","text":"208"}]},"finish_reason":"stop"}]}""", false, null, "stop")]
    public void ShouldReadTheReasoningFactsOfTheAnswer_ForEachDialect(string answer, bool trace, long? tokens, string finish)
    {
        var read = ReasoningWireRecorder.ReadAnswer(answer);

        Assert.NotNull(read);
        Assert.Equal(trace, read.Trace);
        Assert.Equal(tokens, read.ReasoningTokens);
        Assert.Equal(finish, read.FinishReason);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    public void ShouldReadNoAnswer_WhenTheBodyIsNotAJsonObject(string answer)
    {
        Assert.Null(ReasoningWireRecorder.ReadAnswer(answer));
    }

    [Fact]
    public async Task ShouldCaptureTheVendorAnswer_AndLeaveTheBodyReadableForTheProvider()
    {
        const string answer = """{"content":[{"type":"thinking","thinking":"x"},{"type":"text","text":"0.05"}],"stop_reason":"end_turn"}""";
        using var inner = new StubAnswerHandler(HttpStatusCode.OK, answer);
        using var recorder = new ReasoningWireRecorder(inner);
        using var client = new HttpClient(recorder, disposeHandler: false);
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("http://localhost/v1/messages"), content, TestContext.Current.CancellationToken);

        Assert.Equal(new VendorAnswer(true, null, "end_turn"), recorder.LastAnswer);
        Assert.Equal(answer, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldCaptureNoAnswer_WhenTheVendorRefuses_AndForgetItOnReset()
    {
        using var inner = new StubAnswerHandler(HttpStatusCode.BadRequest, """{"stop_reason":"end_turn"}""");
        using var recorder = new ReasoningWireRecorder(inner);
        using var client = new HttpClient(recorder, disposeHandler: false);
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("http://localhost/v1/messages"), content, TestContext.Current.CancellationToken);

        Assert.Null(recorder.LastAnswer);
        recorder.Reset();
        Assert.Null(recorder.LastAnswer);
    }
}
