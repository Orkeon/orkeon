using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Serialization
{
    /// <summary>
    /// YamlDotNet implementation of IYamlSerializer.
    ///
    /// Serialization is camelCase (canonical form). Deserialization tolerates both camelCase
    /// (canonical) and snake_case (Python Orkeon parity) keys, and ignores unknown properties
    /// so that forward-compatible YAML extensions do not crash the loader.
    /// </summary>
    public class YamlDotNetSerializer : IYamlSerializer
    {
        private readonly IDeserializer _deserializer;
        private readonly ISerializer _serializer;

        /// <summary>Initializes a new instance of <see cref="YamlDotNetSerializer"/> with camelCase naming, snake_case fallback, and forgiving deserialization.</summary>
        public YamlDotNetSerializer() : this(NullLogger.Instance) { }

        /// <summary>Initializes a new instance with an injected logger for debug telemetry on snake_case fallbacks.</summary>
        /// <param name="logger">Logger used to record snake_case → camelCase fallbacks at Debug level.</param>
        public YamlDotNetSerializer(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);

            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeInspector(inner => new CamelOrSnakeCaseTypeInspector(inner, logger))
                .IgnoreUnmatchedProperties()
                .Build();

            _serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        /// <inheritdoc />
        public T Deserialize<T>(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml))
            {
                throw new ArgumentException("YAML content cannot be null or empty", nameof(yaml));
            }

            return _deserializer.Deserialize<T>(yaml);
        }

        /// <inheritdoc />
        public object Deserialize(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml))
            {
                throw new ArgumentException("YAML content cannot be null or empty", nameof(yaml));
            }

            return _deserializer.Deserialize(yaml) ?? throw new InvalidOperationException("Deserialization returned null");
        }

        /// <inheritdoc />
        public string Serialize<T>(T obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            return _serializer.Serialize(obj);
        }
    }
}
