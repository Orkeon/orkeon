using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>#432: the documented context windows, matched like the output caps.</summary>
public sealed class LlmModelContextWindowsTests
{
    [Theory]
    [Trait("BR", "CH-432")]
    [InlineData("kimi-k3", 1_000_000)]
    [InlineData("KIMI-K2.6", 256_000)]                 // case never matters on an id
    [InlineData("mistral-medium-2604", 256_000)]
    [InlineData("mistral/mistral-medium-3-5", 256_000)] // a vendor prefix is dropped
    [InlineData("kimi-k3.1-pro", null)]                // '.' is no separator: no page gives its window
    [InlineData("nobody/knows-this-one", null)]
    [InlineData(null, null)]
    public void ShouldMatchAnIdExactlyThenByFamilyThenWithoutItsVendor_WhenTheTableIsAsked(string? model, int? expected)
    {
        // Arrange
        var id = model;

        // Act
        var window = LlmModelContextWindows.ContextWindowTokens(id);

        // Assert
        Assert.Equal(expected, window);
    }
}
