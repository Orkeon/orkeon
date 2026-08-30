using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// Attribution of a "this model cannot do that" refusal (D-03).
/// </summary>
/// <remarks>
/// Capabilities are declared per provider, support is a property of the model. When the two
/// disagree the vendor refuses, accurately, and says nothing about why Orkeon asked — so the
/// framework takes the blame. These pin the sentence that fixes the attribution, and just as
/// importantly the silence everywhere else: a hint on an unrelated error would send readers
/// chasing a capability problem that does not exist.
/// </remarks>
public class CapabilityMismatchHintTests
{
    /// <summary>
    /// The exact refusals observed in the campaigns of 2026-08-01 and 2026-08-02. Every entry is
    /// a string a real vendor really sent — inventing plausible wordings here would test nothing.
    /// </summary>
    [Theory]
    [InlineData("""{"error":"\"llama3.2\" does not support thinking"}""", "thinking")]
    [InlineData("""{"error":{"message":"Multimodal data provided, but model does not support multimodal requests."}}""", "image input")]
    [InlineData("""{"error":{"code":"1210","message":"messages.content.type is invalid, allowed values: ['text']"}}""", "image input")]
    // llava campaign, 2026-08-02: this one reached the report unattributed while the thinking
    // refusal beside it was named, because the table had never been asked about tools.
    [InlineData("""{"error":"registry.ollama.ai/library/llava:latest does not support tools"}""", "tool calling")]
    [InlineData("""{"error":{"message":"This model does not support function calling."}}""", "tool calling")]
    public void ShouldNameTheCapability_FromTheVendorsOwnWording(string vendorError, string expected)
    {
        var hint = CapabilityMismatchHint.ForVendorError(vendorError, "Ollama", "llama3.2");

        Assert.Contains($"declares {expected} support", hint, StringComparison.Ordinal);
        Assert.Contains("declared per provider while models differ", hint, StringComparison.Ordinal);
        Assert.Contains("'llama3.2'", hint, StringComparison.Ordinal);
    }

    /// <summary>
    /// The OpenAI campaign of 2026-08-30: `gpt-5.6-sol` reasons by default, and on
    /// /v1/chat/completions the server refuses function tools unless `reasoning_effort` is
    /// explicitly `"none"`. Orkeon sent no reasoning_effort at all — the server default is
    /// what collides — so the generic "drop the option" sentence would name an option the
    /// request never carried. The remedy is Orkeon's own knob, and the hint must say it.
    /// </summary>
    [Fact]
    public void ShouldNameTheThinkingKnob_WhenAReasoningModelRefusesFunctionTools()
    {
        const string VendorError =
            """{"error":{"message":"Function tools with reasoning_effort are not supported for gpt-5.6-sol in /v1/chat/completions. To use function tools, use /v1/responses or set reasoning_effort to 'none'.","param":"reasoning_effort"}}""";

        var hint = CapabilityMismatchHint.ForVendorError(VendorError, "OpenAI", "gpt-5.6-sol");

        Assert.Contains("effort", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("none", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thinking", hint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A remedy, not just a diagnosis — a hint with no way forward is noise.</summary>
    [Fact]
    public void ShouldOfferAWayForward()
    {
        var hint = CapabilityMismatchHint.ForVendorError(
            "model does not support vision", "Kimi", "kimi-k2.6");

        Assert.Contains("Pick a model that does", hint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"error":{"message":"rate limit exceeded"}}""")]
    [InlineData("""{"error":{"message":"Unknown Model, please check the model code."}}""")]
    [InlineData("""{"error":{"message":"invalid api key"}}""")]
    [InlineData("")]
    [InlineData(null)]
    public void ShouldStaySilent_WhenTheErrorIsAboutSomethingElse(string? vendorError)
    {
        Assert.Empty(CapabilityMismatchHint.ForVendorError(vendorError, "Grok", "grok-4.6"));
    }

    /// <summary>Vendors vary their casing; the signature must not.</summary>
    [Fact]
    public void ShouldMatchRegardlessOfCase()
    {
        var hint = CapabilityMismatchHint.ForVendorError(
            "Model Does Not Support Multimodal requests", "Ollama", "llama3.2");

        Assert.NotEmpty(hint);
    }

    /// <summary>An unknown model still yields a usable sentence rather than a dangling quote.</summary>
    [Fact]
    public void ShouldFallBackToAGenericSubject_WhenTheModelIsUnknown()
    {
        var hint = CapabilityMismatchHint.ForVendorError("does not support thinking", "Ollama", model: null);

        Assert.Contains("this model does not have it", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("''", hint, StringComparison.Ordinal);
    }
}
