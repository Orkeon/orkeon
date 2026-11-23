using Orkeon.Tools.Chat.Text;

namespace Orkeon.Tools.Chat.Tests.Text;

/// <summary>What a model wrote becomes HTML that can hold no tag, mention or dangerous address of its own.</summary>
public sealed class MarkdownRendererTests
{
    [Theory]
    [InlineData("**bold** and ~~gone~~", "<p><strong>bold</strong> and <del>gone</del></p>")]
    [InlineData("# Title", "<h1>Title</h1>")]
    [InlineData("[site](https://example.org/a?b=1&c=2)", "<p><a href=\"https://example.org/a?b=1&amp;c=2\">site</a></p>")]
    [InlineData("`<b>`", "<p><code>&lt;b&gt;</code></p>")]
    public void Should_render_markdown(string markdown, string html) => Assert.Equal(html, MarkdownRenderer.FromMarkdown(markdown).Trim());

    [Fact]
    public void Should_render_lists_tables_and_code()
    {
        var html = MarkdownRenderer.FromMarkdown("- a\n- b\n\n| h |\n|---|\n| c |\n\n```\nx < y\n```");

        Assert.Contains("<li>a</li>", html, StringComparison.Ordinal);
        Assert.Contains("<td>c</td>", html, StringComparison.Ordinal);
        Assert.Contains("x &lt; y", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("a <at id=\"0\">Ada</at> b")]
    [InlineData("<at id=\"0\">Ada</at>")]
    [InlineData("<img src=\"https://evil.test/p.png\">")]
    [InlineData("<div>\n\n<at id=\"0\">x</at>\n\n</div>")]
    [InlineData("<!-- x --> <at id='0'>")]
    [InlineData("<attachment id=\"1\"></attachment>")]
    [InlineData("&lt;at id=\"0\"&gt;Ada&lt;/at&gt;")]
    [InlineData("&#60;at id=&#34;0&#34;&#62;Ada&#60;/at&#62;")]
    [InlineData("```\"><at id=\"0\">\nx\n```")]
    [InlineData("[<at id=\"0\">x</at>](https://example.org)")]
    [InlineData("[x](https://example.org \"><at id='0'>\")")]
    [InlineData("[x](https://example.org/\"><at/id='0'>)")]
    [InlineData("<https://example.org/\"><at>")]
    [InlineData("\\<at id=\"0\">Ada\\</at>")]
    public void Should_encode_raw_HTML_and_mentions(string markdown)
    {
        var html = MarkdownRenderer.FromMarkdown(markdown);

        foreach (var tag in new[] { "<at", "<script", "<img", "<div", "<!--", "<attachment" })
            Assert.DoesNotContain(tag, html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JaVaScRiPt:alert)")]
    [InlineData("[x](java&#115;cript:alert)")]
    [InlineData("[x](&#x6A;avascript:alert)")]
    [InlineData("[x](java\\script:alert)")]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("[x](<java\tscript:alert>)")]
    [InlineData("[x](data:text/html,hi)")]
    [InlineData("[x](vbscript:m)")]
    [InlineData("[x](file:///etc/passwd)")]
    [InlineData("[x](//evil.test/a)")]
    [InlineData("[x](/relative)")]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)")]
    [InlineData("[x]\n\n[x]: data:text/html,hi")]
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("![x][r]\n\n[r]: javascript:alert(1)")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<data:text/html,hi>")]
    [InlineData("ftp://example.org/a")]
    [InlineData("tel:123")]
    public void Should_keep_no_dangerous_address(string markdown)
    {
        var html = MarkdownRenderer.FromMarkdown(markdown);

        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("<p>", html, StringComparison.Ordinal);
        Assert.NotEqual("<p></p>", html.Trim());
    }

    [Fact]
    public void Should_keep_the_text_of_a_dropped_link_in_order() =>
        Assert.Equal("<p>a <em>b</em> c d</p>", MarkdownRenderer.FromMarkdown("[a *b* c](javascript:x) d").Trim());

    [Theory]
    [InlineData("![logo](https://example.org/l.png)")]
    [InlineData("![logo][r]\n\n[r]: https://example.org/l.png")]
    public void Should_turn_an_image_into_a_link(string markdown) =>
        Assert.Equal("<p><a href=\"https://example.org/l.png\">logo</a></p>", MarkdownRenderer.FromMarkdown(markdown).Trim());

    [Fact]
    public void Should_never_embed_an_image_inside_a_link() =>
        Assert.DoesNotContain("<img", MarkdownRenderer.FromMarkdown("[![logo](https://example.org/l.png)](https://example.org/a)"), StringComparison.Ordinal);

    [Theory]
    [InlineData("<https://example.org/a>")]
    [InlineData("https://example.org/a")]
    [InlineData("[https://example.org/a][r]\n\n[r]: https://example.org/a")]
    public void Should_link_a_bare_web_address(string markdown) =>
        Assert.Equal("<p><a href=\"https://example.org/a\">https://example.org/a</a></p>", MarkdownRenderer.FromMarkdown(markdown).Trim());

    [Fact]
    public void Should_encode_plain_text_and_keep_its_lines()
    {
        var html = MarkdownRenderer.FromText("<at id=\"0\"> **x**\r\ny\nz");

        Assert.DoesNotContain("<at", html, StringComparison.Ordinal);
        Assert.Contains("&lt;at id=&quot;0&quot;&gt; **x**<br>y<br>z", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_refuse_or_render_a_hostile_nesting_without_an_unexpected_failure()
    {
        string? html = null;
        var error = Record.Exception(() => html = MarkdownRenderer.FromMarkdown(new string('>', 3000) + " deep"));

        if (error is null)
            Assert.Contains("deep", html, StringComparison.Ordinal);
        else
            Assert.Equal(ChatErrorCode.InvalidRequest, Assert.IsType<ChatToolException>(error).Code);
    }
}
