using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>
/// The local embedding model (BGE-micro-v2, English) out of a minimal container (STUDIO-38 D-04).
/// <para>
/// Not the runner host: <c>RunnerHost.Build</c> reads a settings file, warns when it has no
/// <c>Llm</c> section and wires RaggableTree — none of which a search of the catalogue needs, and
/// all of which it would pay for at every start. The container holds the one thing
/// <c>AddOrkeonLocalEmbeddings</c> resolves besides the model: an <see cref="IFileSystemService"/>.
/// The bundled model needs no path resolution, so it is the tightest one there is — no mount at
/// all: the search reads no file through it, and nothing could.
/// </para>
/// </summary>
internal static class UseCaseLocalModel
{
    /// <summary>Where the SmartComponents package puts the model, next to the binary.</summary>
    public const string ModelDirectory = "LocalEmbeddingsModel/default";

    private static readonly string[] ModelFiles = ["model.onnx", "vocab.txt"];

    /// <summary>
    /// Loads the model; throws <see cref="UseCaseModelUnavailableException"/> when its files are
    /// not installed. The provider returned is disposable and owns its container.
    /// </summary>
    public static IEmbeddingProvider Load()
    {
        foreach (var file in ModelFiles)
        {
            // OUT-OF-SCOPE: probing the tool's own install directory for the files the
            // SmartComponents package copies next to the binary, as `orkeon doctor` does.
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "LocalEmbeddingsModel", "default", file)))
            {
                throw new UseCaseModelUnavailableException(
                    $"the local embedding model is not installed ({ModelDirectory}/{file} is missing next to the orkeon binary)");
            }
        }

        var services = new ServiceCollection();
        services.AddSingleton(_ => new FileSystemRegistry([]));
        services.AddSingleton<IPathValidator>(_ =>
            new PathValidator(Options.Create(new PathSecurityOptions()), NullLogger<PathValidator>.Instance));
        services.AddSingleton<IFileSystemService>(provider => new FileSystemService(
            provider.GetRequiredService<FileSystemRegistry>(),
            provider.GetRequiredService<IPathValidator>(),
            NullLogger<FileSystemService>.Instance));
        services.AddOrkeonLocalEmbeddings();

        var container = services.BuildServiceProvider();
        try
        {
            return new ContainerOwnedModel(container, container.GetRequiredService<IEmbeddingProvider>());
        }
        catch
        {
            container.Dispose();
            throw;
        }
    }

    /// <summary>The model, and the container whose disposal releases its native session.</summary>
    private sealed class ContainerOwnedModel(ServiceProvider container, IEmbeddingProvider model)
        : IEmbeddingProvider, IDisposable
    {
        public int Dimensions => model.Dimensions;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            model.EmbedBatchAsync(texts, ct);

        public void Dispose() => container.Dispose();
    }
}
