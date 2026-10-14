using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Telemetry;
using Orkeon.Constants.Llm;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Telemetry;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

/// <summary>CH-08 - the embedding port as registered opens one <c>embeddings {model}</c> span per call.</summary>
public sealed class TracedEmbeddingProviderTests
{
    /// <summary>The tag carrying the number of texts of one call (name fixed by this test, GREEN follows).</summary>
    private const string TextCountTag = "orkeon.embeddings.text_count";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldOpenEmbeddingsSpan_WhenPortEmbedsTexts()
    {
        var inner = new MockEmbeddingProvider { Name = "ch08-provider", Model = "ch08-model" };
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IEmbeddingProvider>(inner);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var container = services.BuildServiceProvider();
        var port = container.GetRequiredService<IEmbeddingProvider>();
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = new Activity("ch08-root");
        root.Start();

        var vectors = await port.GetEmbeddingsAsync(["alpha", "beta", "gamma"], Ct);
        root.Stop();

        Assert.Equal(3, vectors.Count);
        var span = Assert.Single(recorder.Snapshot(root.TraceId));
        Assert.Equal("embeddings ch08-model", span.OperationName);
        Assert.Equal(GenAiAttributes.OperationEmbeddings, span.GetTagItem(GenAiAttributes.OperationName));
        Assert.Equal("ch08-provider", span.GetTagItem(GenAiAttributes.ProviderName));
        Assert.Equal("ch08-model", span.GetTagItem(GenAiAttributes.RequestModel));
        Assert.Equal(3, span.GetTagItem(TextCountTag));
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldTraceCacheMissesOnly_WhenHostRegistersAnUntracedCache()
    {
        var inner = new MockEmbeddingProvider { Name = "ch08-cache-provider", Model = "ch08-cache-model" };
        var cache = new CachedEmbeddingProvider(inner, Options.Create(new EmbeddingCacheOptions()));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IEmbeddingProvider>(cache);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var container = services.BuildServiceProvider();
        var port = container.GetRequiredService<IEmbeddingProvider>();
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = new Activity("ch08-root-cache");
        root.Start();

        await port.GetEmbeddingAsync("same text", Ct);
        await port.GetEmbeddingAsync("same text", Ct);
        root.Stop();

        Assert.Equal(1, inner.GetEmbeddingCallCount);
        var span = Assert.Single(recorder.Snapshot(root.TraceId));
        Assert.Equal("embeddings ch08-cache-model", span.OperationName);
    }
}
