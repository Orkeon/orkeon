using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

public class CsvHelperSerializerTestsFixture
{
    private readonly CsvHelperSerializer _serializer;

    public CsvHelperSerializerTestsFixture()
    {
        _serializer = new CsvHelperSerializer();
    }

    public CsvHelperSerializer GetSerializer() => _serializer;

}
