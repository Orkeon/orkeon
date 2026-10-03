using Orkeon.Rag.Factories;
using Orkeon.Rag.Reranking;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IRerankerRegistrar"/> double: offers the <c>onnx</c> name, as
/// <c>AddOrkeonOnnxReranker()</c> does, without the model — for a container whose profile reranks with
/// it (<c>balanced</c>, <c>quality</c>), which a host refuses at its start when no registrar offers it.
/// </summary>
public sealed class StubOnnxRerankerRegistrar : IRerankerRegistrar
{
    /// <inheritdoc />
    public void Register(RerankerFactory factory, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(factory);
        factory.Register("onnx", static () => new NoopReranker(), "cross-encoder");
    }
}
