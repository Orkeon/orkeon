using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

public class YamlDotNetSerializerTestsFixture
{
    private readonly YamlDotNetSerializer _serializer;

    public YamlDotNetSerializerTestsFixture()
    {
        _serializer = new YamlDotNetSerializer();
    }

    public YamlDotNetSerializer GetSerializer() => _serializer;

}
