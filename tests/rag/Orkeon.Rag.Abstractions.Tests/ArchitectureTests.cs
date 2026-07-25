using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Tests;

/// <summary>
/// Guards the ADR-006 dependency rule: <c>Orkeon.Rag.Abstractions</c> must
/// reference <c>Orkeon.Domain</c> and the BCL only — no Application, no
/// Infrastructure, no third-party packages.
/// </summary>
public class ArchitectureTests
{
    private static System.Reflection.AssemblyName[] GetReferencedAssemblies() =>
        typeof(RagAnswer).Assembly.GetReferencedAssemblies();

    [Fact]
    public void RagAbstractions_ShouldNotReference_ApplicationAssembly()
    {
        var referenced = GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, a => a.Name == "Orkeon.Application");
    }

    [Fact]
    public void RagAbstractions_ShouldNotReference_InfrastructureAssembly()
    {
        var referenced = GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, a => a.Name == "Orkeon.Infrastructure");
    }

    [Fact]
    public void RagAbstractions_ShouldOnlyReference_DomainAndBcl()
    {
        // The dependency rule is an ALLOWLIST: Orkeon.Domain and the BCL only.
        // Note: the compiler prunes unused references from assembly metadata, so
        // Orkeon.Domain only appears here once a contract actually uses a Domain
        // type — its absence is fine, anything outside the allowlist is not.
        var referenced = GetReferencedAssemblies();

        var offenders = referenced
            .Select(a => a.Name ?? string.Empty)
            .Where(name =>
                name != "Orkeon.Domain"
                && name != "System"
                && name != "mscorlib"
                && name != "netstandard"
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }
}
