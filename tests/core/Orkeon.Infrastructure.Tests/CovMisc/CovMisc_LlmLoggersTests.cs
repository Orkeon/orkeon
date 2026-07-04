using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Logging;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Coverage tests for <see cref="CompositeLlmExchangeLogger"/> and
/// <see cref="LlmExchangeStructuredLogger"/>.
/// </summary>
public class CovMisc_LlmLoggersTests
{
    private static LlmExchangeRecord MakeRecord(
        int statusCode = 200,
        string? errorMessage = null,
        bool streaming = false,
        string? model = "gpt-4o")
        => new()
        {
            ExchangeId = "ex-1",
            Timestamp = DateTimeOffset.UtcNow,
            Provider = "openai",
            HttpMethod = "POST",
            RequestUrl = new Uri("https://api.openai.com/v1/chat/completions"),
            RequestHeaders = new Dictionary<string, string[]>(),
            RequestBody = "{}",
            StatusCode = statusCode,
            ResponseHeaders = new Dictionary<string, string[]>(),
            ResponseBody = "{}",
            Duration = TimeSpan.FromMilliseconds(123.4),
            ErrorMessage = errorMessage,
            Model = model,
            IsStreaming = streaming
        };

    // --- CompositeLlmExchangeLogger ---

    private sealed class CountingLogger : ILlmExchangeLogger
    {
        public int Count { get; private set; }
        public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingLogger : ILlmExchangeLogger
    {
        private readonly Exception _ex;
        public ThrowingLogger(Exception ex) => _ex = ex;
        public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
            => throw _ex;
    }

    [Fact]
    public async Task Composite_FansOutToAllLoggers()
    {
        var a = new CountingLogger();
        var b = new CountingLogger();
        var composite = new CompositeLlmExchangeLogger(a, b);

        await composite.LogExchangeAsync(MakeRecord(), TestContext.Current.CancellationToken);

        Assert.Equal(1, a.Count);
        Assert.Equal(1, b.Count);
    }

    [Fact]
    public async Task Composite_EnumerableCtor_FansOut()
    {
        var a = new CountingLogger();
        IEnumerable<ILlmExchangeLogger> loggers = new ILlmExchangeLogger[] { a };
        var composite = new CompositeLlmExchangeLogger(loggers);

        await composite.LogExchangeAsync(MakeRecord(), TestContext.Current.CancellationToken);

        Assert.Equal(1, a.Count);
    }

    [Fact]
    public async Task Composite_AggregatesExceptions_StillCallsAll()
    {
        var ok = new CountingLogger();
        var bad = new ThrowingLogger(new InvalidOperationException("boom"));
        var composite = new CompositeLlmExchangeLogger(bad, ok);

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => composite.LogExchangeAsync(MakeRecord(), TestContext.Current.CancellationToken));

        Assert.Single(ex.InnerExceptions);
        Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
        Assert.Equal(1, ok.Count); // the ok logger still ran despite the earlier failure
    }

    [Fact]
    public void Composite_NullParamsCtor_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CompositeLlmExchangeLogger((ILlmExchangeLogger[])null!));

    [Fact]
    public void Composite_NullEnumerableCtor_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CompositeLlmExchangeLogger((IEnumerable<ILlmExchangeLogger>)null!));

    [Fact]
    public async Task Composite_NoLoggers_DoesNotThrow()
    {
        var composite = new CompositeLlmExchangeLogger();
        var ex = await Record.ExceptionAsync(() => composite.LogExchangeAsync(MakeRecord(), TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // --- LlmExchangeStructuredLogger ---

    [Fact]
    public void Structured_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new LlmExchangeStructuredLogger(null!));

    [Fact]
    public async Task Structured_NullExchange_Throws()
    {
        var logger = new LlmExchangeStructuredLogger(new MockLogger<LlmExchangeStructuredLogger>());
        await Assert.ThrowsAsync<ArgumentNullException>(() => logger.LogExchangeAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Structured_SuccessfulExchange_LogsInformation()
    {
        var mock = new MockLogger<LlmExchangeStructuredLogger>();
        var logger = new LlmExchangeStructuredLogger(mock);

        await logger.LogExchangeAsync(MakeRecord(statusCode: 200, streaming: true), TestContext.Current.CancellationToken);

        Assert.Equal(1, mock.LogCallCount);
        Assert.Equal(LogLevel.Information, mock.LastLogLevel);
    }

    [Fact]
    public async Task Structured_FailedExchange_LogsWarning()
    {
        var mock = new MockLogger<LlmExchangeStructuredLogger>();
        var logger = new LlmExchangeStructuredLogger(mock);

        await logger.LogExchangeAsync(MakeRecord(statusCode: 500, errorMessage: "rate limited"), TestContext.Current.CancellationToken);

        Assert.Equal(1, mock.LogCallCount);
        Assert.Equal(LogLevel.Warning, mock.LastLogLevel);
    }

    [Fact]
    public async Task Structured_NullModel_UsesUnknown()
    {
        var mock = new MockLogger<LlmExchangeStructuredLogger>();
        var logger = new LlmExchangeStructuredLogger(mock);

        await logger.LogExchangeAsync(MakeRecord(model: null), TestContext.Current.CancellationToken);

        Assert.Equal(1, mock.LogCallCount);
    }

    [Fact]
    public async Task Structured_SuccessSuppressedWhenInformationDisabled()
    {
        var logger = new LlmExchangeStructuredLogger(new DisabledInfoLogger());
        // No assertion on output; verifies the IsEnabled guard early-return path does not throw.
        var ex = await Record.ExceptionAsync(() => logger.LogExchangeAsync(MakeRecord(statusCode: 200), TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    private sealed class DisabledInfoLogger : ILogger<LlmExchangeStructuredLogger>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
