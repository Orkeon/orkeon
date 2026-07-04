using CsvHelper;
using CsvHelper.Configuration;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using System.Globalization;
using System.Text;

namespace Orkeon.Infrastructure.Serialization
{
    /// <summary>
    /// RFC 4180-compliant CSV serializer built on CsvHelper.
    /// <para>
    /// Implements <see cref="ICsvSerializer"/> following the RFC 4180 standard
    /// (Common Format and MIME Type for Comma-Separated Values):
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Comma (<c>,</c>) as field delimiter.</description></item>
    ///   <item><description>CRLF (<c>\r\n</c>) as record delimiter.</description></item>
    ///   <item><description>First record is a header row containing field names.</description></item>
    ///   <item><description>Fields containing commas, double quotes, or line breaks are enclosed in double quotes.</description></item>
    ///   <item><description>Double quotes inside a field are escaped by doubling them (<c>""</c>).</description></item>
    /// </list>
    /// <example>
    /// Serialization:
    /// <code>
    /// var serializer = new CsvHelperSerializer();
    /// var csv = serializer.Serialize(new[] { new { Name = "Alice", Age = 30 } });
    /// // Result: Name,Age\r\nAlice,30\r\n
    /// </code>
    /// </example>
    /// <example>
    /// RFC 4180 quoting rules — a value containing a literal double quote:
    /// <code>
    /// // Value: "Quoted Name"
    /// // CSV field: """Quoted Name"""
    /// //   - Outer quotes: field quoting (RFC 4180 §2.6)
    /// //   - Inner "": escaped double quote (RFC 4180 §2.7)
    /// </code>
    /// </example>
    /// </summary>
    /// <remarks>
    /// Uses <see cref="CultureInfo.InvariantCulture"/> for deterministic formatting
    /// across locales (decimal separators, date formats, etc.).
    /// </remarks>
    /// <seealso href="https://datatracker.ietf.org/doc/html/rfc4180">RFC 4180 — Common Format and MIME Type for CSV Files</seealso>
    public class CsvHelperSerializer : ICsvSerializer
    {
        private readonly CsvConfiguration _configuration;

        /// <summary>Initializes a new instance of <see cref="CsvHelperSerializer"/> with RFC 4180 defaults.</summary>
        public CsvHelperSerializer()
        {
            _configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ","
            };
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Thrown when <paramref name="csv"/> is null or whitespace.</exception>
        /// <exception cref="CsvHelper.BadDataException">Thrown when the CSV input violates RFC 4180 quoting rules.</exception>
        public IEnumerable<T> Deserialize<T>(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                throw new ArgumentException("CSV content cannot be null or empty", nameof(csv));
            }

            using var reader = new StringReader(csv);
            using var csvReader = new CsvReader(reader, _configuration);

            return csvReader.GetRecords<T>().ToList();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="records"/> is null.</exception>
        public string Serialize<T>(IEnumerable<T> records)
        {
            ArgumentNullException.ThrowIfNull(records);

            var sb = new StringBuilder();
            using var writer = new StringWriter(sb);
            using var csvWriter = new CsvWriter(writer, _configuration);

            csvWriter.WriteRecords(records);
            csvWriter.Flush();

            return sb.ToString();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="records"/> or <paramref name="stream"/> is null.</exception>
        public Task SerializeAsync<T>(IEnumerable<T> records, Stream stream)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(stream);

            return SerializeAsyncCore(records, stream, _configuration);

            static async Task SerializeAsyncCore(IEnumerable<T> records, Stream stream, CsvConfiguration configuration)
            {
                using var writer = new StreamWriter(stream);
                using var csvWriter = new CsvWriter(writer, configuration);

                await csvWriter.WriteRecordsAsync(records).ConfigureAwait(false);
                await csvWriter.FlushAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
        /// <exception cref="CsvHelper.BadDataException">Thrown when the CSV input violates RFC 4180 quoting rules.</exception>
        public Task<IEnumerable<T>> DeserializeAsync<T>(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            return DeserializeAsyncCore(stream, _configuration);

            static async Task<IEnumerable<T>> DeserializeAsyncCore(Stream stream, CsvConfiguration configuration)
            {
                using var reader = new StreamReader(stream);
                using var csvReader = new CsvReader(reader, configuration);

                var records = new List<T>();
                await foreach (var record in csvReader.GetRecordsAsync<T>().ConfigureAwait(false))
                {
                    records.Add(record);
                }
                return records;
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Thrown when <paramref name="csv"/> is null or whitespace.</exception>
        /// <exception cref="CsvHelper.BadDataException">Thrown when the CSV input violates RFC 4180 quoting rules.</exception>
        public IEnumerable<dynamic> DeserializeDynamic(string csv)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(csv);

            using var reader = new StringReader(csv);
            using var csvReader = new CsvReader(reader, _configuration);

            return csvReader.GetRecords<dynamic>().ToList();
        }
    }
}
