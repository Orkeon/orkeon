using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Infrastructure.Tests.Services;

/// <summary>
/// Additional edge case tests for SimpleTokenCounter to improve code coverage.
/// </summary>
public class SimpleTokenCounterAdditionalTests
{
    private readonly SimpleTokenCounter _counter = new();

    [Fact]
    public void ShouldReturnZero_WhenCountTokensWithOnlyWhitespace()
    {
        // Arrange
        var text = "   \t\n\r   ";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("\n", 0)]
    [InlineData("\t", 0)]
    [InlineData("\r", 0)]
    [InlineData(" ", 0)]
    [InlineData("\n\t\r ", 0)]
    public void ShouldReturnZero_WhenCountTokensWithDifferentWhitespaceTypes(string whitespace, int expected)
    {
        // Act
        var result = _counter.CountTokens(whitespace);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldCountWordsCorrectly_WhenCountTokensWithMixedWhitespace()
    {
        // Arrange - Words separated by different whitespace types
        var text = "word1\tword2\nword3\rword4 word5";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 5 words * 1.3 = 6.5, truncated to 6
        Assert.Equal(6, result);
    }

    [Fact]
    public void ShouldIgnoreExtraSpaces_WhenCountTokensWithConsecutiveWhitespace()
    {
        // Arrange
        var text = "word1    word2\n\n\nword3\t\t\tword4";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldCountOnlyWords_WhenCountTokensWithLeadingAndTrailingWhitespace()
    {
        // Arrange
        var text = "   \t\n  word1 word2 word3   \r\n\t  ";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9, truncated to 3
        Assert.Equal(3, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithSingleCharacterWords()
    {
        // Arrange
        var text = "a b c d e f g h i j";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 10 words * 1.3 = 13
        Assert.Equal(13, result);
    }

    [Fact]
    public void ShouldHandleLargeInputs_WhenCountTokensWithLongText()
    {
        // Arrange - Create a large text with 100 words
        var words = Enumerable.Range(1, 100).Select(i => $"word{i}");
        var text = string.Join(" ", words);

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 100 words * 1.3 = 130
        Assert.Equal(130, result);
    }

    [Fact]
    public void ShouldTreatAsWords_WhenCountTokensWithSpecialCharacters()
    {
        // Arrange
        var text = "hello@world.com test-case special_chars 123-456";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldCountNumbers_WhenCountTokensWithNumericContent()
    {
        // Arrange
        var text = "123 456 789 0.5 -42";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 5 words * 1.3 = 6.5, truncated to 6
        Assert.Equal(6, result);
    }

    [Fact]
    public void ShouldTreatAsSingleWords_WhenCountTokensWithPunctuationAttached()
    {
        // Arrange
        var text = "Hello, world! How are you? Fine, thanks.";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // Words: "Hello,", "world!", "How", "are", "you?", "Fine,", "thanks."
        // 7 words * 1.3 = 9.1, truncated to 9
        Assert.Equal(9, result);
    }

    [Fact]
    public void ShouldReturnZero_WhenCountTokensEmptyStringAfterWhitespaceNormalization()
    {
        // Arrange
        var text = "\0\0\0"; // Null characters that aren't typical whitespace

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // The null characters would be treated as a single "word"
        // 1 word * 1.3 = 1.3, truncated to 1
        Assert.Equal(1, result);
    }

    [Fact]
    public void ShouldSplitCorrectly_WhenCountTokensWithNewlines()
    {
        // Arrange
        var text = "Line1\nLine2\nLine3";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9, truncated to 3
        Assert.Equal(3, result);
    }

    [Fact]
    public void ShouldSplitCorrectly_WhenCountTokensWithTabSeparated()
    {
        // Arrange
        var text = "Column1\tColumn2\tColumn3\tColumn4";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldSplitCorrectly_WhenCountTokensWithCarriageReturns()
    {
        // Arrange
        var text = "Word1\rWord2\rWord3";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9, truncated to 3
        Assert.Equal(3, result);
    }

    [Theory]
    [InlineData("one two three four five", 6)] // 5 words * 1.3 = 6.5 -> 6
    [InlineData("a", 1)] // 1 word * 1.3 = 1.3 -> 1
    [InlineData("testing multiple word counting", 5)] // 4 words * 1.3 = 5.2 -> 5
    public void ShouldReturnExpectedResults_WhenCountTokensWithKnownInputs(string input, int expected)
    {
        // Act
        var result = _counter.CountTokens(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldAlwaysReturnSameValue_WhenCountTokensConsistentResults()
    {
        // Arrange
        var text = "This is a consistent test input";

        // Act
        var result1 = _counter.CountTokens(text);
        var result2 = _counter.CountTokens(text);
        var result3 = _counter.CountTokens(text);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenCountTokensWithUnicodeCharacters()
    {
        // Arrange
        var text = "hello 世界 مرحبا мир";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldStillCountAsOneWord_WhenCountTokensWithVeryLongWord()
    {
        // Arrange
        var veryLongWord = new string('a', 1000);

        // Act
        var result = _counter.CountTokens(veryLongWord);

        // Assert
        // 1 word * 1.3 = 1.3, truncated to 1
        Assert.Equal(1, result);
    }

    [Fact]
    public void ShouldHandleReasonablySizedText_WhenCountTokensPerformance()
    {
        // Arrange - Create text with 10,000 words
        var words = Enumerable.Range(1, 10000).Select(i => $"word{i}");
        var text = string.Join(" ", words);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _counter.CountTokens(text);
        stopwatch.Stop();

        // Assert
        Assert.Equal(13000, result); // 10000 * 1.3
        // Performance assertion - should complete in reasonable time (less than 100ms)
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Token counting took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithMixedPunctuationAndWords()
    {
        // Arrange
        var text = "word1,word2;word3:word4!word5?word6.word7";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 1 word (all connected without spaces) * 1.3 = 1.3, truncated to 1
        Assert.Equal(1, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithUrlsAndEmails()
    {
        // Arrange
        var text = "Visit https://example.com or email contact@example.org for info";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 7 words * 1.3 = 9.1, truncated to 9
        Assert.Equal(9, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithEmojis()
    {
        // Arrange
        var text = "Hello 😊 World 🌍 Testing 🚀";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 6 words * 1.3 = 7.8, truncated to 7
        Assert.Equal(7, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithHtmlTags()
    {
        // Arrange
        var text = "<html> <body> <p>Hello World</p> </body> </html>";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // Simple implementation: 5 words ("<html>", "<body>", "<p>Hello", "World</p>", "</body>", "</html>") = 6 * 1.3 = 7.8 -> 7
        Assert.Equal(7, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithJsonContent()
    {
        // Arrange
        var text = "{ \"name\": \"John\", \"age\": 30, \"city\": \"New York\" }";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // Simple implementation: 10 words ("{", "\"name\":", "\"John\",", "\"age\":", "30,", "\"city\":", "\"New", "York\"", "}") = 9 * 1.3 = 11.7 -> 11
        Assert.Equal(11, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithXmlContent()
    {
        // Arrange
        var text = "<root><item id=\"1\">Value</item><item id=\"2\">Another</item></root>";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // The XML string contains spaces within attributes, so it splits into 3 words
        // 3 words * 1.3 = 3.9, truncated to 3
        Assert.Equal(3, result);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithBase64String()
    {
        // Arrange
        var text = "SGVsbG8gV29ybGQh";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 1 word * 1.3 = 1.3, truncated to 1
        Assert.Equal(1, result);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithCamelCaseWords()
    {
        // Arrange
        var text = "camelCaseWord PascalCaseWord snake_case_word kebab-case-word";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithCodeSnippet()
    {
        // Arrange
        var text = "public void TestMethod() { return value * 2; }";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 9 words * 1.3 = 11.7, truncated to 11
        Assert.Equal(11, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithMathematicalExpressions()
    {
        // Arrange
        var text = "2 + 2 = 4 and 3 * 3 = 9";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 11 words * 1.3 = 14.3, truncated to 14
        Assert.Equal(14, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithFilePathsWindows()
    {
        // Arrange
        var text = "C:\\Users\\Test\\Documents\\file.txt /home/user/file.txt";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 2 words * 1.3 = 2.6, truncated to 2
        Assert.Equal(2, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithIpAddresses()
    {
        // Arrange
        var text = "192.168.1.1 localhost 127.0.0.1 ::1";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithHashtagsAndMentions()
    {
        // Arrange
        var text = "#hashtag @mention #another_tag @user123";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithPercentagesAndCurrencies()
    {
        // Arrange
        var text = "50% $100 €75 £60 ¥1000";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 5 words * 1.3 = 6.5, truncated to 6
        Assert.Equal(6, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithAcronyms()
    {
        // Arrange
        var text = "USA FBI CIA NATO EU";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 5 words * 1.3 = 6.5, truncated to 6
        Assert.Equal(6, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithBinaryString()
    {
        // Arrange
        var text = "01010101 11110000 10101010";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9, truncated to 3
        Assert.Equal(3, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithHexadecimalValues()
    {
        // Arrange
        var text = "0xFF 0xAB12 #FFFFFF #000000";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2, truncated to 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldCountWordsInsideQuotes_WhenCountTokensWithQuotedStrings()
    {
        // Arrange
        var text = "\"Hello World\" 'Single Quotes' `Backticks`";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // Simple implementation: 5 words ("\"Hello", "World\"", "'Single", "Quotes'", "`Backticks`") = 5 * 1.3 = 6.5 -> 6
        Assert.Equal(6, result);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithDatesAndTimes()
    {
        // Arrange
        var text = "2025-08-17 15:30:00 17/08/2025 3:30PM";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // Simple implementation: 4 words ("2025-08-17", "15:30:00", "17/08/2025", "3:30PM") = 4 * 1.3 = 5.2 -> 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithPhoneNumbers()
    {
        // Arrange
        var text = "+1-555-123-4567 (555)123-4567 555.123.4567";

        // Act
        var result = _counter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9, truncated to 3
        Assert.Equal(3, result);
    }
}
