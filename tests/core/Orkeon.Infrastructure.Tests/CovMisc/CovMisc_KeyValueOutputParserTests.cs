using Orkeon.Infrastructure.OutputParsing.Parsers;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Additional coverage for <see cref="KeyValueOutputParser{T}"/> and the non-generic
/// <see cref="KeyValueOutputParser"/>, focusing on object mapping, type conversion and
/// error/fallback paths not exercised by the existing dictionary-centric tests.
/// </summary>
public class CovMisc_KeyValueOutputParserTests
{
    public sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public double Score { get; set; }
        public string FullAddress { get; set; } = "";
    }

    [Fact]
    public void Generic_Parse_Null_Throws()
    {
        var parser = new KeyValueOutputParser<Person>();
        Assert.Throws<ArgumentNullException>(() => parser.Parse(null!));
    }

    [Fact]
    public void Generic_MapsToObject_WithTypeConversion()
    {
        var parser = new KeyValueOutputParser<Person>();
        var input = """
            Name: Alice
            Age: 42
            Score: 9.5
            """;

        var result = parser.Parse(input);

        Assert.Equal("Alice", result.Name);
        Assert.Equal(42, result.Age);
        Assert.Equal(9.5, result.Score);
    }

    [Fact]
    public void Generic_MapsSpacedKey_ToCompactPropertyName()
    {
        var parser = new KeyValueOutputParser<Person>();
        // "Full Address" has a space; matched against "FullAddress" by stripping spaces.
        var input = "Full Address: 1 Main St";

        var result = parser.Parse(input);

        Assert.Equal("1 Main St", result.FullAddress);
    }

    [Fact]
    public void Generic_SkipsUnconvertibleValue_WithoutThrowing()
    {
        var parser = new KeyValueOutputParser<Person>();
        var input = """
            Name: Bob
            Age: not-a-number
            """;

        var result = parser.Parse(input);

        Assert.Equal("Bob", result.Name);
        Assert.Equal(0, result.Age); // conversion failed and was skipped
    }

    [Fact]
    public void Generic_IgnoresUnknownKeys()
    {
        var parser = new KeyValueOutputParser<Person>();
        var input = "Unknown: whatever\nName: Carol";

        var result = parser.Parse(input);

        Assert.Equal("Carol", result.Name);
    }

    [Fact]
    public void Generic_TryParse_ReturnsTrue_OnObjectMapping()
    {
        var parser = new KeyValueOutputParser<Person>();
        var success = parser.TryParse("Name: Dave", out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal("Dave", result!.Name);
    }

    [Fact]
    public void Generic_TryParse_WhitespaceOnly_ReturnsFalse()
    {
        var parser = new KeyValueOutputParser<Person>();
        var success = parser.TryParse("   \n  ", out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public async Task Generic_ParseAsync_ReturnsResult()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var result = await parser.ParseAsync("Key: Value", TestContext.Current.CancellationToken);

        Assert.Equal("Value", result["Key"]);
    }

    [Fact]
    public async Task Generic_ParseAsync_HonorsCancellation()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => parser.ParseAsync("Key: Value", cts.Token));
    }

    // --- Non-generic ---

    [Fact]
    public void NonGeneric_Parse_Null_Throws()
    {
        var parser = new KeyValueOutputParser();
        Assert.Throws<ArgumentNullException>(() => parser.Parse(null!, typeof(Person)));
    }

    [Fact]
    public void NonGeneric_MapsToTypedObject()
    {
        var parser = new KeyValueOutputParser();
        var input = """
            Name: Eve
            Age: 7
            """;

        var result = parser.Parse(input, typeof(Person));

        var person = Assert.IsType<Person>(result);
        Assert.Equal("Eve", person.Name);
        Assert.Equal(7, person.Age);
    }

    [Fact]
    public void NonGeneric_SkipsUnconvertibleValue()
    {
        var parser = new KeyValueOutputParser();
        var input = "Age: xyz\nName: Frank";

        var result = parser.Parse(input, typeof(Person));

        var person = Assert.IsType<Person>(result);
        Assert.Equal("Frank", person.Name);
        Assert.Equal(0, person.Age);
    }

    [Fact]
    public void NonGeneric_TryParse_ReturnsTrue_ForTypedObject()
    {
        var parser = new KeyValueOutputParser();
        var success = parser.TryParse("Name: Grace", typeof(Person), out var result);

        Assert.True(success);
        var person = Assert.IsType<Person>(result);
        Assert.Equal("Grace", person.Name);
    }
}
