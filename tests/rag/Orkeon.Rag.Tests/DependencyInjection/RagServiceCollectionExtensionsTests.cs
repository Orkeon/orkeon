using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Rag.Validation;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.DependencyInjection;

/// <summary>
/// Tests for <see cref="RagServiceCollectionExtensions.AddOrkeonRagPipeline"/> —
/// the self-sufficient opt-in of the new RAG subsystem (TryAdd*, host wins).
/// </summary>
public class RagServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    private static ServiceProvider BuildProvider(
        IChatClient chatClient,
        Action<IServiceCollection>? hostRegistrations = null,
        IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<IDocumentStore>(new FakeDocumentStore());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton(chatClient);
        hostRegistrations?.Invoke(services);

        services.AddOrkeonRagPipeline(configuration ?? EmptyConfiguration());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddOrkeonRagPipeline_RegistersTheFourFileLoaders()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat);

        var loaders = provider.GetServices<IDocumentLoader>().ToList();

        Assert.Equal(4, loaders.Count);
        Assert.Contains(loaders, l => l is TextFileLoader);
        Assert.Contains(loaders, l => l is CsvDocumentLoader);
        Assert.Contains(loaders, l => l is HtmlDocumentLoader);
        Assert.Contains(loaders, l => l is PdfDocumentLoader);
    }

    [Fact]
    public void AddOrkeonRagPipeline_ResolvesPipelinesAndValidation()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat);

        Assert.IsType<DefaultIngestionPipeline>(provider.GetRequiredService<IIngestionPipeline>());
        Assert.IsType<LinearRagPipeline>(provider.GetRequiredService<IRagPipeline>());
        Assert.NotNull(provider.GetRequiredService<DataValidationPipeline>());
        Assert.NotNull(provider.GetRequiredService<DocumentLoaderFactory>());
        Assert.NotNull(provider.GetRequiredService<ChunkingStrategyFactory>());
        Assert.Equal(2, provider.GetServices<IDataValidator>().Count());
    }

    [Fact]
    public void AddOrkeonRagPipeline_IsIdempotent()
    {
        using var chat = new FakeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<IDocumentStore>(new FakeDocumentStore());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(chat);

        services.AddOrkeonRagPipeline(EmptyConfiguration());
        services.AddOrkeonRagPipeline(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        Assert.Equal(4, provider.GetServices<IDocumentLoader>().Count());
        Assert.Equal(2, provider.GetServices<IDataValidator>().Count());
    }

    [Fact]
    public void AddOrkeonRagPipeline_HostRegistrationWins()
    {
        var hostPipeline = new HostRagPipeline();
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat, services =>
            services.AddSingleton<IRagPipeline>(hostPipeline));

        Assert.Same(hostPipeline, provider.GetRequiredService<IRagPipeline>());
    }

    [Fact]
    public void AddOrkeonRagPipeline_BindsOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Rag:Ingestion:DefaultChunkingStrategy"] = "sentence",
                ["Orkeon:Rag:Pipeline:CandidateK"] = "13",
            })
            .Build();

        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat, configuration: configuration);

        var ingestion = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RagIngestionOptions>>().Value;
        var pipeline = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<LinearRagPipelineOptions>>().Value;

        Assert.Equal("sentence", ingestion.DefaultChunkingStrategy);
        Assert.Equal(13, pipeline.CandidateK);
    }

    private sealed class HostRagPipeline : IRagPipeline
    {
        public Task<Orkeon.Rag.Abstractions.Models.RagAnswer> QueryAsync(
            Orkeon.Rag.Abstractions.Models.RagQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Orkeon.Rag.Abstractions.Models.RagAnswer { Text = "host" });
    }
}
