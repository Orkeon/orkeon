using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Common;
using Orkeon.Trading.Tools.Infrastructure.DependencyInjection;

namespace Orkeon.Examples.Runners.Tests;

/// <summary>
/// Verifies that AddTradingTools() registers exactly 44 tools and that each one
/// has its Name, Description, and Schema loaded successfully from its YAML
/// definition (catches missing yaml files, malformed yaml, or missing
/// CopyToOutputDirectory wiring).
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
        var tools = sp.GetServices<ITool>().ToList();
        Assert.Equal(44, tools.Count);
    }

    [Fact]
    public void All_44_tools_load_their_yaml_metadata()
    {
        var sp = BuildProvider();
        var tools = sp.GetServices<ITool>().ToList();

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
        var names = sp.GetServices<ITool>().Select(t => t.Name).ToList();
        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0,
            "Duplicate tool names: " + string.Join(", ", duplicates));
    }
}
