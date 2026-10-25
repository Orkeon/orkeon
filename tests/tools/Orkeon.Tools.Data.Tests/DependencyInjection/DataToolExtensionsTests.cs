using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Tools.Abstractions.Data;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.Data.Graph;
using Orkeon.Tools.Data.MongoDB;
using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.Search;

namespace Orkeon.Tools.Data.Tests.DependencyInjection;

/// <summary>Tests for the registrations made by <see cref="DataToolExtensions"/>.</summary>
public class DataToolExtensionsTests
{
    private static readonly Type[] RelationalTools =
    [
        typeof(RelationalDatabaseTool), typeof(SqlServerDatabaseTool), typeof(PostgresDatabaseTool),
        typeof(MySqlDatabaseTool), typeof(MariaDbDatabaseTool), typeof(DatabaseSchemaTool),
    ];

    private static readonly Type[] MongoTools = [typeof(MongoDbTool), typeof(MongoDbSchemaTool)];

    private static readonly Type[] GraphTools = [typeof(ArcadeDbTool), typeof(JanusGraphTool), typeof(GraphSchemaTool)];

    private static readonly Type[] FileTools =
    [
        typeof(JsonTool), typeof(CsvReaderTool), typeof(PdfReaderTool), typeof(XmlParserTool),
        typeof(DocxReadTool), typeof(DocxWriteTool), typeof(XlsxReadTool), typeof(XlsxWriteTool),
        typeof(TxtSearchTool), typeof(MdxSearchTool), typeof(PdfSearchTool),
    ];

    private static List<Type?> ToolTypes(IServiceCollection services) =>
        [.. services.Where(d => d.ServiceType == typeof(IBaseTool)).Select(d => d.ImplementationType)];

    [Fact]
    public void ShouldRegisterEveryDataToolAsTransient_WhenAddingDataTools()
    {
        var services = new ServiceCollection();

        var returned = services.AddOrkeonDataTools();

        Assert.Same(services, returned);
        var tools = ToolTypes(services);
        Assert.Equal(FileTools.Concat(RelationalTools).Concat(MongoTools).Concat(GraphTools), tools);
        Assert.All(
            services.Where(d => d.ServiceType == typeof(IBaseTool)),
            d => Assert.Equal(ServiceLifetime.Transient, d.Lifetime));
        Assert.Contains(services, d => d.ServiceType == typeof(IEphemeralCollectionSearch));
        Assert.Contains(services, d => d.ServiceType == typeof(IDocumentLoader));
    }

    [Fact]
    public void ShouldRegisterRelationalSingletonsAndTools_WhenAddingRelationalTools()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddRelationalDatabaseTools());

        Assert.Equal(RelationalTools, ToolTypes(services));
        var factory = Assert.Single(services, d => d.ServiceType == typeof(IDatabaseProviderFactory));
        Assert.Equal(typeof(DatabaseProviderFactory), factory.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, factory.Lifetime);
        var policy = Assert.Single(services, d => d.ServiceType == typeof(IDatabaseSecurityPolicy));
        Assert.Equal(typeof(DefaultDatabaseSecurityPolicy), policy.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, policy.Lifetime);
    }

    [Fact]
    public void ShouldRegisterOnlyMongoTools_WhenAddingMongoTools()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddMongoDbTools());

        Assert.Equal(MongoTools, ToolTypes(services));
        Assert.Equal(2, services.Count);
    }

    [Fact]
    public void ShouldRegisterOnlyGraphTools_WhenAddingGraphTools()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddGraphDatabaseTools());

        Assert.Equal(GraphTools, ToolTypes(services));
        Assert.Equal(3, services.Count);
    }
}
