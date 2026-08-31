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
    /// (canonical) and snake_case (legacy dialect parity) keys, and ignores unknown properties
    /// so that forward-compatible YAML extensions do not crash the loader.
    /// </summary>
    public class YamlDotNetSerializer : IYamlSerializer
    {
        private readonly IDeserializer _deserializer;
        private readonly ISerializer _serializer;
        private readonly ISerializer _tidySerializer;

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

            // The same camelCase form, minus the properties nobody set. YamlDotNet's default
            // is Preserve, which writes every null as a bare `key:` line — a generated crew
            // came out with eight empty keys under the crew and nine under each task, so the
            // three lines that matter were buried in the ones that did not.
            //
            // OmitNull, never OmitDefaults: `verbose: false` and `memory: false` are explicit
            // choices, distinguishable downstream from "unset", and OmitDefaults would eat them.
            _tidySerializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();
        }

        /// <summary>
        /// Serializes without the keys nobody set. Opt-in rather than the default: a consumer
        /// that reads back a file expecting every key to be present must not be changed under
        /// it, even though the crew loader itself is tolerant.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>The YAML string representation, without null-valued keys.</returns>
        public string SerializeWithoutNulls<T>(T obj) => _tidySerializer.Serialize(obj);

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
