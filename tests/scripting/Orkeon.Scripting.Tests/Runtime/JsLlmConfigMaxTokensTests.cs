using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class JsLlmConfigMaxTokensTests
{
    private static JsLlmConfig Seed() => new("openai", LlmConfig.Create("gpt-4o-mini"));

    private static JsLlmConfig With(string maxTokensSource)
    {
        using var engine = new Jint.Engine();
        var overrides = engine.Evaluate($"({{ maxTokens: {maxTokensSource} }})");
        return Seed().with(overrides);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public void ShouldKeepAPositiveMaxTokens_WhenWithIsCalled()
    {
        var updated = With("512");

        Assert.Equal(512, updated.maxTokens);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public void ShouldThrowNamingMaxTokens_WhenWithGetsZero()
    {
        var ex = Assert.Throws<InvalidScriptException>(() => With("0"));

        Assert.Contains("maxTokens", ex.Message);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public void ShouldThrowNamingMaxTokens_WhenWithGetsANonInteger()
    {
        var ex = Assert.Throws<InvalidScriptException>(() => With("1.5"));

        Assert.Contains("maxTokens", ex.Message);
    }

    [Theory]
    [Trait("BR", "CH-07")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("2147483648")]
    [InlineData("\"5\"")]
    [InlineData("true")]
    public void ShouldThrowNamingMaxTokens_WhenWithGetsAnOutOfDomainValue(string source)
    {
        var ex = Assert.Throws<InvalidScriptException>(() => With(source));

        Assert.Contains("maxTokens", ex.Message);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public void ShouldThrowNamingMaxTokens_WhenModelGetsZeroInOverrides()
    {
        using var engine = new Jint.Engine();
        var ns = new JsLlmNamespace(NullLogger.Instance);
        var name = engine.Evaluate("'gpt-4o-mini'");
        var overrides = engine.Evaluate("({ maxTokens: 0 })");

        var ex = Assert.Throws<InvalidScriptException>(() => ns.model(name, overrides));

        Assert.Contains("maxTokens", ex.Message);
    }
}
