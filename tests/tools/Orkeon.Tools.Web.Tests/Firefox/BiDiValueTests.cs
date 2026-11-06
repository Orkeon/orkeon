using System.Text.Json.Nodes;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class BiDiValueTests
{
    [Theory]
    [InlineData("""{"type":"string","value":"plain text"}""", "plain text")]
    [InlineData("""{"type":"undefined"}""", "undefined")]
    [InlineData("""{"type":"null"}""", "null")]
    [InlineData("""{"type":"number","value":42}""", "42")]
    [InlineData("""{"type":"number","value":"NaN"}""", "\"NaN\"")]
    [InlineData("""{"type":"boolean","value":true}""", "true")]
    [InlineData("""{"type":"bigint","value":"12345678901234567890"}""", "\"12345678901234567890\"")]
    [InlineData("""{"type":"array","value":[{"type":"number","value":1},{"type":"string","value":"a"},{"type":"undefined"}]}""", """[1,"a",null]""")]
    [InlineData("""{"type":"object","value":[["a",{"type":"number","value":1}],["b",{"type":"array","value":[]}]]}""", """{"a":1,"b":[]}""")]
    [InlineData("""{"type":"map","value":[[{"type":"number","value":7},{"type":"string","value":"x"}]]}""", """{"7":"x"}""")]
    [InlineData("""{"type":"object"}""", "\"[object]\"")]
    [InlineData("""{"type":"regexp","value":{"pattern":"a+","flags":"g"}}""", "\"/a+/g\"")]
    [InlineData("""{"type":"array","value":[{"type":"string","value":"caf\u00E9 <b> & 1+1"}]}""", "[\"caf\u00E9 <b> & 1+1\"]")]
    [InlineData("""{"type":"node","sharedId":"n1"}""", "\"[node]\"")]
    [InlineData("""{"type":"function"}""", "\"[function]\"")]
    public void Should_RenderAScriptValueAsTheTextAnAgentReads(string remote, string expected)
    {
        Assert.Equal(expected, BiDiValue.Render(JsonNode.Parse(remote)));
    }

    [Fact]
    public void Should_RenderNothing_When_TheBrowserDescribesNoValue()
    {
        Assert.Equal(string.Empty, BiDiValue.Render(null));
    }
}
