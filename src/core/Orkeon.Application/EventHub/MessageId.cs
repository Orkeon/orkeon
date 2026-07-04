using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Strongly-typed identifier for a single <see cref="Message"/> in the EventHub.
/// Backed by a ULID (lexicographically sortable, 26-char Crockford base32 form);
/// common identifier contract inherited from <see cref="TypedId"/>.
/// </summary>
public sealed class MessageId : TypedId
{
    private MessageId(Ulid value) : base(value) { }

    /// <summary>Generates a new unique <see cref="MessageId"/>.</summary>
    public static MessageId NewId() => new(Ulid.NewUlid());

    /// <summary>Creates a <see cref="MessageId"/> from an existing ULID value.</summary>
    public static MessageId From(Ulid value) => new(value);

    /// <summary>Creates a <see cref="MessageId"/> from its canonical string representation (strict ULID parse).</summary>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is null/empty/whitespace, or not a valid ULID string.</exception>
    public static MessageId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("MessageId cannot be null, empty, or whitespace.", nameof(value));
        return new MessageId(Ulid.Parse(value, CultureInfo.InvariantCulture));
    }
}
