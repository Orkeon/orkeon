using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Trading.Tools.Infrastructure.DependencyInjection;
using Orkeon.Trading.Tools.Infrastructure.Execution;

namespace Orkeon.Examples.Runners.Tests;

/// <summary>
/// Non-regression coverage for the trading-tools DI wiring.
///
/// Root cause guarded here: the tools were previously registered as
/// <c>AddSingleton&lt;ITool, X&gt;()</c>, but <c>ServiceProviderToolRegistry</c>
/// (the production consumer, in <c>Orkeon.Hosting</c>) is constructed from an
/// <see cref="IEnumerable{IBaseTool}"/>. MS DI resolves services by their exact
/// registered service type and does not upcast an <c>ITool</c> registration to
/// its <c>IBaseTool</c> base, so <c>GetServices&lt;IBaseTool&gt;()</c> returned
/// zero tools and the registry was empty. Registrations now use
/// <c>AddSingleton&lt;IBaseTool, X&gt;()</c>; these tests fail if that ever
/// regresses.
/// </summary>
public class TradingToolsDependencyInjectionTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTradingTools();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddTradingTools_resolves_44_tools_as_IBaseTool()
    {
        // This is the exact resolution that was broken: ServiceProviderToolRegistry
        // depends on IEnumerable<IBaseTool>. An ITool-only registration yields 0 here.
        var sp = BuildProvider();

        var tools = sp.GetServices<IBaseTool>().ToList();

        Assert.Equal(44, tools.Count);
    }

    [Fact]
    public async Task Registered_tools_flow_through_ServiceProviderToolRegistry()
    {
        var sp = BuildProvider();
        var tools = sp.GetServices<IBaseTool>();

        // Mirrors CrewFactory wiring: the registry is built from the DI tool set.
        var registry = new ServiceProviderToolRegistry(tools);

        var all = await registry.GetAllToolsAsync();
        Assert.Equal(44, all.Count);
    }

    [Fact]
    public async Task Every_tool_name_is_resolvable_from_the_registry()
    {
        var sp = BuildProvider();
        var tools = sp.GetServices<IBaseTool>().ToList();
        var registry = new ServiceProviderToolRegistry(tools);

        var unresolved = new List<string>();
        foreach (var tool in tools)
        {
            var registered = await registry.IsRegisteredAsync(tool.Name);
            var byName = await registry.GetToolByNameAsync(tool.Name);
            if (!registered || byName is null)
                unresolved.Add($"{tool.GetType().Name} (name '{tool.Name}')");
        }

        Assert.True(unresolved.Count == 0,
            "Tools not resolvable by name from the registry:\n" + string.Join("\n", unresolved));
    }

    [Fact]
    public async Task Key_trading_tool_names_are_registered()
    {
        var sp = BuildProvider();
        var registry = new ServiceProviderToolRegistry(sp.GetServices<IBaseTool>());

        // Spot-check one representative tool name per functional area. These names
        // come from the YAML definitions, so this also guards the yaml->Name binding.
        string[] expectedNames =
        [
            "twap_execution",        // Execution
            "vwap_execution",        // Execution
            "correlation_analysis",  // Analysis
            "technical_indicators",  // Analysis
            "var_calculation",       // Risk
            "cvar_calculation",      // Risk
            "compliance_check",      // Governance
            "mean_variance_optimization", // Portfolio
            "backtesting",           // Prediction
            "historical_data_fetch", // Data
        ];

        var missing = new List<string>();
        foreach (var name in expectedNames)
        {
            if (!await registry.IsRegisteredAsync(name))
                missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            "Expected tool names not registered: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_concrete_tool_type_in_the_assembly_is_registered()
    {
        var sp = BuildProvider();
        var registeredTypes = sp.GetServices<IBaseTool>()
            .Select(t => t.GetType())
            .ToHashSet();

        // Reflect over the Trading.Tools assembly for every concrete IBaseTool
        // implementation. If someone adds a new tool class but forgets the
        // AddSingleton line, it will be present here but absent from DI.
        var toolInterface = typeof(IBaseTool);
        var concreteToolTypes = typeof(TWAPExecutionTool).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && !t.IsGenericTypeDefinition
                        && toolInterface.IsAssignableFrom(t))
            .ToList();

        var unregistered = concreteToolTypes
            .Where(t => !registeredTypes.Contains(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unregistered.Count == 0,
            "Concrete tool classes present in the assembly but not registered by AddTradingTools():\n"
            + string.Join("\n", unregistered));
    }

    [Fact]
    public void ITool_registration_would_not_satisfy_the_registry()
    {
        // Encodes the DI behavior that caused the original bug: an ITool-only
        // registration is NOT resolvable as IBaseTool (no service-type upcast),
        // which is precisely why AddTradingTools must register IBaseTool.
        var services = new ServiceCollection();
        services.AddSingleton<ITool, TWAPExecutionTool>();
        using var sp = services.BuildServiceProvider();

        Assert.NotEmpty(sp.GetServices<ITool>());
        Assert.Empty(sp.GetServices<IBaseTool>());
    }
}
