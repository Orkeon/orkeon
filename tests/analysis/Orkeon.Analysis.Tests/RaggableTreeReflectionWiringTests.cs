using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// White-box tests for <c>TryCreateLocalProvider</c>, the reflection helper in
/// <see cref="RaggableTreeServiceCollectionExtensions"/> that wires the optional
/// local-embeddings package without a hard reference. Validates its 3 guarantees:
/// <list type="number">
/// <item>Assembly loaded → provider resolves to <c>LocalEmbeddingProvider</c>.</item>
/// <item>Assembly absent → <c>InvalidOperationException</c> with actionable message
///   (asserted via "pin contract wording" on the source — see test #2 inline notes).</item>
/// <item>Short-circuit (AddOrkeonLocalEmbeddings before AddRaggableTree) → exactly 1 provider registered.</item>
/// </list>
/// </summary>
/// <remarks>
/// These tests reference <see cref="Orkeon.Tools.Embeddings.Local.LocalEmbeddingProvider"/>
/// transitively (the test project takes a ProjectReference on the local-embeddings
/// package). The "assembly absent" scenario therefore cannot be exercised at runtime
/// without <see cref="System.Runtime.Loader.AssemblyLoadContext"/> isolation — see test #2.
/// </remarks>
[Trait("Category", "Slow")]
public sealed class RaggableTreeReflectionWiringTests
{
    [Fact]
    public void LocalSmartComponents_WithAssemblyLoaded_ResolvesProvider()
    {
        // Arrange — a minimal IFileSystemService is required by AddRaggableTree.
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProviderKind.LocalSmartComponents,
            },
        });

        // Act — the assembly Orkeon.Tools.Embeddings.Local IS loaded (referenced by this test project),
        // so reflection should find LocalEmbeddingProvider and resolve it.
        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IEmbeddingProvider>();

        // Assert — provider is the local one (FullName check covers cross-package identity).
        Assert.Equal(
            "Orkeon.Tools.Embeddings.Local.LocalEmbeddingProvider",
            provider.GetType().FullName);
    }

    [Fact]
    public void LocalSmartComponents_WithoutAssembly_ThrowsHelpfulException()
    {
        // Strategy: we cannot deroute Type.GetType("…, Orkeon.Tools.Embeddings.Local")
        // from this test project, which references Orkeon.Tools.Embeddings.Local. Without
        // AssemblyLoadContext-isolation the factory can never return null here. The
        // pragmatic alternative is to "pin the contract wording" on the source itself,
        // catching any drift in that actionable error message.
        //
        // TODO — replace this source-level pin with a real runtime test: load
        // Orkeon.Analysis into a collectible AssemblyLoadContext that does NOT resolve
        // Orkeon.Tools.Embeddings.Local, so Type.GetType(...) actually returns null,
        // then assert the thrown InvalidOperationException message instead of the source
        // text. That removes the dependency on the bin/ -> src/ relative path below.
        var sourcePath = Path.Combine(
            // From bin/Debug/net10.0/, climb to the repo root then point the source file.
            // ATTENTION: assumes the binary runs from bin/Debug/netX.0/. Adjust if the
            // build layout changes (e.g. nested test output).
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
            "src", "analysis", "Orkeon.Analysis", "DependencyInjection",
            "RaggableTreeServiceCollectionExtensions.cs");

        var source = File.ReadAllText(Path.GetFullPath(sourcePath));

        // The contractual error message must contain all four fragments.
        // The LocalSmartComponents error wording must point at the missing package.
        Assert.Contains("requires the package", source);
        // The wording must name the opt-in package the user needs to reference.
        Assert.Contains("Orkeon.Tools.Embeddings.Local", source);
        // The wording must suggest both csproj-side ways to add the dep.
        Assert.Contains("PackageReference / ProjectReference", source);
        // The wording must suggest the DI short-circuit alternative.
        Assert.Contains("AddOrkeonLocalEmbeddings", source);
    }

    [Fact]
    public void LocalSmartComponents_ShortCircuit_NoDoubleInstantiation()
    {
        // Calling AddOrkeonLocalEmbeddings() BEFORE AddRaggableTree(...) pre-registers
        // IEmbeddingProvider; AddRaggableTree's TryAddSingleton then becomes a no-op.
        // No reflection is exercised, no double instantiation.
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddOrkeonLocalEmbeddings();
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProviderKind.LocalSmartComponents,
            },
        });

        using var sp = services.BuildServiceProvider();

        // GetServices<IEmbeddingProvider>() returns the registration list. With TryAddSingleton
        // applied twice on the same service type, only one descriptor remains.
        var providers = sp.GetServices<IEmbeddingProvider>().ToList();
        Assert.Single(providers);
        Assert.Equal(
            "Orkeon.Tools.Embeddings.Local.LocalEmbeddingProvider",
            providers[0].GetType().FullName);
    }
}
