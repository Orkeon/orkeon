using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// LLM-11: a failed call and an empty answer are two different things, and the response
/// says which through <see cref="LlmResponse.Error"/> — read from the metadata key every
/// provider writes its refusal under.
/// </summary>
public class LlmResponseErrorTests
{
    [Fact]
    public void Error_reads_the_providers_refusal_from_the_metadata()
    {
        var response = new LlmResponse
        {
            Content = "",
            Metadata = new Dictionary<string, object>
            {
                [LlmResponseMetadataKeys.Error] = "Kimi did not answer within Llm:TimeoutSeconds = 180 s",
                [LlmResponseMetadataKeys.ErrorType] = nameof(TaskCanceledException),
            },
        };

        Assert.Equal("Kimi did not answer within Llm:TimeoutSeconds = 180 s", response.Error);
        Assert.Equal(nameof(TaskCanceledException), response.ErrorType);
    }

    [Fact]
    public void An_empty_answer_is_not_a_failed_call()
    {
        Assert.Null(new LlmResponse { Content = "" }.Error);
        Assert.Null(new LlmResponse { Content = "" }.ErrorType);
        Assert.Null(new LlmResponse { Content = "an answer" }.Error);
    }

    [Fact]
    public void A_blank_error_value_counts_as_no_error()
    {
        var response = new LlmResponse
        {
            Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = "" },
        };

        Assert.Null(response.Error);
    }

    [Fact]
    public void The_keys_are_the_strings_the_providers_write()
    {
        // The Infrastructure metadata builder spells them as attribute literals; a drift
        // here would silently blind every reader.
        Assert.Equal("error", LlmResponseMetadataKeys.Error);
        Assert.Equal("error_type", LlmResponseMetadataKeys.ErrorType);
    }
}
