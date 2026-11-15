using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Gives a command host its own writable default for <c>Orkeon:Embeddings:FingerprintPath</c>.</summary>
internal static class EmbeddingFingerprintDefault
{
    /// <summary>Applies <paramref name="virtualPath"/> as the lowest-precedence default.</summary>
    public static void Apply(IHostBuilder builder, string virtualPath) =>
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.Sources.Insert(0, new MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?>
                {
                    ["Orkeon:Embeddings:FingerprintPath"] = virtualPath,
                },
            }));
}
