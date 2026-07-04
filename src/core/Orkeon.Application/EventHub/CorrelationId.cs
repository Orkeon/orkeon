using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Strongly-typed correlation identifier linking a <c>Send</c> request to its matching <c>Reply</c>.
/// Backed by a ULID; common identifier contract inherited from <see cref="TypedId"/>.
/// </summary>
public sealed class CorrelationId : TypedId
{
    private CorrelationId(Ulid value) : base(value) { }

    /// <summary>Generates a new unique <see cref="CorrelationId"/>.</summary>
    public static CorrelationId NewId() => new(Ulid.NewUlid());

    /// <summary>Creates a <see cref="CorrelationId"/> from an existing ULID value.</summary>
    public static CorrelationId From(Ulid value) => new(value);

    /// <summary>Creates a <see cref="CorrelationId"/> from its canonical string representation (strict ULID parse).</summary>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is null/empty/whitespace, or not a valid ULID string.</exception>
    public static CorrelationId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("CorrelationId cannot be null, empty, or whitespace.", nameof(value));
        return new CorrelationId(Ulid.Parse(value, CultureInfo.InvariantCulture));
    }
}
