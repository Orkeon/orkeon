using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Fallback;

/// <summary>FWK final review L5: a vendor's own error label classes the failure like the status it stands for.</summary>
public sealed class LlmFailureClassifierVendorLabelTests
{
    private static LlmResponse Labelled(string label) =>
        FakeScriptedLlmProvider.Failed($"Anthropic API error: {label} - the stream failed", "APIError");

    [Theory]
    [InlineData("overloaded_error", "ServerError")]
    [InlineData("server_error", "ServerError")]
    [InlineData("api_error", "ServerError")]
    [InlineData("rate_limit_error", "RateLimited")]
    [InlineData("rate_limit_exceeded", "RateLimited")]
    [InlineData("invalid_api_key", "Refused")]
    [InlineData("authentication_error", "Refused")]
    [InlineData("permission_error", "Refused")]
    public void An_in_band_vendor_label_is_classed_like_its_status(string label, string expectedName)
    {
        Assert.Equal(Enum.Parse<LlmFailureKind>(expectedName), LlmFailureClassifier.Classify(Labelled(label)));
    }

    [Theory]
    [InlineData("overloaded_error", "ServerError")]
    [InlineData("rate_limit_exceeded", "RateLimited")]
    [InlineData("api_error", "ServerError")]
    [InlineData("invalid_api_key", "Refused")]
    [InlineData("authentication_error", "Refused")]
    [InlineData("permission_error", "Refused")]
    public void A_mid_stream_rejection_without_a_status_is_classed_by_its_label(string label, string expectedName)
    {
        var rejection = new HttpRequestException($"X API error: {label} - nope", inner: null, statusCode: null);

        Assert.Equal(Enum.Parse<LlmFailureKind>(expectedName), LlmFailureClassifier.Classify(rejection));
    }

    [Fact]
    public void A_rejection_without_status_or_known_label_stays_a_transport_failure()
    {
        Assert.Equal(LlmFailureKind.Transport, LlmFailureClassifier.Classify(new HttpRequestException("connection reset")));
        Assert.Equal(LlmFailureKind.Transport, LlmFailureClassifier.Classify(new HttpRequestException("X API error: mid-stream - cut")));
    }

    [Theory]
    [InlineData("overloaded_error", "ServerError")]
    [InlineData("rate_limit_error", "RateLimited")]
    [InlineData("invalid_api_key", "Refused")]
    public void The_exception_of_a_labelled_answer_classes_as_the_answer_did(string label, string expectedName)
    {
        var exception = LlmFailureClassifier.ToException(Labelled(label));

        Assert.Equal(Enum.Parse<LlmFailureKind>(expectedName), LlmFailureClassifier.Classify(exception));
    }

    [Fact]
    public void An_overloaded_answer_moves_a_text_call_to_the_next_profile_class()
    {
        Assert.True(LlmFailureClassifier.FailsOver(LlmFailureClassifier.Classify(Labelled("overloaded_error"))));
        Assert.False(LlmFailureClassifier.FailsOver(LlmFailureClassifier.Classify(Labelled("invalid_api_key"))));
    }
}
