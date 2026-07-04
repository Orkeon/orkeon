using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

public class YamlCrewDefinitionTestsFixture
{
    private readonly MockYamlSerializer _yamlSerializer;
    private readonly MockToolRegistry _toolRegistry;
    private readonly ILogger<YamlCrewDefinitionLoader> _loaderLogger;
    private readonly ILogger<CrewFactory> _factoryLogger;
    private readonly ILogger<YamlCrewExporter> _exporterLogger;
    private readonly YamlCrewDefinitionLoader _loader;

    public YamlCrewDefinitionTestsFixture()
    {
        _yamlSerializer = new MockYamlSerializer();
        _toolRegistry = new MockToolRegistry();
        _loaderLogger = NullLogger<YamlCrewDefinitionLoader>.Instance;
        _factoryLogger = NullLogger<CrewFactory>.Instance;
        _exporterLogger = NullLogger<YamlCrewExporter>.Instance;
        _loader = new YamlCrewDefinitionLoader(_yamlSerializer, new FakeFileSystemService(), _loaderLogger);
    }

    public YamlCrewDefinitionTestsFixture WithYamlSerializer(MockYamlSerializer value)
    {
        // Configure _yamlSerializer as needed
        return this;
    }

    public YamlCrewDefinitionTestsFixture WithToolRegistry(MockToolRegistry value)
    {
        // Configure _toolRegistry as needed
        return this;
    }

    public YamlCrewDefinitionTestsFixture WithLoaderLogger(ILogger<YamlCrewDefinitionLoader> value)
    {
        // Configure _loaderLogger as needed
        return this;
    }

    public YamlCrewDefinitionTestsFixture WithFactoryLogger(ILogger<CrewFactory> value)
    {
        // Configure _factoryLogger as needed
        return this;
    }

    public YamlCrewDefinitionTestsFixture WithExporterLogger(ILogger<YamlCrewExporter> value)
    {
        // Configure _exporterLogger as needed
        return this;
    }

    public MockYamlSerializer GetYamlSerializer() => _yamlSerializer;
    public MockToolRegistry GetToolRegistry() => _toolRegistry;
    public ILogger<YamlCrewDefinitionLoader> GetLoaderLogger() => _loaderLogger;
    public ILogger<CrewFactory> GetFactoryLogger() => _factoryLogger;
    public ILogger<YamlCrewExporter> GetExporterLogger() => _exporterLogger;
    public YamlCrewDefinitionLoader GetLoader() => _loader;

}
