using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class DocumentLoaderFactoryTestsFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly HttpClient _webHttpClient;
    private readonly DocumentLoaderFactory _factory;

    public DocumentLoaderFactoryTestsFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"doc-factory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var fs = new MockFileSystemService(_tempDir);
        _webHttpClient = new HttpClient();

        var loaders = new IDocumentLoader[]
        {
            new TextFileLoader(fs),
            new CsvDocumentLoader(fs),
            new HtmlDocumentLoader(fs),
            new WebPageLoader(_webHttpClient)
        };

        _factory = new DocumentLoaderFactory(loaders);
    }

    public IDocumentLoader GetLoader(string type)
        => _factory.GetLoader(type);

    public IDocumentLoader? GetLoaderForSource(string source)
        => _factory.GetLoaderForSource(source);

    public IEnumerable<string> GetSupportedTypes()
        => _factory.SupportedTypes;

    public DocumentLoaderFactory GetFactory() => _factory;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _webHttpClient.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
