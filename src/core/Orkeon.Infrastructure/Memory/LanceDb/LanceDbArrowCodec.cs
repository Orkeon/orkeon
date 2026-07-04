using System.Globalization;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Arrow IPC (de)serialization for the LanceDB REST protocol: encodes
/// <see cref="LanceDbRecord"/> rows as an Arrow IPC stream for
/// <c>insert</c>/<c>merge_insert</c>/<c>create</c> payloads, and decodes the Arrow
/// IPC file (or stream) bodies returned by <c>query</c> back into rows.
/// </summary>
internal static class LanceDbArrowCodec
{
    internal const string IdColumn = "id";
    internal const string ContentColumn = "content";
    internal const string VectorColumn = "vector";
    internal const string ImportanceColumn = "importance";
    internal const string SourceColumn = "source";
    internal const string TagsColumn = "tags";
    internal const string CreatedAtColumn = "created_at";
    internal const string MetadataColumn = "metadata_json";

    /// <summary>Distance column appended by the server to vector search results.</summary>
    internal const string DistanceColumn = "_distance";

    /// <summary>Relevance column appended by the server to full-text search results.</summary>
    internal const string ScoreColumn = "_score";

    private static readonly byte[] s_arrowFileMagic = "ARROW1"u8.ToArray();

    /// <summary>
    /// Builds the Arrow schema of the memory table. The vector column is a
    /// fixed-size list of <paramref name="embeddingDimension"/> float32 values,
    /// nullable so that items without embeddings can be stored.
    /// </summary>
    public static Schema BuildSchema(int embeddingDimension)
    {
        var vectorType = new FixedSizeListType(
            new Field("item", FloatType.Default, nullable: true),
            embeddingDimension);

        return new Schema.Builder()
            .Field(f => f.Name(IdColumn).DataType(StringType.Default).Nullable(false))
            .Field(f => f.Name(ContentColumn).DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name(VectorColumn).DataType(vectorType).Nullable(true))
            .Field(f => f.Name(ImportanceColumn).DataType(FloatType.Default).Nullable(true))
            .Field(f => f.Name(SourceColumn).DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name(TagsColumn).DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name(CreatedAtColumn).DataType(StringType.Default).Nullable(true))
            .Field(f => f.Name(MetadataColumn).DataType(StringType.Default).Nullable(true))
            .Build();
    }

    /// <summary>
    /// Encodes the supplied records as a single-batch Arrow IPC stream
    /// (<c>application/vnd.apache.arrow.stream</c>). An empty record list produces a
    /// schema-bearing zero-row batch, suitable for creating an empty table.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a record carries an embedding whose length differs from
    /// <paramref name="embeddingDimension"/> — the fixed-size server schema cannot
    /// store it faithfully.
    /// </exception>
    public static byte[] EncodeRecords(IReadOnlyList<LanceDbRecord> records, int embeddingDimension)
    {
        var schema = BuildSchema(embeddingDimension);

        var ids = new StringArray.Builder();
        var contents = new StringArray.Builder();
        var importances = new FloatArray.Builder();
        var sources = new StringArray.Builder();
        var tags = new StringArray.Builder();
        var createdAts = new StringArray.Builder();
        var metadata = new StringArray.Builder();

        var vectorValues = new FloatArray.Builder();
        var vectorValidity = new ArrowBuffer.BitmapBuilder();
        var vectorNullCount = 0;

        foreach (var record in records)
        {
            ids.Append(record.Id);
            contents.Append(record.Content);
            importances.Append(record.Importance);
            sources.Append(record.Source);
            AppendNullable(tags, LanceDbRecordMapper.SerializeTags(record.Tags));
            createdAts.Append(record.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            AppendNullable(metadata, record.MetadataJson);

            if (record.Embedding is { Length: > 0 })
            {
                if (record.Embedding.Length != embeddingDimension)
                {
                    throw new InvalidOperationException(
                        $"Embedding dimension mismatch for key '{record.Id}': the LanceDB table stores " +
                        $"fixed-size vectors of {embeddingDimension} dimensions but the item carries " +
                        $"{record.Embedding.Length}. Align LanceDbOptions.EmbeddingDimension with the embedding provider.");
                }

                foreach (var value in record.Embedding)
                {
                    vectorValues.Append(value);
                }

                vectorValidity.Append(true);
            }
            else
            {
                // Fixed-size list children must stay aligned even for null rows.
                for (var i = 0; i < embeddingDimension; i++)
                {
                    vectorValues.Append(0f);
                }

                vectorValidity.Append(false);
                vectorNullCount++;
            }
        }

        var vectorType = (FixedSizeListType)schema.GetFieldByName(VectorColumn).DataType;
        var vectorArray = new FixedSizeListArray(
            vectorType,
            records.Count,
            vectorValues.Build(),
            vectorValidity.Build(),
            vectorNullCount);

        using var batch = new RecordBatch(
            schema,
            new IArrowArray[]
            {
                ids.Build(),
                contents.Build(),
                vectorArray,
                importances.Build(),
                sources.Build(),
                tags.Build(),
                createdAts.Build(),
                metadata.Build()
            },
            records.Count);

        using var stream = new MemoryStream();
        using (var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true))
        {
            writer.WriteRecordBatch(batch);
            writer.WriteEnd();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Decodes a query response payload (Arrow IPC file or stream format — the format
    /// is sniffed from the <c>ARROW1</c> magic bytes) into rows. Server ranking columns
    /// (<c>_distance</c>, <c>_score</c>) are surfaced when present.
    /// </summary>
    public static IReadOnlyList<LanceDbQueryRow> DecodeQueryResponse(byte[] payload)
    {
        if (payload is not { Length: > 0 })
            return [];

        using var stream = new MemoryStream(payload, writable: false);
        using ArrowStreamReader reader = HasArrowFileMagic(payload)
            ? new ArrowFileReader(stream)
            : new ArrowStreamReader(stream);

        var rows = new List<LanceDbQueryRow>();
        while (true)
        {
            using var batch = reader.ReadNextRecordBatch();
            if (batch is null)
                break;

            AppendRows(batch, rows);
        }

        return rows;
    }

    private static bool HasArrowFileMagic(byte[] payload)
    {
        if (payload.Length < s_arrowFileMagic.Length)
            return false;

        for (var i = 0; i < s_arrowFileMagic.Length; i++)
        {
            if (payload[i] != s_arrowFileMagic[i])
                return false;
        }

        return true;
    }

    private static void AppendRows(RecordBatch batch, List<LanceDbQueryRow> rows)
    {
        var idColumn = FindColumn(batch, IdColumn) as StringArray;
        var contentColumn = FindColumn(batch, ContentColumn) as StringArray;
        var vectorColumn = FindColumn(batch, VectorColumn) as FixedSizeListArray;
        var importanceColumn = FindColumn(batch, ImportanceColumn);
        var sourceColumn = FindColumn(batch, SourceColumn) as StringArray;
        var tagsColumn = FindColumn(batch, TagsColumn) as StringArray;
        var createdAtColumn = FindColumn(batch, CreatedAtColumn) as StringArray;
        var metadataColumn = FindColumn(batch, MetadataColumn) as StringArray;
        var distanceColumn = FindColumn(batch, DistanceColumn);
        var scoreColumn = FindColumn(batch, ScoreColumn);

        for (var i = 0; i < batch.Length; i++)
        {
            var record = new LanceDbRecord
            {
                Id = GetString(idColumn, i) ?? "",
                Content = GetString(contentColumn, i) ?? "",
                Embedding = ReadVector(vectorColumn, i),
                Importance = ReadFloat(importanceColumn, i) ?? MemoryDefaults.DefaultImportance,
                Source = GetString(sourceColumn, i) ?? "unknown",
                Tags = LanceDbRecordMapper.DeserializeTags(GetString(tagsColumn, i)),
                CreatedAt = ParseTimestamp(GetString(createdAtColumn, i)),
                MetadataJson = GetString(metadataColumn, i)
            };

            rows.Add(new LanceDbQueryRow(record, ReadFloat(distanceColumn, i), ReadFloat(scoreColumn, i)));
        }
    }

    private static IArrowArray? FindColumn(RecordBatch batch, string name)
    {
        var fields = batch.Schema.FieldsList;
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return batch.Column(i);
        }

        return null;
    }

    private static string? GetString(StringArray? array, int index)
        => array is null || array.IsNull(index) ? null : array.GetString(index);

    private static float? ReadFloat(IArrowArray? array, int index)
    {
        if (array is null || array.IsNull(index))
            return null;

        return array switch
        {
            FloatArray floats => floats.GetValue(index),
            DoubleArray doubles => (float?)doubles.GetValue(index),
            _ => null
        };
    }

    private static float[]? ReadVector(FixedSizeListArray? array, int index)
    {
        if (array is null || array.IsNull(index))
            return null;

        var listSize = ((FixedSizeListType)array.Data.DataType).ListSize;
        var start = (array.Offset + index) * listSize;
        var result = new float[listSize];

        switch (array.Values)
        {
            case FloatArray floats:
                for (var i = 0; i < listSize; i++)
                    result[i] = floats.GetValue(start + i) ?? 0f;
                break;

            case DoubleArray doubles:
                for (var i = 0; i < listSize; i++)
                    result[i] = (float)(doubles.GetValue(start + i) ?? 0d);
                break;

            default:
                return null;
        }

        return result;
    }

    private static DateTime ParseTimestamp(string? value)
    {
        if (!string.IsNullOrEmpty(value) &&
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return DateTime.UtcNow;
    }

    private static void AppendNullable(StringArray.Builder builder, string? value)
    {
        if (value is null)
        {
            builder.AppendNull();
        }
        else
        {
            builder.Append(value);
        }
    }
}
