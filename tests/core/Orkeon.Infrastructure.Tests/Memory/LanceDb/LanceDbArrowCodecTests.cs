using Orkeon.Infrastructure.Memory.LanceDb;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// Round-trip tests for <see cref="LanceDbArrowCodec"/> — the Arrow IPC payloads
/// exchanged with the LanceDB server. Exercises both wire formats: the stream
/// format used for inserts and the file format used by query responses.
/// </summary>
public class LanceDbArrowCodecTests
{
    private const int Dimension = 4;

    [Fact]
    public void ShouldRoundTripAllColumns_WhenEncodeThenDecode()
    {
        // Arrange
        var record = new LanceDbRecord
        {
            Id = "rt-1",
            Content = "Round-trip content",
            Embedding = [0.1f, 0.2f, 0.3f, 0.4f],
            Importance = 0.8f,
            Source = "codec-test",
            Tags = ["ai", "agents"],
            CreatedAt = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc),
            MetadataJson = "{\"team\":\"eng\"}"
        };

        // Act — encode to the Arrow IPC stream format, decode through the sniffer
        var payload = LanceDbArrowCodec.EncodeRecords([record], Dimension);
        var rows = LanceDbArrowCodec.DecodeQueryResponse(payload);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("rt-1", row.Record.Id);
        Assert.Equal("Round-trip content", row.Record.Content);
        Assert.Equal(record.Embedding, row.Record.Embedding);
        Assert.Equal(0.8f, row.Record.Importance);
        Assert.Equal("codec-test", row.Record.Source);
        Assert.NotNull(row.Record.Tags);
        Assert.Equal(["ai", "agents"], row.Record.Tags);
        Assert.Equal(record.CreatedAt, row.Record.CreatedAt);
        Assert.Equal("{\"team\":\"eng\"}", row.Record.MetadataJson);
        Assert.Null(row.Distance); // no server ranking columns in storage payloads
        Assert.Null(row.Score);
    }

    [Fact]
    public void ShouldPreserveNullEmbedding_WhenEncodeThenDecode()
    {
        var record = new LanceDbRecord { Id = "no-vec", Content = "Text only" };

        var payload = LanceDbArrowCodec.EncodeRecords([record], Dimension);
        var rows = LanceDbArrowCodec.DecodeQueryResponse(payload);

        var row = Assert.Single(rows);
        Assert.Null(row.Record.Embedding);
    }

    [Fact]
    public void ShouldEncodeSchemaOnlyBatch_WhenNoRecords()
    {
        // Used to create an empty table with the expected schema.
        var payload = LanceDbArrowCodec.EncodeRecords([], Dimension);

        Assert.NotEmpty(payload);
        Assert.Empty(LanceDbArrowCodec.DecodeQueryResponse(payload));
    }

    [Fact]
    public void ShouldThrowInvalidOperation_WhenEmbeddingDimensionMismatch()
    {
        var record = new LanceDbRecord { Id = "bad", Content = "x", Embedding = [1f, 2f] };

        var exception = Assert.Throws<InvalidOperationException>(
            () => LanceDbArrowCodec.EncodeRecords([record], Dimension));

        Assert.Contains("dimension mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldDecodeArrowFileFormat_WhenServerStyleResponse()
    {
        // Arrange — the query endpoint replies in the Arrow IPC *file* format
        // (ARROW1 magic), with the server ranking column appended.
        var payload = LanceDbMemoryProviderTestsFixture.BuildArrowFile(
            [new LanceDbMemoryProviderTestsFixture.LanceDbTestRow(
                "f1", "File format row", Embedding: [1f, 0f, 0f, 0f], Distance: 0.25f)]);

        // Act
        var rows = LanceDbArrowCodec.DecodeQueryResponse(payload);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("f1", row.Record.Id);
        Assert.Equal("File format row", row.Record.Content);
        Assert.NotNull(row.Distance);
        Assert.Equal(0.25f, row.Distance!.Value, precision: 5);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenPayloadIsEmpty()
    {
        Assert.Empty(LanceDbArrowCodec.DecodeQueryResponse([]));
    }

    [Fact]
    public void ShouldRoundTripMultipleRecords_WhenBatchEncoded()
    {
        var records = new[]
        {
            new LanceDbRecord { Id = "m1", Content = "First", Embedding = [1f, 0f, 0f, 0f] },
            new LanceDbRecord { Id = "m2", Content = "Second" },
            new LanceDbRecord { Id = "m3", Content = "Third", Embedding = [0f, 0f, 0f, 1f] }
        };

        var rows = LanceDbArrowCodec.DecodeQueryResponse(LanceDbArrowCodec.EncodeRecords(records, Dimension));

        Assert.Equal(3, rows.Count);
        Assert.Equal(["m1", "m2", "m3"], rows.Select(r => r.Record.Id).ToArray());
        Assert.NotNull(rows[0].Record.Embedding);
        Assert.Null(rows[1].Record.Embedding);
        var thirdEmbedding = rows[2].Record.Embedding;
        Assert.NotNull(thirdEmbedding);
        Assert.Equal([0f, 0f, 0f, 1f], thirdEmbedding);
    }
}
