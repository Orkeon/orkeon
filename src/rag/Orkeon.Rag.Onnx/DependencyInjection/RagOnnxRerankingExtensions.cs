using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Onnx.Reranking;
using Orkeon.Rag.Reranking;

namespace Orkeon.Rag.Onnx.DependencyInjection;

/// <summary>
/// Opt-in registration of the ONNX cross-encoder reranker. Contributes the
/// <c>onnx</c>/<c>cross-encoder</c> names to the <see cref="RerankerFactory"/>
/// built by <c>AddOrkeonRagReranking()</c> (called by <c>AddOrkeonRag</c>) —
/// call order between the two extensions is irrelevant.
/// </summary>
public static class RagOnnxRerankingExtensions
{
    /// <summary>
    /// Registers the singleton <see cref="OnnxCrossEncoderReranker"/> (one shared
    /// ONNX session) and its factory contribution under the names <c>onnx</c> and
    /// <c>cross-encoder</c>. The model itself is resolved lazily at first use —
    /// see <see cref="OnnxCrossEncoderReranker"/> for the resolution order.
    /// </summary>
    public static IServiceCollection AddOrkeonOnnxReranker(
        this IServiceCollection services,
        Action<OnnxRerankerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(sp => new OnnxCrossEncoderReranker(
            sp.GetRequiredService<IFileSystemService>(),
            sp.GetService<IOptions<OnnxRerankerOptions>>()?.Value,
            sp.GetService<ILogger<OnnxCrossEncoderReranker>>()));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRerankerRegistrar, OnnxRerankerRegistrar>());

        return services;
    }

    /// <summary>
    /// Registers <c>onnx</c>/<c>cross-encoder</c> on the factory, resolving the
    /// shared <see cref="OnnxCrossEncoderReranker"/> singleton.
    /// </summary>
    internal sealed class OnnxRerankerRegistrar : IRerankerRegistrar
    {
        /// <inheritdoc />
        public void Register(RerankerFactory factory, IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(serviceProvider);

            factory.Register(
                OnnxCrossEncoderReranker.RerankerName,
                serviceProvider.GetRequiredService<OnnxCrossEncoderReranker>,
                "cross-encoder");
        }
    }
}
