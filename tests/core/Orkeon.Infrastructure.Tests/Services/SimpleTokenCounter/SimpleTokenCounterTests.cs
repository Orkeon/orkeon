using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Infrastructure.Tests.Services;

public class SimpleTokenCounterTests
{
    private readonly SimpleTokenCounter _tokenCounter;

    public SimpleTokenCounterTests()
    {
        _tokenCounter = new SimpleTokenCounter();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("hello", 1)]
    [InlineData("hello world", 2)]
    [InlineData("one two   three\nfour\tfive", 5)]
    public void ShouldReturnApproxWordCount_WhenCountTokensVariousInputs(string? text, int expectedWords)
    {
        // Arrange
        var counter = new SimpleTokenCounter();

        // Act
        var tokens = counter.CountTokens(text ?? string.Empty);

        // Assert
        // Implementation multiplies word count by 1.3 then truncates
        var minExpected = (int)(expectedWords * 1.3);
        Assert.Equal(minExpected, tokens);
    }

    [Fact]
    public void ShouldCountWordsCorrectly_WhenCountTokensWithPunctuation()
    {
        // Arrange
        var text = "Hello, world! How are you?";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 5 words (Hello, world! How are you?) * 1.3 = 6.5 -> 6
        Assert.Equal(6, tokens);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCountTokensWithMixedWhitespace()
    {
        // Arrange
        var text = "Word1\tWord2\r\nWord3  Word4";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2 -> 5
        Assert.Equal(5, tokens);
    }

    [Fact]
    public void ShouldCountWordsOnly_WhenCountTokensWithSpecialCharacters()
    {
        // Arrange
        var text = "@user #hashtag https://example.com word";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2 -> 5
        Assert.Equal(5, tokens);
    }

    [Fact]
    public void ShouldHandleLargeInput_WhenCountTokensWithLongText()
    {
        // Arrange
        var words = new string[1000];
        for (int i = 0; i < 1000; i++)
        {
            words[i] = $"word{i}";
        }
        var text = string.Join(" ", words);

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 1000 words * 1.3 = 1300
        Assert.Equal(1300, tokens);
    }

    [Fact]
    public void ShouldReturnZero_WhenCountTokensWithOnlyWhitespace()
    {
        // Arrange
        var text = "\t\t  \n\r\n  ";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        Assert.Equal(0, tokens);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithSingleCharacterWords()
    {
        // Arrange
        var text = "I a b c d e f";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 7 words * 1.3 = 9.1 -> 9
        Assert.Equal(9, tokens);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCountTokensWithUnicodeCharacters()
    {
        // Arrange
        var text = "Hello 世界 مرحبا мир";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2 -> 5
        Assert.Equal(5, tokens);
    }

    [Theory]
    [InlineData("The quick brown fox", 4)]
    [InlineData("The quick brown fox jumps", 5)]
    [InlineData("The quick brown fox jumps over", 6)]
    [InlineData("The quick brown fox jumps over the", 7)]
    [InlineData("The quick brown fox jumps over the lazy", 8)]
    [InlineData("The quick brown fox jumps over the lazy dog", 9)]
    public void ShouldReturnConsistentCounts_WhenCountTokensWithCommonPhrases(string text, int expectedWords)
    {
        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        var expectedTokens = (int)(expectedWords * 1.3);
        Assert.Equal(expectedTokens, tokens);
    }

    [Fact]
    public void ShouldCountTokensCorrectly_WhenCountTokensWithCodeSnippet()
    {
        // Arrange
        var code = @"public class Test {
                        private int value;
                        public void Method() {
                            Console.WriteLine(""Hello"");
                        }
                    }";

        // Act
        var tokens = _tokenCounter.CountTokens(code);

        // Assert
        // Count words: public class Test { private int value; public void Method() { Console.WriteLine("Hello"); } }
        // Approximately 13 words * 1.3 = 16.9 -> 16
        Assert.True(tokens > 0);
        Assert.True(tokens >= 16);
    }

    [Fact]
    public void ShouldCountTokensCorrectly_WhenCountTokensWithJson()
    {
        // Arrange
        var json = @"{
                        ""name"": ""John Doe"",
                        ""age"": 30,
                        ""city"": ""New York""
                    }";

        // Act
        var tokens = _tokenCounter.CountTokens(json);

        // Assert
        // Words: { "name": "John Doe", "age": 30, "city": "New York" }
        // Approximately 10 words * 1.3 = 13
        Assert.True(tokens > 0);
        Assert.True(tokens >= 10);
    }

    [Fact]
    public void ShouldCountTokensCorrectly_WhenCountTokensWithMarkdown()
    {
        // Arrange
        var markdown = @"# Header
                        ## Subheader
                        * Item 1
                        * Item 2
                        **Bold text**";

        // Act
        var tokens = _tokenCounter.CountTokens(markdown);

        // Assert
        // Words: # Header ## Subheader * Item 1 * Item 2 **Bold text**
        // Approximately 11 words * 1.3 = 14.3 -> 14
        Assert.True(tokens > 0);
        Assert.True(tokens >= 14);
    }

    [Fact]
    public void ShouldReturnSameValueForSameInput_WhenCountTokensConsistentResults()
    {
        // Arrange
        var text = "This is a test sentence with several words";

        // Act
        var result1 = _tokenCounter.CountTokens(text);
        var result2 = _tokenCounter.CountTokens(text);
        var result3 = _tokenCounter.CountTokens(text);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithNumbers()
    {
        // Arrange
        var text = "The year 2024 has 365 days";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 6 words * 1.3 = 7.8 -> 7
        Assert.Equal(7, tokens);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithHyphenatedWords()
    {
        // Arrange
        var text = "This is a well-known fact";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 5 words * 1.3 = 6.5 -> 6
        Assert.Equal(6, tokens);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithEmailAddresses()
    {
        // Arrange
        var text = "Contact us at support@example.com for help";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 6 words * 1.3 = 7.8 -> 7
        Assert.Equal(7, tokens);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithFilePathsAndUrls()
    {
        // Arrange
        var text = "Visit https://www.example.com/page or check C:\\Users\\Documents\\file.txt";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // Simple implementation: 5 words * 1.3 = 6.5 -> 6
        Assert.Equal(6, tokens);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithAcronyms()
    {
        // Arrange
        var text = "The NASA API uses JSON and XML formats";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 8 words * 1.3 = 10.4 -> 10
        Assert.Equal(10, tokens);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithContractions()
    {
        // Arrange
        var text = "Don't can't won't shouldn't wouldn't";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 5 words * 1.3 = 6.5 -> 6
        Assert.Equal(6, tokens);
    }

    [Fact]
    public void ShouldIgnoreExtraSpaces_WhenCountTokensWithMultipleSpaces()
    {
        // Arrange
        var text = "Word1     Word2          Word3";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9 -> 3
        Assert.Equal(3, tokens);
    }

    [Fact]
    public void ShouldIgnore_WhenCountTokensWithLeadingAndTrailingWhitespace()
    {
        // Arrange
        var text = "   hello world   ";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 2 words * 1.3 = 2.6 -> 2
        Assert.Equal(2, tokens);
    }

    [Fact]
    public void ShouldCountCorrectly_WhenCountTokensWithMixedCaseWords()
    {
        // Arrange
        var text = "CamelCase snake_case UPPERCASE lowercase MiXeD";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 5 words * 1.3 = 6.5 -> 6
        Assert.Equal(6, tokens);
    }

    [Fact]
    public void ShouldCountTagsAsWords_WhenCountTokensWithXmlTags()
    {
        // Arrange
        var text = "<tag>content</tag> <another>value</another>";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 2 words * 1.3 = 2.6 -> 2
        Assert.Equal(2, tokens);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithEmoticons()
    {
        // Arrange
        var text = "Hello :) How are you :D";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 6 words * 1.3 = 7.8 -> 7
        Assert.Equal(7, tokens);
    }

    [Fact]
    public void ShouldCountAsWords_WhenCountTokensWithBinaryContent()
    {
        // Arrange
        var text = "01010101 11110000 10101010";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9 -> 3
        Assert.Equal(3, tokens);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithBase64String()
    {
        // Arrange
        var text = "The token is SGVsbG8gV29ybGQh";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 4 words * 1.3 = 5.2 -> 5
        Assert.Equal(5, tokens);
    }

    [Theory]
    [InlineData("\n\n\n", 0)]
    [InlineData("\r\n\r\n", 0)]
    [InlineData("\t\t\t", 0)]
    [InlineData(" \n \r \t ", 0)]
    public void ShouldReturnZero_WhenCountTokensWithOnlyWhitespaceVariations(string text, int expected)
    {
        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        Assert.Equal(expected, tokens);
    }

    [Fact]
    public void ShouldCountAsOneWord_WhenCountTokensWithVeryLongWord()
    {
        // Arrange
        var longWord = new string('a', 1000);
        var text = $"This {longWord} word";

        // Act
        var tokens = _tokenCounter.CountTokens(text);

        // Assert
        // 3 words * 1.3 = 3.9 -> 3
        Assert.Equal(3, tokens);
    }

    [Fact]
    public void ShouldCountTokensCorrectly_WhenCountTokensWithSqlQuery()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE age > 18 AND status = 'active'";

        // Act
        var tokens = _tokenCounter.CountTokens(sql);

        // Assert
        // Simple implementation: 12 words * 1.3 = 15.6 -> 15
        Assert.Equal(15, tokens);
    }

    [Fact]
    public void ShouldCountTokensCorrectly_WhenCountTokensWithMathematicalExpression()
    {
        // Arrange
        var math = "x = 2 * (y + 3) - z / 4";

        // Act
        var tokens = _tokenCounter.CountTokens(math);

        // Assert
        // Simple implementation: 11 words * 1.3 = 14.3 -> 14
        Assert.Equal(14, tokens);
    }

    [Fact]
    public void ShouldCountTokensCorrectly_WhenCountTokensWithCsvData()
    {
        // Arrange
        var csv = "name,age,city\nJohn,30,NYC\nJane,25,LA";

        // Act
        var tokens = _tokenCounter.CountTokens(csv);

        // Assert
        // 3 words * 1.3 = 3.9 -> 3
        Assert.Equal(3, tokens);
    }

    [Fact]
    public void ShouldCountTokensCorrectly_WhenCountTokensWithRegexPattern()
    {
        // Arrange
        var regex = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

        // Act
        var tokens = _tokenCounter.CountTokens(regex);

        // Assert
        // 1 word * 1.3 = 1.3 -> 1
        Assert.Equal(1, tokens);
    }
}
