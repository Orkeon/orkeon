using System.Reflection;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// Guards the <c>Orkeon.Tools.Rag</c> layering: the assembly loads and follows the
/// Tools.* rule — tools sit on <c>Orkeon.Rag.Abstractions</c> / <c>Orkeon.Domain</c>,
/// never on Application or Infrastructure.
/// </summary>
public class ArchitectureTests
{
    private static Assembly LoadToolsRagAssembly() => typeof(RagSearchTool).Assembly;

    [Fact]
    public void ToolsRagAssembly_Loads_WithExpectedName()
    {
        var assembly = LoadToolsRagAssembly();

        Assert.Equal("Orkeon.Tools.Rag", assembly.GetName().Name);
    }

    [Fact]
    public void ToolsRag_ShouldNotReference_ApplicationAssembly()
    {
        var referenced = LoadToolsRagAssembly().GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, a => a.Name == "Orkeon.Application");
    }

    [Fact]
    public void ToolsRag_ShouldNotReference_InfrastructureAssembly()
    {
        var referenced = LoadToolsRagAssembly().GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, a => a.Name == "Orkeon.Infrastructure");
    }
}
