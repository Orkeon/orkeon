using Orkeon.Infrastructure.OutputParsing;

namespace Orkeon.Infrastructure.Tests.OutputParsing;

public class OutputParserFactoryTestsFixture
{
    private readonly OutputParserFactory _factory = new();

    public OutputParserFactoryTestsFixture()
    {
    }

    public OutputParserFactory GetFactory() => _factory;

}
