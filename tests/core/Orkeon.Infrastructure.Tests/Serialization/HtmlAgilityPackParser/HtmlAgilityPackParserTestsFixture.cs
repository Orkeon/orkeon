using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

public class HtmlAgilityPackParserTestsFixture
{
    private readonly HtmlAgilityPackParser _parser;

    public HtmlAgilityPackParserTestsFixture()
    {
        _parser = new HtmlAgilityPackParser();
    }

    public HtmlAgilityPackParser GetParser() => _parser;

}
