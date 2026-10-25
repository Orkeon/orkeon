using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Tests.Factories;

/// <summary>Tests for the constructors of <see cref="RagComponentNotFoundException"/>.</summary>
public class RagComponentNotFoundExceptionTests
{
    [Fact]
    public void ShouldListKnownNamesInOrdinalOrder_WhenBuiltFromLookup()
    {
        var exception = new RagComponentNotFoundException("reranker", "onnx", ["llm", "Cohere", "bm25"]);

        Assert.Equal("Unknown reranker 'onnx'. Known names and aliases: Cohere, bm25, llm.", exception.Message);
        Assert.Equal("onnx", exception.RequestedName);
        Assert.Equal(3, exception.KnownNames.Count);
        Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
    }

    [Fact]
    public void ShouldHaveEmptyLookupDetails_WhenBuiltWithoutArguments()
    {
        var exception = new RagComponentNotFoundException();

        Assert.Equal(string.Empty, exception.RequestedName);
        Assert.Empty(exception.KnownNames);
    }

    [Fact]
    public void ShouldKeepRawMessage_WhenBuiltFromMessage()
    {
        var exception = new RagComponentNotFoundException("custom");

        Assert.Equal("custom", exception.Message);
        Assert.Equal(string.Empty, exception.RequestedName);
        Assert.Empty(exception.KnownNames);
    }

    [Fact]
    public void ShouldKeepInnerException_WhenBuiltFromMessageAndInner()
    {
        var inner = new FormatException("bad");

        var exception = new RagComponentNotFoundException("outer", inner);

        Assert.Equal("outer", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(string.Empty, exception.RequestedName);
        Assert.Empty(exception.KnownNames);
    }
}
