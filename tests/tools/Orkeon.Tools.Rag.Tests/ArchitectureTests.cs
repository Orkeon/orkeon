using System.Reflection;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// Guards the skeleton <c>Orkeon.Tools.Rag</c> project (RAG-02 C1): the assembly
/// exists, loads, and follows the Tools.* layering (no Application reference).
/// The project intentionally contains no tool classes yet, so the assembly is
/// resolved by name instead of via a type anchor.
/// </summary>
public class ArchitectureTests
{
    private static Assembly LoadToolsRagAssembly() =>
        Assembly.Load(new AssemblyName("Orkeon.Tools.Rag"));

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

    [Fact]
    public void ToolsRag_IsStillASkeleton_WithNoPublicTools()
    {
        // This test documents the C1 state on purpose: the rag_* tools migrate
        // here in a later batch. When they land, replace this assertion with
        // real tool coverage.
        var assembly = LoadToolsRagAssembly();

        Assert.Empty(assembly.GetExportedTypes());
    }
}
