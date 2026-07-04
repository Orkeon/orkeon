using System.Net;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs;

public class OllamaLlmProviderTestsFixture
{
    private readonly HttpStatusCode _statusCode = HttpStatusCode.OK;
    private readonly string _content = "";
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<string> _requestContents = [];
    private readonly Exception? _exceptionToThrow = null;
    private readonly int _delayMs = 0;

    public OllamaLlmProviderTestsFixture()
    {
    }

    public OllamaLlmProviderTestsFixture WithStatusCode(HttpStatusCode value)
    {
        // Configure _statusCode as needed
        return this;
    }

    public OllamaLlmProviderTestsFixture WithContent(string value)
    {
        // Configure _content as needed
        return this;
    }

    public OllamaLlmProviderTestsFixture WithRequests(List<HttpRequestMessage> value)
    {
        // Configure _requests as needed
        return this;
    }

    public OllamaLlmProviderTestsFixture WithRequestContents(List<string> value)
    {
        // Configure _requestContents as needed
        return this;
    }

    public OllamaLlmProviderTestsFixture WithExceptionToThrow(Exception? value)
    {
        // Configure _exceptionToThrow as needed
        return this;
    }

    public OllamaLlmProviderTestsFixture WithDelayMs(int value)
    {
        // Configure _delayMs as needed
        return this;
    }

    public HttpStatusCode GetStatusCode() => _statusCode;
    public string GetContent() => _content;
    public List<HttpRequestMessage> GetRequests() => _requests;
    public List<string> GetRequestContents() => _requestContents;
    public Exception? GetExceptionToThrow() => _exceptionToThrow;
    public int GetDelayMs() => _delayMs;

#pragma warning restore CS0618
}
