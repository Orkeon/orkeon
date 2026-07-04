using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Logging;

/// <summary>
/// Persists LLM exchange records as JSON Lines (.jsonl) files via the virtual file system.
/// Each line is a self-contained JSON object representing one exchange.
/// <para>
/// Each logger instance creates a unique file scoped to that run:
/// <c>llm-exchanges-2026-04-12T14-30-05.jsonl</c>.
/// Thread-safe via a per-file <see cref="SemaphoreSlim"/> lock.
/// </para>
/// </summary>
public sealed partial class LlmExchangeJsonLogger : ILlmExchangeLogger, IDisposable
{
    private readonly IFileSystemService _fs;
    private readonly string _logDirectory;
    private readonly string _runId;
    private readonly ILogger<LlmExchangeJsonLogger> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="LlmExchangeJsonLogger"/> using the virtual file system.
    /// </summary>
    /// <param name="fs">Virtual file system service.</param>
    /// <param name="logVirtualDir">Virtual directory where .jsonl files are written (e.g. <c>/logs/llm</c>).</param>
    /// <param name="logger">Optional structured logger.</param>
    public LlmExchangeJsonLogger(
        IFileSystemService fs,
        string logVirtualDir,
        ILogger<LlmExchangeJsonLogger>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(logVirtualDir);
        _fs = fs;
        _logDirectory = logVirtualDir;
        _runId = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmExchangeJsonLogger>.Instance;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _jsonOptions.Converters.Add(new TimeSpanMillisecondsConverter());
    }

    /// <summary>
    /// Ensures the log directory exists. Call once during application startup.
    /// </summary>
    public async System.Threading.Tasks.Task InitializeAsync(CancellationToken ct = default)
    {
        await _fs.CreateDirectoryAsync(_logDirectory, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(exchange);
        return LogExchangeCoreAsync(exchange, cancellationToken);
    }

    private async System.Threading.Tasks.Task LogExchangeCoreAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken)
    {
        var fileName = $"llm-exchanges-{_runId}.jsonl";
        var filePath = $"{_logDirectory}/{fileName}";

        var dto = LlmExchangeDto.FromRecord(exchange);
        var jsonLine = JsonSerializer.Serialize(dto, _jsonOptions);

        var fileLock = _fileLocks.GetOrAdd(fileName, _ => new SemaphoreSlim(1, 1));

        await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _fs.AppendAllTextAsync(filePath, jsonLine + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            LogExchangeWritten(exchange.ExchangeId, exchange.Provider, filePath);
        }
        finally
        {
            fileLock.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "LLM exchange {ExchangeId} ({Provider}) written to {FilePath}")]
    private partial void LogExchangeWritten(string exchangeId, string provider, string filePath);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _fileLocks)
        {
            kvp.Value.Dispose();
        }
        _fileLocks.Clear();
    }

    /// <summary>
    /// Internal DTO for JSON serialization of exchange records.
    /// Separates serialization concerns from the domain record.
    /// </summary>
    private sealed class LlmExchangeDto
    {
        [JsonPropertyName("exchange_id")]
        public string ExchangeId { get; init; } = "";

        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; init; }

        [JsonPropertyName("provider")]
        public string Provider { get; init; } = "";

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("is_streaming")]
        public bool IsStreaming { get; init; }

        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; init; }

        [JsonPropertyName("is_success")]
        public bool IsSuccess { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("request")]
        public ExchangeRequestDto Request { get; init; } = new();

        [JsonPropertyName("response")]
        public ExchangeResponseDto Response { get; init; } = new();

        public static LlmExchangeDto FromRecord(LlmExchangeRecord record) => new()
        {
            ExchangeId = record.ExchangeId,
            Timestamp = record.Timestamp,
            Provider = record.Provider,
            Model = record.Model,
            IsStreaming = record.IsStreaming,
            DurationMs = record.Duration.TotalMilliseconds,
            IsSuccess = record.IsSuccess,
            Error = record.ErrorMessage,
            Request = new ExchangeRequestDto
            {
                Method = record.HttpMethod,
                Url = record.RequestUrl?.ToString() ?? "",
                Headers = record.RequestHeaders,
                Body = TryParseJson(record.RequestBody)
            },
            Response = new ExchangeResponseDto
            {
                StatusCode = record.StatusCode,
                Headers = record.ResponseHeaders,
                Body = TryParseJson(record.ResponseBody)
            }
        };

        /// <summary>
        /// Attempts to parse a string as JSON to produce structured output.
        /// Falls back to the raw string wrapped in a JSON value.
        /// </summary>
        private static object? TryParseJson(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(text);
            }
            catch (JsonException)
            {
                return text;
            }
        }
    }

    private sealed class ExchangeRequestDto
    {
        [JsonPropertyName("method")]
        public string Method { get; init; } = "";

        [JsonPropertyName("url")]
        public string Url { get; init; } = "";

        [JsonPropertyName("headers")]
        public IReadOnlyDictionary<string, string[]> Headers { get; init; } = new Dictionary<string, string[]>();

        [JsonPropertyName("body")]
        public object? Body { get; init; }
    }

    private sealed class ExchangeResponseDto
    {
        [JsonPropertyName("status_code")]
        public int StatusCode { get; init; }

        [JsonPropertyName("headers")]
        public IReadOnlyDictionary<string, string[]> Headers { get; init; } = new Dictionary<string, string[]>();

        [JsonPropertyName("body")]
        public object? Body { get; init; }
    }

    /// <summary>
    /// Serializes <see cref="TimeSpan"/> as milliseconds (double).
    /// </summary>
    private sealed class TimeSpanMillisecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.FromMilliseconds(reader.GetDouble());
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(Math.Round(value.TotalMilliseconds, 2));
        }
    }
}
