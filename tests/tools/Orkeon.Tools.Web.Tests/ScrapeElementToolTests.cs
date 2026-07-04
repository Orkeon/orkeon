using Orkeon.Domain.Tools.Protocol;
using System.Net;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Tools.Web.Tests;

public sealed class ScrapeElementToolTests : IDisposable
{
    private static readonly string[] ExpectedTexts = ["Alpha", "Bravo", "Charlie"];
    private static readonly int[] ExpectedIndices = [0, 1, 2];

    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly ScrapeElementTool _tool;

    public ScrapeElementToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new ScrapeElementTool(_httpClient);
    }

    [Fact]
    public async Task ShouldExtractElements_WhenCssSelectorMatchesMultipleElements()
    {
        var html = @"<html><body>
            <ul>
                <li class='item'>Apple</li>
                <li class='item'>Banana</li>
                <li class='item'>Cherry</li>
            </ul>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "li.item"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(3, dict["element_count"]);
        Assert.Equal(3, dict["returned_count"]);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        Assert.Equal(3, elements.Count);
    }

    [Fact]
    public async Task ShouldLimitResults_WhenMaxElementsIsSet()
    {
        var html = @"<html><body>
            <ul>
                <li class='item'>One</li>
                <li class='item'>Two</li>
                <li class='item'>Three</li>
                <li class='item'>Four</li>
                <li class='item'>Five</li>
            </ul>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "li.item",
                ["max_elements"] = 2
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(5, dict["element_count"]);
        Assert.Equal(2, dict["returned_count"]);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        Assert.Equal(2, elements.Count);
    }

    [Fact]
    public async Task ShouldIncludeAttributes_WhenExtractAttributesIsTrue()
    {
        var html = @"<html><body>
            <a href='https://example.com' class='link' id='main-link'>Click me</a>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "a.link",
                ["extract_attributes"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        Assert.Single(elements);

        var element = elements[0] as Dictionary<string, object?>;
        Assert.NotNull(element);
        Assert.Equal("Click me", element["text"]);
        var attrs = element["attributes"] as Dictionary<string, object?>;
        Assert.NotNull(attrs);
        Assert.Equal(TestBaseUrl, attrs["href"]);
        Assert.Equal("link", attrs["class"]);
        Assert.Equal("main-link", attrs["id"]);
    }

    [Fact]
    public async Task ShouldIncludeHtml_WhenIncludeHtmlIsTrue()
    {
        var html = @"<html><body>
            <div class='content'><strong>Bold</strong> text</div>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "div.content",
                ["include_html"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        Assert.Single(elements);

        var element = elements[0] as Dictionary<string, object?>;
        Assert.NotNull(element);
        Assert.Contains("Bold", element["text"]!.ToString()!);
        var rawHtml = element["html"]?.ToString();
        Assert.NotNull(rawHtml);
        Assert.Contains("<strong>", rawHtml);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSelectorMatchesNothing()
    {
        var html = @"<html><body><p>No matching elements here</p></body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "div.nonexistent"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, dict["element_count"]);
        Assert.Equal(0, dict["returned_count"]);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        Assert.Empty(elements);
    }

    [Fact]
    public async Task ShouldReturnError_WhenUrlIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = "not-a-valid-url",
                ["css_selector"] = "div"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid URL", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCssSelectorIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("CSS selector", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRemoveScriptTags_FromExtractedContent()
    {
        var html = @"<html><body>
            <div class='content'>
                <p>Clean text</p>
                <script>alert('xss')</script>
                <style>.red { color: red; }</style>
                <p>More clean text</p>
            </div>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "div.content",
                ["include_html"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        Assert.Single(elements);

        var element = elements[0] as Dictionary<string, object?>;
        Assert.NotNull(element);

        // Text should not contain script/style content
        var text = element["text"]!.ToString()!;
        Assert.Contains("Clean text", text);
        Assert.DoesNotContain("alert", text);
        Assert.DoesNotContain(".red", text);

        // HTML should not contain script/style tags
        var rawHtml = element["html"]?.ToString()!;
        Assert.DoesNotContain("<script>", rawHtml);
        Assert.DoesNotContain("<style>", rawHtml);
        Assert.Contains("Clean text", rawHtml);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("scrape_element", _tool.Name);
        Assert.Equal("Web Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamUrl].Required);
        Assert.True(_tool.Schema.Parameters["css_selector"].Required);
        Assert.False(_tool.Schema.Parameters["extract_attributes"].Required);
        Assert.False(_tool.Schema.Parameters["include_html"].Required);
        Assert.False(_tool.Schema.Parameters["max_elements"].Required);
    }

    [Fact]
    public async Task ShouldReturnError_WhenUrlIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = "",
                ["css_selector"] = "div"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("URL", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Selector-engine contract (R1.4: Fizzler → AngleSharp, iso-behavior) ──

    [Fact]
    public async Task ShouldSupportChildCombinatorSelector_WhenUsingSchemaExample()
    {
        var html = @"<html><body>
            <div class='product'><h2>Direct child</h2></div>
            <div class='product'><section><h2>Nested grandchild</h2></section></div>
            <h2>Outside</h2>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "div.product > h2"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        // Only the direct <h2> child of div.product matches — not the nested one, not the outside one.
        Assert.Equal(1, dict["element_count"]);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        var element = Assert.Single(elements) as Dictionary<string, object?>;
        Assert.NotNull(element);
        Assert.Equal("Direct child", element["text"]);
    }

    [Fact]
    public async Task ShouldSupportAttributeSelector_WhenFilteringByAttributeValue()
    {
        var html = @"<html><body>
            <a href='https://a.example' data-kind='external'>External</a>
            <a href='/local' data-kind='internal'>Internal</a>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "a[data-kind='external']"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, dict["element_count"]);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        var element = Assert.Single(elements) as Dictionary<string, object?>;
        Assert.NotNull(element);
        Assert.Equal("External", element["text"]);
    }

    [Fact]
    public async Task ShouldSupportNthChildSelector_WhenTargetingPositionalElement()
    {
        var html = @"<html><body>
            <ul>
                <li>First</li>
                <li>Second</li>
                <li>Third</li>
            </ul>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "li:nth-child(2)"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, dict["element_count"]);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        var element = Assert.Single(elements) as Dictionary<string, object?>;
        Assert.NotNull(element);
        Assert.Equal("Second", element["text"]);
    }

    [Fact]
    public async Task ShouldPreserveDocumentOrder_WhenMultipleElementsMatch()
    {
        var html = @"<html><body>
            <p class='row'>Alpha</p>
            <div><p class='row'>Bravo</p></div>
            <p class='row'>Charlie</p>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "p.row"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        Assert.Equal(3, elements.Count);

        var texts = elements
            .Select(e => (e as Dictionary<string, object?>)!["text"]?.ToString())
            .ToList();
        Assert.Equal(ExpectedTexts, texts);

        var indices = elements
            .Select(e => Convert.ToInt32((e as Dictionary<string, object?>)!["index"], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(ExpectedIndices, indices);
    }

    [Fact]
    public async Task ShouldDecodeHtmlEntities_InExtractedText()
    {
        var html = @"<html><body>
            <p class='dish'>Fish &amp; Chips &lt;classic&gt;</p>
        </body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "p.dish"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var elements = dict["elements"] as List<object>;
        Assert.NotNull(elements);
        var element = Assert.Single(elements) as Dictionary<string, object?>;
        Assert.NotNull(element);
        // Entities must be decoded, matching the previous HtmlEntity.DeEntitize behavior.
        Assert.Equal("Fish & Chips <classic>", element["text"]);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCssSelectorIsInvalid()
    {
        var html = @"<html><body><p>content</p></body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["css_selector"] = "li[unclosed"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    private void SetupHttpResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _mockHandler.SetResponse(statusCode, content);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
