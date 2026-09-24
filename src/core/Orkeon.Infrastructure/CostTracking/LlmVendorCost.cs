using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.CostTracking;

/// <summary>
/// What a vendor billed for one call, read back from the response its provider built — the
/// real charge, never an estimate (STUDIO-29, DD-1).
/// <para>
/// Only a vendor that bills in its answer has one. OpenRouter writes it in <c>usage.cost</c>;
/// the OpenAI-compatible base copies it under <see cref="LlmUsageMetadataKeys.Cost"/>, and a
/// provider that knows the currency its vendor bills in states it beside it
/// (<see cref="LlmUsageMetadataKeys.CostCurrency"/>). Every other vendor leaves both keys
/// absent, and absent reads as UNKNOWN: a present 0 is a free call and stays a 0.
/// </para>
/// </summary>
public static class LlmVendorCost
{
    // The largest double that converts to decimal without overflowing.
    private const double MaxDecimalAsDouble = 7.9e28;

    /// <summary>Reads the vendor's charge for <paramref name="response"/>.</summary>
    /// <param name="response">The provider's answer.</param>
    /// <param name="cost">The charge as billed, 0 included; 0 when there is none.</param>
    /// <param name="currency">The ISO 4217 code the provider stated for it, or null.</param>
    /// <returns>True when the vendor billed the call in its answer.</returns>
    public static bool TryRead(LlmResponse response, out decimal cost, out string? currency)
    {
        ArgumentNullException.ThrowIfNull(response);

        cost = 0m;
        currency = null;
        if (!response.Metadata.TryGetValue(LlmUsageMetadataKeys.Cost, out var charged))
            return false;

        switch (charged)
        {
            case decimal exact:
                cost = exact;
                break;
            // What the providers write: the JSON number as System.Text.Json reads it. A value a
            // decimal cannot hold is nothing a meter can show, and must not fail the call.
            case double number when double.IsFinite(number) && Math.Abs(number) <= MaxDecimalAsDouble:
                cost = (decimal)number;
                break;
            default:
                return false;
        }

        currency = response.Metadata.TryGetValue(LlmUsageMetadataKeys.CostCurrency, out var code)
            && code is string { Length: > 0 } text
                ? text
                : null;
        return true;
    }
}
