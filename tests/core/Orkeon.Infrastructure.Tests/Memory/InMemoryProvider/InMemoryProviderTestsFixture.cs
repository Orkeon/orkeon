using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.Memory;

public class InMemoryProviderTestsFixture
{
    private readonly InMemoryProvider _provider;
    private readonly TestLogger<InMemoryProvider> _logger;

    public InMemoryProviderTestsFixture()
    {
        _logger = new TestLogger<InMemoryProvider>();
        _provider = new InMemoryProvider(_logger);
    }

    public InMemoryProviderTestsFixture WithLogger(TestLogger<InMemoryProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public InMemoryProvider GetProvider() => _provider;
    public TestLogger<InMemoryProvider> GetLogger() => _logger;

}
