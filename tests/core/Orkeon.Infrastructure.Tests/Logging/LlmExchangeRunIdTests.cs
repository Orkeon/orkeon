using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Logging;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Logging;

/// <summary>
/// Two loggers built in the same second write to two files.
/// <para>
/// The run id was a UTC timestamp truncated to the second, so the class's own claim that
/// "each logger instance creates a unique file scoped to that run" was false for any two runs
/// started within the same second — and its per-file lock serializes writers inside one
/// process, never across two. Interleaved lines from two runs, in a file named after one of
/// them, is an exchange log nobody can read back.
/// </para>
/// </summary>
public sealed class LlmExchangeRunIdTests : IDisposable
{
    private readonly string _physicalDir =
        Path.Combine(Path.GetTempPath(), "orkeon-llmlog-" + Guid.NewGuid().ToString("N"));

    public LlmExchangeRunIdTests() => Directory.CreateDirectory(_physicalDir);

    public void Dispose()
    {
        if (Directory.Exists(_physicalDir))
            Directory.Delete(_physicalDir, recursive: true);
    }

    [Fact]
    public async Task Two_loggers_created_back_to_back_do_not_share_a_file()
    {
        var fs = new DiskBackedFileSystemService(_physicalDir, "/logs");

        using var first = new LlmExchangeJsonLogger(fs, "/logs", NullLogger<LlmExchangeJsonLogger>.Instance);
        using var second = new LlmExchangeJsonLogger(fs, "/logs", NullLogger<LlmExchangeJsonLogger>.Instance);

        await first.LogExchangeAsync(Exchange("first"), TestContext.Current.CancellationToken);
        await second.LogExchangeAsync(Exchange("second"), TestContext.Current.CancellationToken);

        var files = Directory.GetFiles(_physicalDir, "llm-exchanges-*.jsonl");
        Assert.Equal(2, files.Length);
        Assert.All(files, file => Assert.Single(File.ReadAllLines(file)));
    }

    private static LlmExchangeRecord Exchange(string provider) => new()
    {
        ExchangeId = provider,
        Timestamp = DateTimeOffset.UnixEpoch,
        Provider = provider,
        HttpMethod = "POST",
        RequestUrl = new Uri("https://api.example.test/v1/chat/completions"),
        RequestHeaders = new Dictionary<string, string[]>(StringComparer.Ordinal),
        ResponseHeaders = new Dictionary<string, string[]>(StringComparer.Ordinal),
        RequestBody = "{}",
        StatusCode = 200,
        ResponseBody = "{}",
        Duration = TimeSpan.Zero,
    };
}
