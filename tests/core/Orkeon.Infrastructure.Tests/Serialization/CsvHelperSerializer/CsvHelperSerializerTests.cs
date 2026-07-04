using System.Text;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

public class CsvHelperSerializerTests
{
    private readonly CsvHelperSerializer _serializer;

    public CsvHelperSerializerTests()
    {
        _serializer = new CsvHelperSerializer();
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructor()
    {
        // Arrange & Act
        var serializer = new CsvHelperSerializer();

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    public void ShouldReturnCsv_WhenSerializeWithValidList()
    {
        // Arrange
        var items = new List<TestCsvObject>
        {
            new() { Id = 1, Name = "Alice", Age = 30, IsActive = true },
            new() { Id = 2, Name = "Bob", Age = 25, IsActive = false },
            new() { Id = 3, Name = "Charlie", Age = 35, IsActive = true }
        };

        // Act
        var csv = _serializer.Serialize(items);

        // Assert
        Assert.NotNull(csv);
        Assert.Contains("Id,Name,Age,IsActive", csv);
        Assert.Contains("1,Alice,30,True", csv);
        Assert.Contains("2,Bob,25,False", csv);
        Assert.Contains("3,Charlie,35,True", csv);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSerializeWithNullList()
    {
        // Arrange
        List<TestCsvObject>? items = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => _serializer.Serialize(items!));
        Assert.Equal("records", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnHeaderOnly_WhenSerializeWithEmptyList()
    {
        // Arrange
        var items = new List<TestCsvObject>();

        // Act
        var csv = _serializer.Serialize(items);

        // Assert
        Assert.NotNull(csv);
        Assert.Contains("Id,Name,Age,IsActive", csv);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines); // Only header line
    }

    [Fact]
    public void ShouldReturnList_WhenDeserializeWithValidCsv()
    {
        // Arrange
        var csv = @"Id,Name,Age,IsActive
1,Alice,30,True
2,Bob,25,False
3,Charlie,35,True";

        // Act
        var items = _serializer.Deserialize<TestCsvObject>(csv);

        // Assert
        Assert.NotNull(items);
        var list = items.ToList();
        Assert.Equal(3, list.Count);

        Assert.Equal(1, list[0].Id);
        Assert.Equal("Alice", list[0].Name);
        Assert.Equal(30, list[0].Age);
        Assert.True(list[0].IsActive);

        Assert.Equal(2, list[1].Id);
        Assert.Equal("Bob", list[1].Name);
        Assert.Equal(25, list[1].Age);
        Assert.False(list[1].IsActive);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeWithNullCsv()
    {
        // Arrange
        string? csv = null;

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => _serializer.Deserialize<TestCsvObject>(csv!));
        Assert.Equal("csv", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeWithEmptyCsv()
    {
        // Arrange
        var csv = "";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.Deserialize<TestCsvObject>(csv));
        Assert.Equal("csv", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeWithWhitespaceCsv()
    {
        // Arrange
        var csv = "   \n\t  ";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.Deserialize<TestCsvObject>(csv));
        Assert.Equal("csv", exception.ParamName);
    }

    [Fact]
    public void ShouldPreserveData_WhenSerializeAndDeserialize()
    {
        // Arrange
        var original = new List<TestCsvObject>
        {
            new() { Id = 1, Name = "Test User", Age = 42, IsActive = true },
            new() { Id = 2, Name = "Another User", Age = 33, IsActive = false }
        };

        // Act
        var csv = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<TestCsvObject>(csv).ToList();

        // Assert
        Assert.Equal(original.Count, deserialized.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, deserialized[i].Id);
            Assert.Equal(original[i].Name, deserialized[i].Name);
            Assert.Equal(original[i].Age, deserialized[i].Age);
            Assert.Equal(original[i].IsActive, deserialized[i].IsActive);
        }
    }

    [Fact]
    public async Task ShouldWriteToStream_WhenSerializeAsyncWithValidList()
    {
        // Arrange
        var items = new List<TestCsvObject>
        {
            new() { Id = 1, Name = "Alice", Age = 30, IsActive = true },
            new() { Id = 2, Name = "Bob", Age = 25, IsActive = false }
        };
        var stream = new MemoryStream();

        // Act
        await _serializer.SerializeAsync(items, stream);

        // Assert
        var bytes = stream.ToArray();
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.NotNull(csv);
        Assert.Contains("Id,Name,Age,IsActive", csv);
        Assert.Contains("1,Alice,30,True", csv);
        Assert.Contains("2,Bob,25,False", csv);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSerializeAsyncWithNullRecords()
    {
        // Arrange
        List<TestCsvObject>? items = null;
        using var stream = new MemoryStream();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _serializer.SerializeAsync(items!, stream));
        Assert.Equal("records", exception.ParamName);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSerializeAsyncWithNullStream()
    {
        // Arrange
        var items = new List<TestCsvObject> { new() { Id = 1, Name = "Test", Age = 20, IsActive = true } };
        Stream? stream = null;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _serializer.SerializeAsync(items, stream!));
        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public async Task ShouldReturnList_WhenDeserializeAsyncWithValidStream()
    {
        // Arrange
        var csv = @"Id,Name,Age,IsActive
1,Alice,30,True
2,Bob,25,False";
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var stream = new MemoryStream(bytes);

        // Act
        var items = await _serializer.DeserializeAsync<TestCsvObject>(stream);

        // Assert
        Assert.NotNull(items);
        var list = items.ToList();
        Assert.Equal(2, list.Count);

        Assert.Equal(1, list[0].Id);
        Assert.Equal("Alice", list[0].Name);
        Assert.Equal(30, list[0].Age);
        Assert.True(list[0].IsActive);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenDeserializeAsyncWithNullStream()
    {
        // Arrange
        Stream? stream = null;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _serializer.DeserializeAsync<TestCsvObject>(stream!));
        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnDynamicList_WhenDeserializeDynamicWithValidCsv()
    {
        // Arrange
        var csv = @"Id,Name,Age,IsActive
1,Alice,30,True
2,Bob,25,False";

        // Act
        var items = _serializer.DeserializeDynamic(csv);

        // Assert
        Assert.NotNull(items);
        var list = items.ToList();
        Assert.Equal(2, list.Count);

        dynamic first = list[0];
        Assert.Equal("1", first.Id);
        Assert.Equal("Alice", first.Name);
        Assert.Equal("30", first.Age);
        Assert.Equal("True", first.IsActive);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeDynamicWithNullCsv()
    {
        // Arrange
        string? csv = null;

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => _serializer.DeserializeDynamic(csv!));
        Assert.Equal("csv", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeDynamicWithEmptyCsv()
    {
        // Arrange
        var csv = "";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.DeserializeDynamic(csv));
        Assert.Equal("csv", exception.ParamName);
    }

    [Fact]
    public void ShouldEscapeCorrectly_WhenSerializeWithSpecialCharacters()
    {
        // Arrange
        var items = new List<TestCsvObject>
        {
            new() { Id = 1, Name = "Alice, Bob", Age = 30, IsActive = true },
            new() { Id = 2, Name = "\"Quoted Name\"", Age = 25, IsActive = false },
            new() { Id = 3, Name = "Name\nWith\nNewlines", Age = 35, IsActive = true }
        };

        // Act
        var csv = _serializer.Serialize(items);

        // Assert
        Assert.NotNull(csv);
        // CSV should escape commas, quotes, and newlines properly
        Assert.Contains("\"Alice, Bob\"", csv);
        Assert.Contains("\"\"\"Quoted Name\"\"\"", csv);
        Assert.Contains("\"Name\nWith\nNewlines\"", csv);
    }

    [Fact]
    public void ShouldParseCorrectly_WhenDeserializeWithSpecialCharacters()
    {
        // Arrange
        var csv = "Id,Name,Age,IsActive" + "\r\n" +
@"1,""Alice, Bob"",30,True" + "\r\n" +
@"2,""""""Quoted Name"""""",25,False" + "\r\n" +
@"3,""Name" + "\r\n" +
@"With" + "\r\n" +
@"Newlines"",35,True";

        // Act
        var items = _serializer.Deserialize<TestCsvObject>(csv).ToList();

        // Assert
        Assert.Equal(3, items.Count);
        Assert.Equal("Alice, Bob", items[0].Name);
        Assert.Equal("\"Quoted Name\"", items[1].Name);
        Assert.Equal("Name\r\nWith\r\nNewlines", items[2].Name);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenSerializeWithNullProperties()
    {
        // Arrange
        var items = new List<TestCsvObjectWithNulls>
        {
            new() { Id = 1, Name = "Alice", OptionalValue = 100, OptionalText = "Text" },
            new() { Id = 2, Name = "Bob", OptionalValue = null, OptionalText = null },
            new() { Id = 3, Name = "Charlie", OptionalValue = 200, OptionalText = null }
        };

        // Act
        var csv = _serializer.Serialize(items);

        // Assert
        Assert.NotNull(csv);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length); // Header + 3 data rows

        // Second row should have empty values for nulls
        Assert.Contains("2,Bob,,", lines[2]);
    }

    [Fact]
    public async Task ShouldHandleEfficiently_WhenSerializeAndDeserializeAsyncWithLargeDataset()
    {
        // Arrange
        var items = new List<TestCsvObject>();
        for (int i = 1; i <= 1000; i++)
        {
            items.Add(new TestCsvObject
            {
                Id = i,
                Name = $"User {i}",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0
            });
        }

        // Act - Serialize to stream
        var serializeStream = new MemoryStream();
        await _serializer.SerializeAsync(items, serializeStream);

        // Create a new stream from the serialized bytes for deserialization
        var bytes = serializeStream.ToArray();
        using var deserializeStream = new MemoryStream(bytes);
        var deserialized = await _serializer.DeserializeAsync<TestCsvObject>(deserializeStream);

        // Assert
        var deserializedList = deserialized.ToList();
        Assert.Equal(1000, deserializedList.Count);
        Assert.Equal("User 500", deserializedList[499].Name);
        Assert.Equal(20, deserializedList[499].Age); // 20 + (500 % 50) = 20 + 0 = 20
        Assert.True(deserializedList[499].IsActive); // 500 is even
    }
}

// Test models
internal class TestCsvObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

internal class TestCsvObjectWithNulls
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? OptionalValue { get; set; }
    public string? OptionalText { get; set; }
}
