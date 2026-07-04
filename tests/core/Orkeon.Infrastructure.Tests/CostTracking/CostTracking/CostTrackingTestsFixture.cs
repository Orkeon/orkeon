using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.CostTracking;

namespace Orkeon.Infrastructure.Tests;

public class ModelPricingRegistryTestsFixture
{
    private readonly ModelPricingRegistry _sut;

    public ModelPricingRegistryTestsFixture()
    {
        _sut = new ModelPricingRegistry(
        Options.Create(new CostTrackingOptions()),
        NullLogger<ModelPricingRegistry>.Instance);
    }

    public ModelPricingRegistry GetSut() => _sut;

}
