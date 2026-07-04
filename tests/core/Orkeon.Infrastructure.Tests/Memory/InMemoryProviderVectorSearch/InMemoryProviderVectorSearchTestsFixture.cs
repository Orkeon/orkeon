using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.Memory;

public class InMemoryProviderVectorSearchTestsFixture
{
    private readonly InMemoryProvider _provider;

    public InMemoryProviderVectorSearchTestsFixture()
    {
        _provider = new InMemoryProvider(new TestLogger<InMemoryProvider>());
    }

    public InMemoryProvider GetProvider() => _provider;

}
