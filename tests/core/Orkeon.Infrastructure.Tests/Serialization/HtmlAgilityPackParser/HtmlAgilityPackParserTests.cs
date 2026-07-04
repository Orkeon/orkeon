using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

public class HtmlAgilityPackParserTests
{
    private readonly HtmlAgilityPackParser _parser;

    public HtmlAgilityPackParserTests()
    {
        _parser = new HtmlAgilityPackParser();
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructor()
    {
        // Arrange & Act
        var parser = new HtmlAgilityPackParser();

        // Assert
        Assert.NotNull(parser);
    }

    [Fact]
    public void ShouldReturnText_WhenExtractTextWithSimpleHtml()
    {
        // Arrange
        var html = "<html><body><p>Hello World</p></body></html>";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Equal("Hello World", text);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenExtractTextWithNullHtml()
    {
        // Arrange
        string? html = null;

        // Act
        var text = _parser.ExtractText(html!);

        // Assert
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenExtractTextWithEmptyHtml()
    {
        // Arrange
        var html = "";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenExtractTextWithWhitespaceHtml()
    {
        // Arrange
        var html = "   \n\t  ";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ShouldExcludeThem_WhenExtractTextWithScriptAndStyleTags()
    {
        // Arrange
        var html = @"
            <html>
            <head>
                <style>body { color: red; }</style>
            </head>
            <body>
                <p>Visible text</p>
                <script>console.log('invisible');</script>
                <div>More visible text</div>
            </body>
            </html>";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.DoesNotContain("color: red", text);
        Assert.DoesNotContain("console.log", text);
        Assert.Contains("Visible text", text);
        Assert.Contains("More visible text", text);
    }

    [Fact]
    public void ShouldDecodeThemProperly_WhenExtractTextWithHtmlEntities()
    {
        // Arrange
        var html = "<p>&lt;Hello&gt; &amp; &quot;World&quot; &copy; 2024</p>";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Equal("<Hello> & \"World\" © 2024", text);
    }

    [Fact]
    public void ShouldExtractAllText_WhenExtractTextWithNestedElements()
    {
        // Arrange
        var html = @"
            <div>
                <h1>Title</h1>
                <p>Paragraph with <strong>bold</strong> and <em>italic</em> text.</p>
                <ul>
                    <li>Item 1</li>
                    <li>Item 2</li>
                </ul>
            </div>";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Contains("Title", text);
        Assert.Contains("Paragraph with bold and italic text", text);
        Assert.Contains("Item 1", text);
        Assert.Contains("Item 2", text);
    }

    [Fact]
    public void ShouldExtractLabelsAndValues_WhenExtractTextWithFormElements()
    {
        // Arrange
        var html = @"
            <form>
                <label>Name:</label>
                <input type='text' value='John' />
                <button>Submit</button>
                <textarea>Comments here</textarea>
            </form>";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Contains("Name:", text);
        Assert.Contains("Submit", text);
        Assert.Contains("Comments here", text);
    }

    [Fact]
    public void ShouldReturnElements_WhenExtractElementsWithValidSelector()
    {
        // Arrange
        var html = @"
            <html>
            <body>
                <p class='paragraph'>First paragraph</p>
                <p class='paragraph'>Second paragraph</p>
                <p>Third paragraph without class</p>
            </body>
            </html>";

        // Act
        var elements = _parser.ExtractElements(html, "//p[@class='paragraph']");

        // Assert
        var list = elements.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("First paragraph", list[0]);
        Assert.Equal("Second paragraph", list[1]);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenExtractElementsWithNullHtml()
    {
        // Arrange
        string? html = null;

        // Act
        var elements = _parser.ExtractElements(html!, "//p");

        // Assert
        Assert.Empty(elements);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenExtractElementsWithEmptyHtml()
    {
        // Arrange
        var html = "";

        // Act
        var elements = _parser.ExtractElements(html, "//p");

        // Assert
        Assert.Empty(elements);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenExtractElementsWithWhitespaceHtml()
    {
        // Arrange
        var html = "   \n\t  ";

        // Act
        var elements = _parser.ExtractElements(html, "//p");

        // Assert
        Assert.Empty(elements);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenExtractElementsWithNoMatchingElements()
    {
        // Arrange
        var html = "<html><body><div>No paragraphs here</div></body></html>";

        // Act
        var elements = _parser.ExtractElements(html, "//p");

        // Assert
        Assert.Empty(elements);
    }

    [Fact]
    public void ShouldReturnCorrectElements_WhenExtractElementsWithComplexSelector()
    {
        // Arrange
        var html = @"
            <div id='content'>
                <article>
                    <h2>Article 1</h2>
                    <p>Content 1</p>
                </article>
                <article>
                    <h2>Article 2</h2>
                    <p>Content 2</p>
                </article>
            </div>";

        // Act
        var headings = _parser.ExtractElements(html, "//div[@id='content']//article/h2");

        // Assert
        var list = headings.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("Article 1", list[0]);
        Assert.Equal("Article 2", list[1]);
    }

    [Fact]
    public void ShouldDecodeThemProperly_WhenExtractElementsWithHtmlEntities()
    {
        // Arrange
        var html = @"
            <ul>
                <li>&lt;Item 1&gt;</li>
                <li>&quot;Item 2&quot;</li>
                <li>Item &amp; 3</li>
            </ul>";

        // Act
        var elements = _parser.ExtractElements(html, "//li");

        // Assert
        var list = elements.ToList();
        Assert.Equal(3, list.Count);
        Assert.Equal("<Item 1>", list[0]);
        Assert.Equal("\"Item 2\"", list[1]);
        Assert.Equal("Item & 3", list[2]);
    }

    [Fact]
    public void ShouldIgnoreEmptyElements_WhenExtractElements()
    {
        // Arrange
        var html = @"
            <ul>
                <li>Item 1</li>
                <li></li>
                <li>   </li>
                <li>Item 2</li>
            </ul>";

        // Act
        var elements = _parser.ExtractElements(html, "//li");

        // Assert
        var list = elements.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("Item 1", list[0]);
        Assert.Equal("Item 2", list[1]);
    }

    [Fact]
    public void ShouldExtractCorrectly_WhenExtractElementsWithTableData()
    {
        // Arrange
        var html = @"
            <table>
                <tr>
                    <th>Name</th>
                    <th>Age</th>
                </tr>
                <tr>
                    <td>Alice</td>
                    <td>30</td>
                </tr>
                <tr>
                    <td>Bob</td>
                    <td>25</td>
                </tr>
            </table>";

        // Act
        var names = _parser.ExtractElements(html, "//td[1]"); // First td in each row
        var ages = _parser.ExtractElements(html, "//td[2]");  // Second td in each row

        // Assert
        var nameList = names.ToList();
        var ageList = ages.ToList();

        Assert.Equal(2, nameList.Count);
        Assert.Equal("Alice", nameList[0]);
        Assert.Equal("Bob", nameList[1]);

        Assert.Equal(2, ageList.Count);
        Assert.Equal("30", ageList[0]);
        Assert.Equal("25", ageList[1]);
    }

    [Fact]
    public void ShouldHandleLineBreaks_WhenExtractTextWithBrTags()
    {
        // Arrange
        var html = "<p>Line 1<br/>Line 2<br />Line 3</p>";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Contains("Line 1", text);
        Assert.Contains("Line 2", text);
        Assert.Contains("Line 3", text);
    }

    [Fact]
    public void ShouldIgnoreComments_WhenExtractTextWithCommentsInHtml()
    {
        // Arrange
        var html = @"
            <div>
                Visible text
                <!-- This is a comment and should not appear -->
                More visible text
            </div>";

        // Act
        var text = _parser.ExtractText(html);

        // Assert
        Assert.Contains("Visible text", text);
        Assert.Contains("More visible text", text);
        Assert.DoesNotContain("This is a comment", text);
    }
}
