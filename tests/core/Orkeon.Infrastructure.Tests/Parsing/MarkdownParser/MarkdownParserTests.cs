using Orkeon.Infrastructure.Parsing;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Infrastructure.Tests.Parsing;

public class MarkdownParserTests
{
    [Fact]
    public void ShouldRenderHtml_WhenToHtmlBasicMarkdown()
    {
        var parser = new MarkdownParser();
        var html = parser.ToHtml("# Title\n\n**bold** _italic_\n\n[link](https://example.com)");
        Assert.Contains("<h1", html);
        Assert.Contains("<strong>", html);
        Assert.Contains("<em>", html);
        Assert.Contains("<a href=\"https://example.com\"", html);
    }

    [Fact]
    public void ShouldRemoveMarkdownSyntax_WhenToPlainText()
    {
        var parser = new MarkdownParser();
        var text = parser.ToPlainText("# Title\nSome **bold** and *italic* text with `code` and [link](url).");
        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("*italic*", text);
        Assert.DoesNotContain("`code`", text);
        Assert.Contains("link", text);
        Assert.Contains("Title", text);
    }

    [Fact]
    public void ShouldParseKeyValues_WhenExtractMetadataYamlFrontMatter()
    {
        var parser = new MarkdownParser();
        var md = "---\ntitle: Sample Doc\nauthor: Jane Doe\n---\n\n# Content";
        var meta = parser.ExtractMetadata(md);
        Assert.Equal("Sample Doc", meta["title"]);
        Assert.Equal("Jane Doe", meta["author"]);
    }

    [Fact]
    public void ShouldRemoveDangerousHtml_WhenSanitize()
    {
        var parser = new MarkdownParser();
        var md = "<script>alert('x')</script><iframe src='x'></iframe><a href=\"javascript:evil()\" onclick=\"x\">x</a>";
        var sanitized = parser.Sanitize(md);
        Assert.DoesNotContain("script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldFindAllLinks_WhenExtractLinks()
    {
        var parser = new MarkdownParser();
        var md = "See [A](https://a.com) and [B](https://b.com).";
        var links = parser.ExtractLinks(md).ToList();
        Assert.Contains("https://a.com", links);
        Assert.Contains("https://b.com", links);
    }

    [Fact]
    public void ShouldReturnLevelsAndTexts_WhenExtractHeaders()
    {
        var parser = new MarkdownParser();
        var md = "# H1\nText\n## H2\n### H3";
        var headers = parser.ExtractHeaders(md).ToList();
        Assert.Contains((1, "H1"), headers);
        Assert.Contains((2, "H2"), headers);
        Assert.Contains((3, "H3"), headers);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenToHtmlWithNullInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ToHtml(null!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenToHtmlWithEmptyInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ToHtml("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenToHtmlWithWhitespaceInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ToHtml("   \n  \t  ");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenToPlainTextWithNullInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ToPlainText(null!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenToPlainTextWithEmptyInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ToPlainText("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ShouldRemoveBlockquoteMarkers_WhenToPlainTextWithBlockquotes()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = "> This is a quote\n> Second line";

        // Act
        var result = parser.ToPlainText(markdown);

        // Assert
        Assert.DoesNotContain(">", result);
        Assert.Contains("This is a quote", result);
        Assert.Contains("Second line", result);
    }

    [Fact]
    public void ShouldRemoveListMarkers_WhenToPlainTextWithListItems()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = "* Item 1\n- Item 2\n* Item 3";

        // Act
        var result = parser.ToPlainText(markdown);

        // Assert
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("-", result);
        Assert.Contains("Item 1", result);
        Assert.Contains("Item 2", result);
        Assert.Contains("Item 3", result);
    }

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenExtractMetadataWithNoFrontMatter()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = "# Just Content\n\nNo front matter here";

        // Act
        var result = parser.ExtractMetadata(markdown);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenExtractMetadataWithNullInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ExtractMetadata(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldParseCorrectly_WhenExtractMetadataWithComplexYaml()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = @"---
title: Complex Document
author: John Doe
date: 2024-01-15
version: 1.2.3
tags: test, documentation, example
---

# Content";

        // Act
        var result = parser.ExtractMetadata(markdown);

        // Assert
        Assert.Equal("Complex Document", result["title"]);
        Assert.Equal("John Doe", result["author"]);
        Assert.Equal("2024-01-15", result["date"]);
        Assert.Equal("1.2.3", result["version"]);
        Assert.Equal("test, documentation, example", result["tags"]);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenSanitizeWithNullInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.Sanitize(null!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ShouldReturnUnchanged_WhenSanitizeWithCleanMarkdown()
    {
        // Arrange
        var parser = new MarkdownParser();
        var cleanMarkdown = "# Clean Title\n\nThis is **safe** content";

        // Act
        var result = parser.Sanitize(cleanMarkdown);

        // Assert
        Assert.Equal(cleanMarkdown, result);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenExtractLinksWithNoLinks()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = "# No Links Here\n\nJust plain text";

        // Act
        var result = parser.ExtractLinks(markdown).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenExtractLinksWithNullInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ExtractLinks(null!).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldReturnUnique_WhenExtractLinksWithDuplicateLinks()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = "[Link1](https://example.com) and [Link2](https://example.com) and [Link3](https://other.com)";

        // Act
        var result = parser.ExtractLinks(markdown).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(TestBaseUrl, result);
        Assert.Contains("https://other.com", result);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenExtractHeadersWithNoHeaders()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = "Just plain text without any headers";

        // Act
        var result = parser.ExtractHeaders(markdown).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenExtractHeadersWithNullInput()
    {
        // Arrange
        var parser = new MarkdownParser();

        // Act
        var result = parser.ExtractHeaders(null!).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldExtractAllHeaders_WhenExtractHeadersWithAllLevels()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = @"# Level 1
## Level 2
### Level 3
#### Level 4
##### Level 5
###### Level 6";

        // Act
        var result = parser.ExtractHeaders(markdown).ToList();

        // Assert
        Assert.Equal(6, result.Count);
        Assert.Contains((1, "Level 1"), result);
        Assert.Contains((2, "Level 2"), result);
        Assert.Contains((3, "Level 3"), result);
        Assert.Contains((4, "Level 4"), result);
        Assert.Contains((5, "Level 5"), result);
        Assert.Contains((6, "Level 6"), result);
    }

    [Fact]
    public void ShouldRenderCodeElement_WhenToHtmlWithCodeBlock()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = "```csharp\nvar x = 10;\n```";

        // Act
        var html = parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<pre>", html);
        Assert.Contains("<code", html);
        Assert.Contains("var x = 10;", html);
    }

    [Fact]
    public void ShouldRenderTableElements_WhenToHtmlWithTable()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = @"| Column 1 | Column 2 |
|----------|----------|
| Cell 1   | Cell 2   |";

        // Act
        var html = parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<table>", html);
        Assert.Contains("<th>", html);
        Assert.Contains("<td>", html);
        Assert.Contains("Column 1", html);
        Assert.Contains("Cell 1", html);
    }

    [Fact]
    public void ShouldRemoveAllFormatting_WhenToPlainTextWithComplexMarkdown()
    {
        // Arrange
        var parser = new MarkdownParser();
        var markdown = @"# Header

**Bold** and *italic* and `inline code`.

> Blockquote

- List item 1
* List item 2

[Link](https://example.com)

```
Code block
```";

        // Act
        var result = parser.ToPlainText(markdown);

        // Assert
        Assert.Contains("Header", result);
        Assert.Contains("Bold", result);
        Assert.Contains("italic", result);
        Assert.Contains("inline code", result);
        Assert.Contains("Blockquote", result);
        Assert.Contains("List item 1", result);
        Assert.Contains("List item 2", result);
        Assert.Contains("Link", result);
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain("-", result);
        Assert.DoesNotContain("#", result);
    }
}
