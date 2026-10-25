using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Onnx.DependencyInjection;
using Orkeon.Rag.Onnx.Reranking;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Onnx.Tests;

/// <summary>
/// Registration of the ONNX reranker. The reranker is only resolved, never asked to rerank, so no
/// ONNX session is created (that path is in <see cref="OnnxCrossEncoderRerankerSlowTests"/>).
/// </summary>
public class RagOnnxRerankingExtensionsTests
{
    [Fact]
    public void ShouldRefuseANullServiceCollection()
    {
        Assert.Throws<ArgumentNullException>(() => RagOnnxRerankingExtensions.AddOrkeonOnnxReranker(null!));
    }

    [Fact]
    public void ShouldApplyTheConfigureCallback_WhenOneIsGiven()
    {
        var services = new ServiceCollection();
        OnnxRerankerOptions? seen = null;
        services.AddOrkeonOnnxReranker(options => seen = options);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OnnxRerankerOptions>>().Value;

        Assert.Same(options, seen);
        Assert.Equal(16, options.BatchSize);
    }

    [Fact]
    public void ShouldRegisterBothNamesOnOneSharedReranker_WhenTheFactoryIsBuilt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new ThrowingFileSystemService());
        services.AddOrkeonOnnxReranker();
        services.AddOrkeonRagReranking();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<RerankerFactory>();

        Assert.True(factory.IsKnown("onnx"));
        Assert.True(factory.IsKnown("cross-encoder"));
        var reranker = Assert.IsType<OnnxCrossEncoderReranker>(factory.Create("onnx"));
        Assert.Same(reranker, factory.Create("cross-encoder"));
        Assert.Same(reranker, provider.GetRequiredService<OnnxCrossEncoderReranker>());
    }

    [Fact]
    public void ShouldRegisterTheRegistrarOnce_WhenCalledTwice()
    {
        var services = new ServiceCollection();
        services.AddOrkeonOnnxReranker();
        services.AddOrkeonOnnxReranker();

        Assert.Single(services, d => d.ServiceType == typeof(OnnxCrossEncoderReranker));
        Assert.Single(services, d => d.ImplementationType == typeof(RagOnnxRerankingExtensions.OnnxRerankerRegistrar));
    }

    [Fact]
    public void ShouldRefuseNullArguments_InTheRegistrar()
    {
        var registrar = new RagOnnxRerankingExtensions.OnnxRerankerRegistrar();
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentNullException>(() => registrar.Register(null!, provider));
        Assert.Throws<ArgumentNullException>(() => registrar.Register(new RerankerFactory(), null!));
    }
}
