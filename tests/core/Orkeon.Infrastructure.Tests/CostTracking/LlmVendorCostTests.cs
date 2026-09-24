using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.CostTracking;

namespace Orkeon.Infrastructure.Tests.CostTracking;

/// <summary>
/// What a vendor billed for one call, read back from the metadata the provider wrote
/// (STUDIO-29). Absent means unknown — never free — and a billed 0 is a free call that must
/// stay 0 all the way to the wire.
/// </summary>
public sealed class LlmVendorCostTests
{
    private static LlmResponse Billed(object cost, string? currency = null)
    {
        var metadata = new Dictionary<string, object> { ["cost"] = cost };
        if (currency is not null)
            metadata["cost_currency"] = currency;
        return new LlmResponse { Content = "ok", Metadata = metadata };
    }

    [Fact]
    public void The_charge_the_provider_wrote_is_read_as_billed_with_its_currency()
    {
        Assert.True(LlmVendorCost.TryRead(Billed(0.00042, "USD"), out var cost, out var currency));

        Assert.Equal(0.00042m, cost);
        Assert.Equal("USD", currency);
    }

    [Fact]
    public void A_free_call_is_billed_zero_and_stays_zero()
    {
        Assert.True(LlmVendorCost.TryRead(Billed(0.0, "USD"), out var cost, out _));

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void A_response_the_vendor_did_not_bill_has_no_charge()
    {
        Assert.False(LlmVendorCost.TryRead(new LlmResponse { Content = "ok" }, out _, out var currency));

        Assert.Null(currency);
    }

    [Fact]
    public void A_charge_whose_currency_nobody_stated_is_read_without_one()
    {
        Assert.True(LlmVendorCost.TryRead(Billed(0.0125), out var cost, out var currency));

        Assert.Equal(0.0125m, cost);
        Assert.Null(currency);
    }

    [Fact]
    public void A_value_that_is_not_an_amount_is_no_charge()
    {
        // A decimal cannot hold these, and a meter must not fail the call it observes.
        Assert.False(LlmVendorCost.TryRead(Billed(double.NaN, "USD"), out _, out _));
        Assert.False(LlmVendorCost.TryRead(Billed(1e300, "USD"), out _, out _));
        Assert.False(LlmVendorCost.TryRead(Billed("0.01", "USD"), out _, out _));
    }
}
