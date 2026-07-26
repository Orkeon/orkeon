using System.Reflection;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Tests that verify Tools.FileSystem follows Clean Architecture layering:
/// it should only depend on Domain and Tools.Abstractions, NOT on Application.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void ToolsFileSystem_ShouldNotReference_ApplicationAssembly()
    {
        // Arrange
        var assembly = typeof(DirectorySearchTool).Assembly;

        // Act
        var referencedAssemblies = assembly.GetReferencedAssemblies();

        // Assert — Tools.FileSystem must NOT depend on Orkeon.Application
        Assert.DoesNotContain(
            referencedAssemblies,
            a => a.Name == "Orkeon.Application");
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
