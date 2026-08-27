using System.Reflection;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Guards the layering rule for <c>Orkeon.Tools.FileSystem</c>: Domain-side assemblies only,
/// never <c>Orkeon.Application</c> or <c>Orkeon.Infrastructure</c>.
/// <para>
/// Written as an ALLOWLIST, because that is what the rule is. It used to be a denylist of two
/// names under a doc claiming "only Domain and Tools.Abstractions" — a claim already false
/// (this project also references the Analysis and RAG abstractions and the RAG
/// implementation), and a shape that says nothing about the next reference someone adds. The
/// sibling suite in <c>Orkeon.Rag.Abstractions.Tests</c> has the right form.
/// </para>
/// </summary>
public class ArchitectureTests
{
    /// <summary>
    /// The allowlist. Adding a reference outside it is a layering decision, so it should cost
    /// a deliberate edit here rather than pass silently.
    /// </summary>
    private static readonly string[] AllowedOrkeonReferences =
    [
        "Orkeon.Domain",
        "Orkeon.Tools.Abstractions",
        "Orkeon.Analysis.Abstractions",
        "Orkeon.Rag.Abstractions",
        "Orkeon.Rag",
        "Orkeon.Compliance.Vfs",
    ];

    [Fact]
    public void ToolsFileSystem_ReferencesNothingOutsideTheAllowlist()
    {
        var referenced = typeof(DirectorySearchTool).Assembly.GetReferencedAssemblies();

        var offenders = referenced
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Orkeon.", StringComparison.Ordinal))
            .Where(name => !AllowedOrkeonReferences.Contains(name, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Orkeon.Tools.FileSystem references " + string.Join(", ", offenders)
            + " — outside the layering allowlist. Add it deliberately, or route through an abstraction.");
    }

    /// <summary>
    /// The two the allowlist exists to keep out, named explicitly: the compiler prunes unused
    /// references from metadata, so an allowlist alone would pass for the wrong reason if this
    /// project ever referenced Application without using a type from it.
    /// </summary>
    [Theory]
    [InlineData("Orkeon.Application")]
    [InlineData("Orkeon.Infrastructure")]
    public void ToolsFileSystem_ShouldNotReference(string forbidden)
    {
        var referenced = typeof(DirectorySearchTool).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, a => a.Name == forbidden);
    }

    [Fact]
    public void ToolsFileSystem_ShouldReference_DomainAssembly()
    {
        // Arrange
        var assembly = typeof(DirectorySearchTool).Assembly;

        // Act
        var referencedAssemblies = assembly.GetReferencedAssemblies();

        // Assert — Tools.FileSystem MUST depend on Orkeon.Domain
        Assert.Contains(
            referencedAssemblies,
            a => a.Name == "Orkeon.Domain");
    }

    [Fact]
    public void ToolsFileSystem_ShouldReference_ToolsAbstractionsAssembly()
    {
        // Arrange
        var assembly = typeof(DirectorySearchTool).Assembly;

        // Act
        var referencedAssemblies = assembly.GetReferencedAssemblies();

        // Assert — Tools.FileSystem MUST depend on Orkeon.Tools.Abstractions
        Assert.Contains(
            referencedAssemblies,
            a => a.Name == "Orkeon.Tools.Abstractions");
    }

    [Fact]
    public void EmbeddingVector_ShouldBeDefined_InDomainMemory()
    {
        // Arrange — EmbeddingVector should live in Domain, not Application
        var domainAssembly = typeof(Orkeon.Domain.Memory.IEmbeddingService).Assembly;

        // Act
        var type = domainAssembly.GetType("Orkeon.Domain.Memory.EmbeddingVector");

        // Assert
        Assert.NotNull(type);
        Assert.True(type.IsClass); // record is a class
    }

    [Fact]
    public void DirectorySearchTool_CanBeCreated_WithEphemeralCollectionSearch()
    {
        // Arrange — since RAG-03/C5 the façade takes the shared ephemeral-collection
        // search contract (Orkeon.Rag.Abstractions, itself Domain-only per ADR-006),
        // still not requiring any Application type.
        var constructors = typeof(DirectorySearchTool).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        // Act
        var ctor = constructors.FirstOrDefault(c =>
        {
            var parameters = c.GetParameters();
            return parameters.Any(p =>
                p.ParameterType == typeof(Orkeon.Rag.Abstractions.Interfaces.IEphemeralCollectionSearch));
        });

        // Assert
        Assert.NotNull(ctor);
    }
}
