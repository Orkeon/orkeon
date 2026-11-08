using Orkeon.Infrastructure.LLMs.Embeddings;

namespace Orkeon.ConsoleApp;

/// <summary>
/// The REPL builds its host and never starts it, so the hosted service of the embedding model guard
/// (FWK-25) never fires: the REPL calls this once, before anything else runs.
/// </summary>
internal static class ReplEmbeddingGuard
{
    /// <summary>The exit code of a refused start.</summary>
    public const int RefusedExitCode = 1;

    /// <summary>
    /// Checks the embedding model against the recorded fingerprint. A change is said on
    /// <paramref name="stderr"/> and answered with <see cref="RefusedExitCode"/>; otherwise null.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="stderr">Where the refusal is written.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<int?> CheckAsync(IServiceProvider services, TextWriter stderr, CancellationToken cancellationToken)
    {
        if (await EmbeddingModelGuard.CheckAsync(services, cancellationToken).ConfigureAwait(false) is not { } refusal)
            return null;

        await stderr.WriteLineAsync("ERROR: " + refusal).ConfigureAwait(false);
        return RefusedExitCode;
    }
}
