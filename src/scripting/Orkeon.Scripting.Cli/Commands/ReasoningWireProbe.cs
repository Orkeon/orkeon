using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// What the reasoning probes (M15-M17) read back after each step: the reasoning fields the
/// provider really put on the wire, and the warnings it logged about them.
/// </summary>
internal interface IReasoningWireProbe
{
    /// <summary>Forgets the previous step: the captured fields and the warnings.</summary>
    void Reset();

    /// <summary>The reasoning fields of the last request, as a compact JSON object, or null when none was seen.</summary>
    string? LastFields { get; }

    /// <summary>The Warning-or-worse messages the provider loggers produced since <see cref="Reset"/>.</summary>
    IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Keeps, for each JSON request that passes, only the top-level reasoning fields of its body.
/// Headers are never read and nothing else of the body is retained, so no credential and no
/// prompt can end up in a report.
/// </summary>
internal sealed class ReasoningWireRecorder : DelegatingHandler
{
    private static readonly string[] s_topLevelFields =
        ["thinking", "reasoning_effort", "reasoning", "think", "enable_thinking", "thinking_budget"];

    private readonly object _gate = new();
    private string? _last;

    /// <summary>Initializes a recorder in front of a plain <see cref="HttpClientHandler"/>.</summary>
#pragma warning disable CA2000 // The DelegatingHandler owns and disposes its inner handler.
    public ReasoningWireRecorder()
        : base(new HttpClientHandler())
#pragma warning restore CA2000
    {
    }

    /// <summary>Initializes a recorder in front of <paramref name="inner"/>.</summary>
    /// <param name="inner">The handler that really sends the request.</param>
    public ReasoningWireRecorder(HttpMessageHandler inner)
        : base(inner)
    {
    }

    /// <summary>The fields captured from the last request, or null.</summary>
    public string? LastFields
    {
        get { lock (_gate) return _last; }
    }

    /// <summary>Forgets the captured fields.</summary>
    public void Reset()
    {
        lock (_gate)
            _last = null;
    }

    /// <summary>Extracts the reasoning fields of a request body; null for a non-JSON or empty body.</summary>
    /// <param name="body">The request body text.</param>
    /// <returns>A compact JSON object, or null when nothing relevant is there.</returns>
    internal static string? Extract(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            using var stream = new MemoryStream();
            var found = false;
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var name in s_topLevelFields)
                {
                    if (!doc.RootElement.TryGetProperty(name, out var value))
                        continue;
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                    found = true;
                }

                if (doc.RootElement.TryGetProperty("output_config", out var config)
                    && config.ValueKind == JsonValueKind.Object
                    && config.TryGetProperty("effort", out var effort))
                {
                    writer.WritePropertyName("output_config");
                    writer.WriteStartObject();
                    writer.WritePropertyName("effort");
                    effort.WriteTo(writer);
                    writer.WriteEndObject();
                    found = true;
                }

                writer.WriteEndObject();
            }

            return found ? System.Text.Encoding.UTF8.GetString(stream.ToArray()) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is { } content)
        {
            // Buffering keeps the content readable twice: here, then by the inner handler.
            await content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var fields = Extract(body);
            lock (_gate)
                _last = fields;
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Keeps the Warning-or-worse messages of every logger it hands out.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];

    /// <summary>The captured messages, in order.</summary>
    public IReadOnlyList<string> Messages
    {
        get { lock (_gate) return [.. _messages]; }
    }

    /// <summary>Forgets the captured messages.</summary>
    public void Clear()
    {
        lock (_gate)
            _messages.Clear();
    }

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Add(string message)
    {
        lock (_gate)
            _messages.Add(message);
    }

    private sealed class CapturingLogger(CapturingLoggerFactory owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            owner.Add(formatter(state, exception));
        }
    }
}

/// <summary>The wire recorder and the warning capture of one probe run, read as one.</summary>
internal sealed class ReasoningWireProbe : IReasoningWireProbe
{
    /// <summary>Initializes the probe; hand <see cref="LoggerFactory"/> and the HTTP factory's recorder to the provider.</summary>
    public ReasoningWireProbe()
    {
        LoggerFactory = new CapturingLoggerFactory();
    }

    /// <summary>The recorder installed in front of the HTTP handler; set by <see cref="CliHttpClientFactory"/>.</summary>
    public ReasoningWireRecorder? Recorder { get; internal set; }

    /// <summary>The factory the provider's loggers come from.</summary>
    public CapturingLoggerFactory LoggerFactory { get; }

    /// <inheritdoc />
    public string? LastFields => Recorder?.LastFields;

    /// <inheritdoc />
    public IReadOnlyList<string> Warnings => LoggerFactory.Messages;

    /// <inheritdoc />
    public void Reset()
    {
        Recorder?.Reset();
        LoggerFactory.Clear();
    }
}
