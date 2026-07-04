using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.DependencyInjection;
using Orkeon.Tools.Abstractions.Tools;

namespace Orkeon.Tools.Abstractions.Tests.DependencyInjection;

/// <summary>
/// Regression tests for experiment 07 issue #13: `list_mounts` was previously orphaned —
/// the class existed but no DI extension registered it, so crews advertising the tool by
/// name triggered a silent "Tool 'list_mounts' not found in registry" at load time.
/// </summary>
public class AbstractionToolExtensionsTests
{
    [Fact]
    public void AddOrkeonAbstractionTools_RegistersListMountsToolAsIBaseTool()
    {
        var services = new ServiceCollection();

        services.AddOrkeonAbstractionTools();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IBaseTool) && d.ImplementationType == typeof(ListMountsTool));
    }

    [Fact]
    public void AddOrkeonAbstractionTools_ReturnsServiceCollectionForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddOrkeonAbstractionTools();

        Assert.Same(services, result);
    }
}
