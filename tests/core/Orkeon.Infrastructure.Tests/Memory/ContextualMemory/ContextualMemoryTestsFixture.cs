using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Memory;

public class ContextualMemoryTestsFixture
{
    private readonly List<MemoryItem> _items = [];

    public ContextualMemoryTestsFixture()
    {
    }

    public ContextualMemoryTestsFixture WithItems(List<MemoryItem> value)
    {
        // Configure _items as needed
        return this;
    }

    public List<MemoryItem> GetItems() => _items;

}
