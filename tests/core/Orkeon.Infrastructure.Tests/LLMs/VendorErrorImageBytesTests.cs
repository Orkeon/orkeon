using System.Text;
using System.Net;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Polly;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>A vendor that quotes the image of a call in its refusal: the bytes reach neither the log nor the error.</summary>
public sealed class VendorErrorImageBytesTests : IDisposable
{
    private static readonly string s_image = Convert.ToBase64String(Enumerable.Range(0, 900).Select(i => (byte)(i * 7)).ToArray());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly MockHttpClientFactory _http = new();
    private readonly List<string> _logged = [];

    public void Dispose() => _http.Dispose();

    private sealed class Logger<T>(List<string> logged) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logged.Add(formatter(state, exception) + exception);
    }

    private ILlmProvider Build(string vendor, string body, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        _http.SetupDefaultHandler().SetResponseFactory(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        var config = LlmConfig.Create("some-model") with { MaxRetries = 0, ApiKey = "sk-test" };
        var once = Policy.NoOpAsync<HttpResponseMessage>();
        return vendor switch
        {
            "openai" => new OpenAILlmProvider(config, _http, once, new Logger<OpenAILlmProvider>(_logged)),
            "anthropic" => new AnthropicLlmProvider(config, _http, once, new Logger<AnthropicLlmProvider>(_logged)),
            _ => new OllamaLlmProvider(config with { BaseUrl = new Uri("http://localhost:11434") }, _http, once, new Logger<OllamaLlmProvider>(_logged)),
        };
    }

    private static async Task<string> FailAsync(ILlmProvider provider, string call)
    {
        using var owned = provider as IDisposable;
        LlmMessage[] messages = [new() { Role = "user", Content = "hello" }];
        try
        {
            if (call == "generate")
                return (await provider.GenerateAsync("hello", null, Ct)).Error!;
            if (call == "chat")
                return (await provider.ChatAsync(messages, null, Ct)).Error!;
            if (call == "text")
            {
                var pieces = new StringBuilder();
                await foreach (var piece in ((IStreamingLlmProvider)provider).GenerateStreamingAsync("hello", null, Ct))
                    pieces.Append(piece);
                return pieces.ToString();
            }

            var text = string.Empty;
            await foreach (var ev in ((IStreamingLlmProvider)provider).ChatStreamingAsync(messages, null, Ct))
                text = ev.FinalResponse?.Error ?? text;
            return text;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    public static TheoryData<string, string> Calls()
    {
        var data = new TheoryData<string, string>();
        string[] vendors = ["openai", "anthropic", "ollama"];
        foreach (var vendor in vendors)
        {
            string[] calls = ["generate", "chat", "text", "stream"];
            foreach (var call in calls)
                data.Add(vendor, call);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task ShouldSayARefusalWithoutTheBytesItQuotes(string vendor, string call)
    {
        var provider = Build(vendor, "{\"error\":\"Invalid image: data:image/png;base64," + s_image + " in messages\"}");

        var error = await FailAsync(provider, call);

        var said = string.Join('\n', _logged) + error;
        Assert.Contains("Invalid image", error, StringComparison.Ordinal);
        Assert.Contains("in messages", error, StringComparison.Ordinal);
        for (var i = 0; i + 40 <= s_image.Length; i += 40)
            Assert.DoesNotContain(s_image.Substring(i, 40), said, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openai", "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Invalid image: IMG in messages\"}}")]
    [InlineData("anthropic", "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Invalid image: IMG in messages\"}}")]
    public async Task ShouldSayAnErrorInAStreamWithoutThem(string vendor, string frame)
    {
        var provider = Build(vendor, $"data: {frame.Replace("IMG", s_image, StringComparison.Ordinal)}\n\n", HttpStatusCode.OK);

        var error = await FailAsync(provider, "stream");

        Assert.Contains("in messages", error, StringComparison.Ordinal);
        Assert.DoesNotContain(s_image[..40], string.Join('\n', _logged) + error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("an ordinary sentence, /some/path/to/a/file.png and sk-abcdefghij1234567890abcdef", "an ordinary sentence, /some/path/to/a/file.png and ***REDACTED***")]
    [InlineData("data:image/png;base64,iVBORw0KGgoAAAANSUhEUg== end", "data:image/png;base64,[data not shown] end")]
    public void ShouldLeaveTheWordsOfAnError(string error, string expected) =>
        Assert.Equal(expected, Orkeon.Infrastructure.Security.LogSanitizer.SanitizeVendorError(error));

    [Theory]
    [InlineData(255, false)]
    [InlineData(256, true)]
    public void ShouldDropALongRunOfBase64(int length, bool dropped)
    {
        var run = new string('A', length);

        Assert.Equal(dropped ? "x [data not shown] y" : $"x {run} y", Orkeon.Infrastructure.Security.LogSanitizer.SanitizeVendorError($"x {run} y"));
    }

    [Fact]
    public void ShouldDropItAsJsonWritesIt()
    {
        var run = string.Concat(Enumerable.Repeat("abc\\/de\\u002Bf", 40));

        Assert.Equal("x [data not shown] y", Orkeon.Infrastructure.Security.LogSanitizer.SanitizeVendorError($"x {run}== y"));
    }
}
