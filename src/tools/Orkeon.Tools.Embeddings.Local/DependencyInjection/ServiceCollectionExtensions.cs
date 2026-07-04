using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;

namespace Orkeon.Tools.Embeddings.Local.DependencyInjection;

/// <summary>
/// DI registration helpers for the on-device local embedding provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the on-device local embedding provider (BGE-micro-v2 ONNX, 384 dims, CPU)
    /// and its companion <see cref="LocalEmbedTool"/> agent tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers three services:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="LocalEmbeddingOptions"/> as a singleton (via <c>TryAddSingleton</c>) — either
    ///     the <paramref name="options"/> argument or a default instance.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="IEmbeddingProvider"/> bound to <see cref="LocalEmbeddingProvider"/> as a
    ///     singleton (via <c>TryAddSingleton</c>). The provider resolves an
    ///     <see cref="ILogger{TCategoryName}"/> and an <see cref="IFileSystemService"/> from the
    ///     container — both are optional. <see cref="IFileSystemService"/> is only required at
    ///     construction time when <see cref="LocalEmbeddingOptions.ModelPath"/> is non-empty;
    ///     otherwise the embedded BGE-micro-v2 model is used and no VFS resolution occurs.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="IBaseTool"/> as a singleton bound to <see cref="LocalEmbedTool"/>. This uses
    ///     <c>AddSingleton</c> (not <c>TryAddSingleton</c>) because multiple <see cref="IBaseTool"/>
    ///     implementations cohabit — the DI container stores them as a list. Calling this method
    ///     more than once will therefore register duplicate <see cref="LocalEmbedTool"/> entries;
    ///     prefer a single call per host.
    ///   </description></item>
    /// </list>
    /// <para>
    /// When <paramref name="options"/> is <see langword="null"/>, the defaults from
    /// <see cref="LocalEmbeddingOptions"/> apply: embedded model, <c>MaxConcurrency</c> equal to
    /// <see cref="Environment.ProcessorCount"/>, <c>MaxTextChars = 2000</c>.
    /// </para>
    /// <para>
    /// When <see cref="LocalEmbeddingOptions.ModelPath"/> is set but no
    /// <see cref="IFileSystemService"/> is registered in the host container, the provider's
    /// first resolution will throw <see cref="InvalidOperationException"/> (see
    /// <see cref="LocalEmbeddingProvider"/> for details).
    /// </para>
    /// <para>
    /// <b>Short-circuit with RaggableTree</b>: calling this method <i>before</i>
    /// <c>AddRaggableTree(...)</c> pre-registers <see cref="IEmbeddingProvider"/>, so the
    /// reflection-based fallback inside <c>RaggableTreeServiceCollectionExtensions</c> becomes a
    /// no-op (idempotent <c>TryAddSingleton</c>).
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="options">
    /// Optional configuration. When <see langword="null"/>, defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddOrkeonLocalEmbeddings(
        this IServiceCollection services,
        LocalEmbeddingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var opts = options ?? new LocalEmbeddingOptions();
        services.TryAddSingleton(opts);

        services.TryAddSingleton<IEmbeddingProvider>(sp =>
            new LocalEmbeddingProvider(
                sp.GetRequiredService<IFileSystemService>(),
                sp.GetRequiredService<LocalEmbeddingOptions>(),
                sp.GetService<ILogger<LocalEmbeddingProvider>>()));

        // Multiple IBaseTool implementations cohabit in the DI container (stored as a list),
        // so AddSingleton is intentional here — TryAddSingleton would silently drop the tool
        // when other IBaseTool registrations already exist.
        services.AddSingleton<IBaseTool, LocalEmbedTool>();

        return services;
    }
}
