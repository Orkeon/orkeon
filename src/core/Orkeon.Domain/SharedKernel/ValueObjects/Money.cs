using System.Globalization;
using System.Text.RegularExpressions;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Platform;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Money value object with currency support.
/// </summary>
public sealed partial record Money : ValueObjectRecord
{
    /// <summary>Gets the monetary amount (non-negative).</summary>
    public decimal Amount { get; }
    /// <summary>Gets the ISO 4217 currency code (e.g., "USD").</summary>
    public string Currency { get; }

    /// <summary>Initializes a new <see cref="Money"/> value.</summary>
    /// <param name="amount">The monetary amount (non-negative).</param>
    /// <param name="currency">The ISO 4217 currency code (default "USD").</param>
    private Money(decimal amount, string currency = PlatformDefaults.DefaultCurrency)
    {
        Amount = EnsureNonNegative(amount, nameof(amount));
        Currency = ValidateCurrency(currency);
        Validate();
    }

    /// <summary>Creates a new <see cref="Money"/> value.</summary>
    /// <param name="amount">The monetary amount (non-negative).</param>
    /// <param name="currency">The ISO 4217 currency code (default "USD").</param>
    /// <returns>A new <see cref="Money"/>.</returns>
    public static Money Create(decimal amount, string currency = PlatformDefaults.DefaultCurrency) => new(amount, currency);

    private static string ValidateCurrency(string currency)
    {
        EnsureNotNullOrWhiteSpace(currency, nameof(currency));

        if (currency.Length != 3)
            throw new ArgumentException("Currency must be 3 characters", nameof(currency));

        var normalized = currency.ToUpperInvariant();

        if (!CurrencyCodeRegex().IsMatch(normalized))
            throw new ArgumentException("Invalid currency format", nameof(currency));

        return normalized;
    }

    /// <summary>Returns a new <see cref="Money"/> with the amounts summed.</summary>
    /// <param name="other">The amount to add (must have the same currency).</param>
    /// <returns>A new <see cref="Money"/> instance with the summed amount.</returns>
    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add different currencies");
        return Create(Amount + other.Amount, Currency);
    }

    /// <summary>Returns a new <see cref="Money"/> with the amount subtracted.</summary>
    /// <param name="other">The amount to subtract (must have the same currency).</param>
    /// <returns>A new <see cref="Money"/> instance with the difference.</returns>
    public Money Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot subtract different currencies");
        return Create(Amount - other.Amount, Currency);
    }

    /// <summary>Returns a new <see cref="Money"/> multiplied by the given factor.</summary>
    /// <param name="factor">The multiplication factor.</param>
    /// <returns>A new <see cref="Money"/> with the scaled amount.</returns>
    public Money Multiply(decimal factor) => Create(Amount * factor, Currency);

    /// <summary>Returns a new <see cref="Money"/> divided by the given divisor.</summary>
    /// <param name="divisor">The divisor.</param>
    /// <returns>A new <see cref="Money"/> with the divided amount.</returns>
    public Money Divide(decimal divisor) => Create(Amount / divisor, Currency);

    /// <summary>Returns a zero-amount <see cref="Money"/> for the given currency.</summary>
    /// <param name="currency">The ISO 4217 currency code (default "USD").</param>
    /// <returns>A <see cref="Money"/> with amount zero.</returns>
    public static Money Zero(string currency = PlatformDefaults.DefaultCurrency) => Create(0, currency);
    /// <summary>Creates a USD <see cref="Money"/> value.</summary>
    /// <param name="amount">The amount in USD.</param>
    /// <returns>A <see cref="Money"/> in USD.</returns>
    public static Money Usd(decimal amount) => Create(amount, PlatformDefaults.DefaultCurrency);
    /// <summary>Creates a EUR <see cref="Money"/> value.</summary>
    /// <param name="amount">The amount in EUR.</param>
    /// <returns>A <see cref="Money"/> in EUR.</returns>
    public static Money Eur(decimal amount) => Create(amount, "EUR");

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:C} {Currency}");

    [GeneratedRegex(@"^[A-Z]{3}$")]
    private static partial Regex CurrencyCodeRegex();
}
