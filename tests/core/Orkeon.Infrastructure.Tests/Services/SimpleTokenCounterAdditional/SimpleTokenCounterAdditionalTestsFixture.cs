using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Infrastructure.Tests.Services;

public class SimpleTokenCounterAdditionalTestsFixture
{
    private readonly SimpleTokenCounter _counter = new();

    public SimpleTokenCounterAdditionalTestsFixture()
    {
    }

    public SimpleTokenCounter GetCounter() => _counter;

}
