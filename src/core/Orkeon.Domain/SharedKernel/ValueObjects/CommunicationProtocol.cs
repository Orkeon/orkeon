using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Http;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Communication protocol value object for agent interactions.
/// </summary>
public sealed record CommunicationProtocol : ValueObjectRecord
{
    /// <summary>Gets the protocol type.</summary>
    public ProtocolType Type { get; init; }
    /// <summary>Gets the message format (e.g., "json").</summary>
    public string Format { get; init; }
    /// <summary>Gets the protocol parameters.</summary>
    public CommunicationParameters Parameters { get; init; }
    /// <summary>Gets the communication timeout.</summary>
    public TimeSpan Timeout { get; init; }
    /// <summary>Gets the maximum number of retries.</summary>
    public int MaxRetries { get; init; }

    /// <summary>Initializes a new <see cref="CommunicationProtocol"/>.</summary>
    /// <param name="type">The protocol type.</param>
    /// <param name="format">The message format (default "json").</param>
    /// <param name="parameters">Optional protocol parameters.</param>
    /// <param name="timeout">Optional timeout (default 30s).</param>
    /// <param name="maxRetries">Maximum retries (default <see cref="AgentDefaults.MaxRetryLimit"/>, non-negative).</param>
    private CommunicationProtocol(
        ProtocolType type,
        string format = "json",
        CommunicationParameters? parameters = null,
        TimeSpan? timeout = null,
        int maxRetries = AgentDefaults.MaxRetryLimit)
    {
        Type = type;
#pragma warning disable CA1308 // lowercase is the stored/normalized wire form of the protocol format, not a comparison normalization
        Format = string.IsNullOrWhiteSpace(format) ? "json" : format.ToLowerInvariant();
#pragma warning restore CA1308
        Parameters = parameters ?? CommunicationParameters.Empty;
        Timeout = timeout ?? HttpDefaults.DefaultHttpTimeout;
        MaxRetries = maxRetries >= 0 ? maxRetries : throw new ArgumentException("Max retries cannot be negative", nameof(maxRetries));
    }

    /// <summary>Creates a new <see cref="CommunicationProtocol"/>.</summary>
    /// <param name="type">The protocol type.</param>
    /// <param name="format">The message format (default "json").</param>
    /// <param name="parameters">Optional protocol parameters.</param>
    /// <param name="timeout">Optional timeout (default 30s).</param>
    /// <param name="maxRetries">Maximum retries (default <see cref="AgentDefaults.MaxRetryLimit"/>, non-negative).</param>
    /// <returns>A new <see cref="CommunicationProtocol"/>.</returns>
    public static CommunicationProtocol Create(
        ProtocolType type,
        string format = "json",
        CommunicationParameters? parameters = null,
        TimeSpan? timeout = null,
        int maxRetries = AgentDefaults.MaxRetryLimit) =>
        new(type, format, parameters, timeout, maxRetries);

    /// <summary>Gets whether this is an asynchronous protocol.</summary>
    public bool IsAsync => Type == ProtocolType.MessageQueue || Type == ProtocolType.EventStream;
    /// <summary>Gets whether this is a synchronous protocol.</summary>
    public bool IsSync => !IsAsync;

    /// <summary>Returns a new instance with the specified parameter set.</summary>
    /// <param name="key">The parameter key.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>A new <see cref="CommunicationProtocol"/> with the parameter added.</returns>
    public CommunicationProtocol WithParameter(string key, object value) =>
        this with { Parameters = Parameters.With(key, value) };

    /// <summary>Gets a direct communication protocol.</summary>
    public static CommunicationProtocol Direct => new(ProtocolType.Direct);
    /// <summary>Gets a broadcast communication protocol.</summary>
    public static CommunicationProtocol Broadcast => new(ProtocolType.Broadcast);
    /// <summary>Gets a message queue communication protocol.</summary>
    public static CommunicationProtocol MessageQueue => new(ProtocolType.MessageQueue);

    /// <inheritdoc />
    public override string ToString() => $"{Type} ({Format}) - Timeout: {Timeout.TotalSeconds}s";
}

/// <summary>Communication protocol type.</summary>
public sealed record ProtocolType
{
    /// <summary>Gets the string value of this protocol type.</summary>
    public string Value { get; }
    private ProtocolType(string value) => Value = value;

    /// <summary>Direct point-to-point communication.</summary>
    public static readonly ProtocolType Direct = new("Direct");
    /// <summary>Broadcast communication to multiple recipients.</summary>
    public static readonly ProtocolType Broadcast = new("Broadcast");
    /// <summary>Asynchronous message queue communication.</summary>
    public static readonly ProtocolType MessageQueue = new("MessageQueue");
    /// <summary>Streaming event-based communication.</summary>
    public static readonly ProtocolType EventStream = new("EventStream");

    private static readonly Dictionary<string, ProtocolType> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Direct)] = Direct,
        [nameof(Broadcast)] = Broadcast,
        [nameof(MessageQueue)] = MessageQueue,
        [nameof(EventStream)] = EventStream,
    };

    /// <summary>Gets all valid protocol types.</summary>
    public static IReadOnlyCollection<ProtocolType> All => s_all.Values;

    /// <summary>Creates a <see cref="ProtocolType"/> from its string representation.</summary>
    public static ProtocolType From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown ProtocolType: '{value}'", nameof(value));

    /// <summary>Attempts to create a <see cref="ProtocolType"/> from its string representation.</summary>
    public static bool TryFrom(string? value, out ProtocolType? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(ProtocolType s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
