using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>#254: <c>LlmConfig.with({ cache })</c> requests the prompt cache a YAML <c>cache:</c> block does.</summary>
public sealed class JsLlmConfigCacheTests
{
    private static JsLlmConfig Seed(LlmCacheConfig? cache = null)
        => new("anthropic", LlmConfig.Create("claude-sonnet-5") with { Cache = cache });

    private static JsLlmConfig With(string cacheSource, LlmCacheConfig? seeded = null)
    {
        using var engine = new Jint.Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var overrides = engine.Evaluate($"({{ cache: {cacheSource} }})");
        return Seed(seeded).with(overrides);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldSetCache_WhenWithGetsCacheOptions()
    {
        var updated = With("""{ systemPrompt: true, tools: true, ttl: "5m" }""");

        Assert.Equal(new LlmCacheConfig { CacheSystemPrompt = true, CacheTools = true, Ttl = "5m" }, updated.Domain.Cache);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldKeepInheritedCache_WhenWithLeavesCacheOut()
    {
        var seeded = LlmCacheConfig.SystemPrompt();
        using var engine = new Jint.Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);

        var updated = Seed(seeded).with(engine.Evaluate("({ temperature: 0.2 })"));

        Assert.Equal(seeded, updated.Domain.Cache);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldTurnCacheOff_WhenWithGetsAnObjectMarkingNothing()
    {
        var updated = With("{ systemPrompt: false }", LlmCacheConfig.SystemPrompt());

        Assert.Null(updated.Domain.Cache);
    }

    [Theory]
    [Trait("BR", "CH-254")]
    [InlineData("""{ systemPrompt: true, ttl: "10m" }""", "5m or 1h")]
    [InlineData("""{ ttl: "1h" }""", "caches nothing on its own")]
    [InlineData("""{ prompt: true }""", "does not apply 'prompt'")]
    [InlineData("""{ systemPrompt: 1 }""", "systemPrompt must be true or false")]
    [InlineData("\"system\"", "an object")]
    [InlineData("[]", "an object")]
    public void ShouldThrowNamingCache_WhenWithGetsInvalidCacheOptions(string source, string expected)
    {
        var ex = Assert.Throws<InvalidScriptException>(() => With(source));

        Assert.Contains("LlmConfig.with({ cache })", ex.Message);
        Assert.Contains(expected, ex.Message);
    }
}
