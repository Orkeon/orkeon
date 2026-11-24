using Orkeon.Host.Gateway;

namespace Orkeon.Host.Tests;

/// <summary>What an answer may not do in Teams: render HTML, or load an image of its choice.</summary>
public sealed class TeamsMarkdownTests
{
    [Theory]
    [InlineData("a List<string> & 1 > 0", "a List&lt;string> & 1 > 0")]
    [InlineData("<img src=\"https://x.test/?d=1\">", "&lt;img src=\"https://x.test/?d=1\">")]
    [InlineData("see ![a](https://x.test/p.png)!", "see [a](https://x.test/p.png)!")]
    [InlineData("!![a](u) ![b][c] wow!!", "[a](u) [b][c] wow!!")]
    [InlineData("`List<string>` and ``a ` <b>`` <c>", "`List<string>` and ``a ` <b>`` &lt;c>")]
    [InlineData("`open <img>", "`open &lt;img>")]
    [InlineData("\\`<img>`", "\\`&lt;img>`")]
    [InlineData("`open\n<img>`", "`open\n&lt;img>`")]
    [InlineData("```cs\nList<string> ![x](y)\n```\n<b>", "```cs\nList<string> ![x](y)\n```\n&lt;b>")]
    [InlineData("~~~\n```\n<a>\n~~~\n<b>", "~~~\n```\n<a>\n~~~\n&lt;b>")]
    [InlineData("````\n```\n<a>\n````\n<b>", "````\n```\n<a>\n````\n&lt;b>")]
    [InlineData("```\n``` no end\n<a>", "```\n``` no end\n<a>")]
    [InlineData("``` <img> ```\n<img>", "``` <img> ```\n&lt;img>")]
    [InlineData("    ```\n<img>", "    ```\n&lt;img>")]
    [InlineData("~~~ <img>\n<a>", "~~~ &lt;img>\n<a>")]
    [InlineData("a\r\n<b>\r\n", "a\r\n&lt;b>\r\n")]
    [InlineData("", "")]
    public void Html_and_images_outside_code_are_text_and_links(string text, string expected) =>
        Assert.Equal(expected, TeamsMarkdown.Harmless(text));
}
