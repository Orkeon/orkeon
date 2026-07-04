using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class JsLlmConfigResponseFormatTests
{
    [Fact]
    public void With_AppliesResponseFormat_WhenStringProvided()
    {
        // Arrange — start from an existing JsLlmConfig (any provider)
        var seed = new JsLlmConfig("openai", LlmConfig.Create("gpt-4o-mini"));
        using var engine = new Jint.Engine();
        var overrides = engine.Evaluate("({ responseFormat: 'json_object' })");

        // Act
        var updated = seed.with(overrides);

        // Assert — Domain.ResponseFormat carries the new value (consumed downstream by adapter)
        Assert.NotNull(updated.Domain.ResponseFormat);
        Assert.Equal("json_object", updated.Domain.ResponseFormat!.Type);
    }

    [Fact]
    public void With_KeepsExistingResponseFormat_WhenOverridesOmitIt()
    {
        var seed = new JsLlmConfig("openai", LlmConfig.Create("gpt-4o-mini") with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        });
        using var engine = new Jint.Engine();
        var overrides = engine.Evaluate("({ temperature: 0.1 })");

        var updated = seed.with(overrides);

        Assert.NotNull(updated.Domain.ResponseFormat);
        Assert.Equal("json_object", updated.Domain.ResponseFormat!.Type);
    }

    [Fact]
    public void With_ClearsResponseFormat_WhenStringIsText()
    {
        var seed = new JsLlmConfig("openai", LlmConfig.Create("gpt-4o-mini") with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        });
        using var engine = new Jint.Engine();
        var overrides = engine.Evaluate("({ responseFormat: 'text' })");

        var updated = seed.with(overrides);

        Assert.Null(updated.Domain.ResponseFormat); // 'text' = provider default, no need to ship
    }
}
