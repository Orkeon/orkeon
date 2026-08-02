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
    /// <summary>The exact refusals observed in the campaigns of 2026-08-01.</summary>
    [Theory]
    [InlineData("""{"error":"\"llama3.2\" does not support thinking"}""", "thinking")]
    [InlineData("""{"error":{"message":"Multimodal data provided, but model does not support multimodal requests."}}""", "image input")]
    [InlineData("""{"error":{"code":"1210","message":"messages.content.type is invalid, allowed values: ['text']"}}""", "image input")]
    public void ShouldNameTheCapability_FromTheVendorsOwnWording(string vendorError, string expected)
    {
        var hint = CapabilityMismatchHint.ForVendorError(vendorError, "Ollama", "llama3.2");

        Assert.Contains($"declares {expected} support", hint, StringComparison.Ordinal);
        Assert.Contains("declared per provider while models differ", hint, StringComparison.Ordinal);
        Assert.Contains("'llama3.2'", hint, StringComparison.Ordinal);
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
        Assert.Empty(CapabilityMismatchHint.ForVendorError(vendorError, "Groq", "llama-3.3-70b-versatile"));
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
