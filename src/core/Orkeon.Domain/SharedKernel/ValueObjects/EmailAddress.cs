using System.Text.RegularExpressions;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

// ID types are defined in Orkeon.Domain.Common.Identity - using those instead

/// <summary>
/// Email address value object with validation.
/// </summary>
public sealed partial record EmailAddress : ValueObjectRecord
{
    /// <summary>Gets the normalized email address string.</summary>
    public string Value { get; }

    /// <summary>Initializes a new <see cref="EmailAddress"/> with validation.</summary>
    /// <param name="value">The raw email address string.</param>
    private EmailAddress(string value)
    {
        Value = ValidateEmail(value);
        Validate();
    }

    private static string ValidateEmail(string email)
    {
        EnsureNotNullOrWhiteSpace(email, nameof(email));

#pragma warning disable CA1308 // lowercase is the stored/canonical form of the email address, not a comparison normalization
        var trimmed = email.Trim().ToLowerInvariant();
#pragma warning restore CA1308

        if (trimmed.Length > 254)
            throw new ArgumentException("Email address too long", nameof(email));

        if (!EmailFormatRegex().IsMatch(trimmed))
            throw new ArgumentException("Invalid email format", nameof(email));

        return trimmed;
    }

    /// <summary>Gets the domain part of the email address.</summary>
    public string Domain => Value.Split('@')[1];
    /// <summary>Gets the local part (before @) of the email address.</summary>
    public string LocalPart => Value.Split('@')[0];

    /// <summary>Creates an <see cref="EmailAddress"/> from a string value.</summary>
    /// <param name="value">The raw email address string.</param>
    /// <returns>A validated <see cref="EmailAddress"/>.</returns>
    public static EmailAddress From(string value) => new(value);
    /// <summary>Implicitly converts an <see cref="EmailAddress"/> to a string.</summary>
    /// <param name="email">The email address to convert.</param>
    public static implicit operator string(EmailAddress email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Value;
    }
    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailFormatRegex();
}
