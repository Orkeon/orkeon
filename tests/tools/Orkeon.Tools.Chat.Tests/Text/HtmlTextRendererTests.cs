using Orkeon.Tools.Chat.Text;

namespace Orkeon.Tools.Chat.Tests.Text;

/// <summary>The HTML Teams stores, read as text.</summary>
public sealed class HtmlTextRendererTests
{
    [Theory]
    [InlineData("a<br>b", "a\nb")]
    [InlineData("<p>a &amp; b&nbsp;c &lt;at&gt;</p>", "a & b c <at>")]
    [InlineData("<p><at id=\"0\">Ada</at> hello</p>", "Ada hello")]
    [InlineData("<a href=\"https://example.org\">https://example.org</a>", "https://example.org")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "x")]
    [InlineData("<a href=\"data:text/html,y\">x</a>", "x")]
    [InlineData("<p>a</p><script>bad()</script><style>p{}</style>", "a")]
    [InlineData("<p>well done <emoji id=\"like\" alt=\"👍\" title=\"Like\"></emoji></p>", "well done 👍")]
    [InlineData("", "")]
    public void Should_render_the_visible_text(string html, string text)
    {
        var rendered = HtmlTextRenderer.Render(html);

        Assert.Equal(text, rendered.Text);
        Assert.False(rendered.HadHiddenContent);
    }

    [Theory]
    [InlineData("<p>one</p><p>two</p>", "one", "two")]
    [InlineData("<ul><li>one</li><li>two</li></ul>", "one", "two")]
    public void Should_put_blocks_on_lines_of_their_own(string html, string first, string second)
    {
        var lines = HtmlTextRenderer.Render(html).Text.Split('\n');

        Assert.EndsWith(first, lines[0], StringComparison.Ordinal);
        Assert.EndsWith(second, lines[^1], StringComparison.Ordinal);
        Assert.True(lines.Length > 1);
    }

    [Fact]
    public void Should_show_the_address_of_a_link_and_the_name_of_an_image()
    {
        Assert.Contains("https://example.org/a", HtmlTextRenderer.Render("<a href=\"https://example.org/a\">site</a>").Text, StringComparison.Ordinal);
        Assert.Contains("a chart", HtmlTextRenderer.Render("<img alt=\"a chart\" src=\"https://example.org/i.png\">").Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<span style=\"display: none\">secretly</span>")]
    [InlineData("<span style=\"VISIBILITY:hidden\">secretly</span>")]
    [InlineData("<span style=\"color:red;font-size:0px\">secretly</span>")]
    [InlineData("<span style=\"font-size:0\">secretly</span>")]
    [InlineData("<span style=\"opacity:0\">secretly</span>")]
    [InlineData("<div hidden><p>secretly</p></div>")]
    public void Should_leave_hidden_text_out_and_report_it(string hidden)
    {
        var rendered = HtmlTextRenderer.Render($"<p>shown</p>{hidden}");

        Assert.Equal("shown", rendered.Text);
        Assert.True(rendered.HadHiddenContent);
    }

    [Fact]
    public void Should_not_take_a_thin_border_for_hidden_text() =>
        Assert.False(HtmlTextRenderer.Render("<p style=\"border-width:0\">x</p>").HadHiddenContent);

    [Fact]
    public void Should_survive_a_hostile_nesting() =>
        Assert.Null(Record.Exception(() => HtmlTextRenderer.Render(string.Concat(Enumerable.Repeat("<div>", 20_000)) + "x")));
}
