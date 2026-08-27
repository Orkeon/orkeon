using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Logging;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Logging;

public class LlmExchangeJsonLoggerTests : IAsyncLifetime
{
    private readonly string _physicalDir;
    private readonly DiskBackedFileSystemService _fs;
    private readonly LlmExchangeJsonLogger _logger;

    private const string VirtualLogDir = "/logs/llm";

    public LlmExchangeJsonLoggerTests()
    {
        _physicalDir = Path.Combine(Path.GetTempPath(), $"orkeon-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_physicalDir);
        _fs = new DiskBackedFileSystemService(_physicalDir, VirtualLogDir);
        _logger = new LlmExchangeJsonLogger(_fs, VirtualLogDir, NullLogger<LlmExchangeJsonLogger>.Instance);
    }

    public async ValueTask InitializeAsync()
    {
        await _logger.InitializeAsync();
    }

    public ValueTask DisposeAsync()
    {
        _logger.Dispose();
        try
        {
            if (Directory.Exists(_physicalDir))
                Directory.Delete(_physicalDir, recursive: true);
        }
        catch { }
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task LogExchangeAsync_CreatesJsonlFileWithCorrectName()
    {
        // Arrange
        var exchange = CreateSampleExchange();

        // Act
        await _logger.LogExchangeAsync(exchange, TestContext.Current.CancellationToken);

        // Assert — file is named with a run-scoped id: llm-exchanges-YYYY-MM-DDTHH-mm-ss-xxxxxx.jsonl
        var files = Directory.GetFiles(_physicalDir, "llm-exchanges-*.jsonl");
        Assert.Single(files);
        Assert.Matches(@"llm-exchanges-\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-[0-9a-f]{6}\.jsonl$", Path.GetFileName(files[0]));
    }

    [Fact]
    public async Task LogExchangeAsync_WritesValidJsonPerLine()
    {
        // Arrange
        var exchange1 = CreateSampleExchange(provider: "openai");
        var exchange2 = CreateSampleExchange(provider: "anthropic");

        // Act
        await _logger.LogExchangeAsync(exchange1, TestContext.Current.CancellationToken);
        await _logger.LogExchangeAsync(exchange2, TestContext.Current.CancellationToken);

        // Assert
        var file = Directory.GetFiles(_physicalDir, "llm-exchanges-*.jsonl").Single();
        var lines = await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(2, lines.Length);

        foreach (var line in lines)
        {
            // Each line must be valid JSON
            var doc = JsonDocument.Parse(line);
            Assert.NotNull(doc);
        }
    }

    [Fact]
    public async Task LogExchangeAsync_CapturesAllFields()
    {
        // Arrange
        var exchange = CreateSampleExchange(
            provider: "openai",
            model: "gpt-4",
            statusCode: 200,
            durationMs: 1234.5);

        // Act
        await _logger.LogExchangeAsync(exchange, TestContext.Current.CancellationToken);

        // Assert
        var file = Directory.GetFiles(_physicalDir, "llm-exchanges-*.jsonl").Single();
        var line = (await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken)).Single();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("openai", root.GetProperty("provider").GetString());
        Assert.Equal("gpt-4", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("is_success").GetBoolean());
        Assert.True(root.GetProperty("duration_ms").GetDouble() > 1000);

        // Request section
        var request = root.GetProperty("request");
        Assert.Equal("POST", request.GetProperty("method").GetString());
        Assert.NotEqual(JsonValueKind.Null, request.GetProperty("headers").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, request.GetProperty("body").ValueKind);

        // Response section
        var response = root.GetProperty("response");
        Assert.Equal(200, response.GetProperty("status_code").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, response.GetProperty("headers").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, response.GetProperty("body").ValueKind);
    }

    [Fact]
    public async Task LogExchangeAsync_RequestBodyIsStructuredJson()
    {
        // Arrange — the request body is valid JSON, so it should be stored as a JSON object (not a string)
        var exchange = CreateSampleExchange();

        // Act
        await _logger.LogExchangeAsync(exchange, TestContext.Current.CancellationToken);

        // Assert
        var file = Directory.GetFiles(_physicalDir, "llm-exchanges-*.jsonl").Single();
        var line = (await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken)).Single();
        using var doc = JsonDocument.Parse(line);

        var requestBody = doc.RootElement.GetProperty("request").GetProperty("body");
        // Should be a JSON object, not a string
        Assert.Equal(JsonValueKind.Object, requestBody.ValueKind);
        Assert.Equal("gpt-4", requestBody.GetProperty("model").GetString());
    }

    [Fact]
    public async Task LogExchangeAsync_IsThreadSafe()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 20)
            .Select(i => _logger.LogExchangeAsync(
                CreateSampleExchange(provider: $"provider-{i}")));

        // Act
        await Task.WhenAll(tasks);

        // Assert
        var file = Directory.GetFiles(_physicalDir, "*.jsonl").Single();
        var lines = await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken);
        Assert.Equal(20, lines.Length);

        // All lines must be valid JSON
        foreach (var line in lines)
        {
            Assert.NotNull(JsonDocument.Parse(line));
        }
    }

    private static LlmExchangeRecord CreateSampleExchange(
        string provider = "openai",
        string model = "gpt-4",
        int statusCode = 200,
        double durationMs = 500)
    {
        return new LlmExchangeRecord
        {
            ExchangeId = Guid.NewGuid().ToString("N")[..12],
            Timestamp = DateTimeOffset.UtcNow,
            Provider = provider,
            HttpMethod = "POST",
            RequestUrl = new Uri($"https://api.{provider}.com/v1/chat/completions"),
            RequestHeaders = new Dictionary<string, string[]>
            {
                ["Content-Type"] = ["application/json"],
                ["Authorization"] = ["Bearer ***REDACTED***"]
            },
            RequestBody = $$$"""{"model":"{{{model}}}","messages":[{"role":"user","content":"Hello"}]}""",
            StatusCode = statusCode,
            ResponseHeaders = new Dictionary<string, string[]>
            {
                ["Content-Type"] = ["application/json"],
                ["x-request-id"] = ["req_abc123"]
            },
            ResponseBody = """{"choices":[{"message":{"content":"Hi there!"}}],"usage":{"total_tokens":15}}""",
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Model = model,
            IsStreaming = false
        };
    }
}
