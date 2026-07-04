using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Memory;

public class MemoryProviderBaseTestsFixture
{
    private readonly Dictionary<string, MemoryItem> _store = [];

    public MemoryProviderBaseTestsFixture()
    {
    }

    public Dictionary<string, MemoryItem> GetStore() => _store;

}
