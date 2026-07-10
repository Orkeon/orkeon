using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Trading.Tools.Infrastructure.DependencyInjection;

namespace Orkeon.Examples.Runners.Tests;

/// <summary>
/// Verifies that AddTradingTools() registers exactly 44 tools and that each one
/// has its Name, Description, and Schema loaded successfully from its YAML
/// definition (catches missing yaml files, malformed yaml, or missing
/// CopyToOutputDirectory wiring).
///
/// Tools are resolved as <see cref="IBaseTool"/> (the service type they are
/// registered under and the type <c>ServiceProviderToolRegistry</c> consumes),
/// not as <c>ITool</c>: MS DI resolves by the exact registered service type and
/// does not upcast a registration to a base interface, so resolving <c>ITool</c>
/// would yield zero services even though every tool implements it.
/// </summary>
public class AllTradingToolsLoadTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTradingTools();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddTradingTools_registers_exactly_44_tools()
    {
        var sp = BuildProvider();
        var tools = sp.GetServices<IBaseTool>().ToList();
        Assert.Equal(44, tools.Count);
    }

    [Fact]
    public void All_44_tools_load_their_yaml_metadata()
    {
        var sp = BuildProvider();
        var tools = sp.GetServices<IBaseTool>().ToList();

        var failures = new List<string>();
        foreach (var tool in tools)
        {
            try
            {
                _ = tool.Name;
                _ = tool.Description;
                _ = tool.Schema;

                if (string.IsNullOrWhiteSpace(tool.Name))
                    failures.Add($"{tool.GetType().Name}: empty Name");
                if (string.IsNullOrWhiteSpace(tool.Description))
                    failures.Add($"{tool.GetType().Name}: empty Description");
            }
            catch (Exception ex)
            {
                failures.Add($"{tool.GetType().Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "Tools failing metadata load:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void All_44_tools_have_distinct_names()
    {
        var sp = BuildProvider();
        var names = sp.GetServices<IBaseTool>().Select(t => t.Name).ToList();
        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0,
            "Duplicate tool names: " + string.Join(", ", duplicates));
    }
}
