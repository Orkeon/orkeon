using System.Collections.Immutable;
using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.DTOs.Common;

public class MiscDTOsTests
{
    // ══════════════ LlmResponseMetadata ══════════════

    [Fact]
    public void ShouldReturnEmptyInstance_WhenAccessingStaticEmpty()
    {
        // Act
        var meta = LlmResponseMetadata.Empty;

        // Assert
        Assert.Null(meta.Model);
        Assert.Null(meta.PromptTokens);
        Assert.Null(meta.CompletionTokens);
        Assert.Null(meta.TotalTokens);
        Assert.Null(meta.Latency);
        Assert.Null(meta.FinishReason);
        Assert.Null(meta.RequestId);
        Assert.Null(meta.ProviderExtensions);
    }

    [Fact]
    public void ShouldPopulateAllFields_WhenCreatingLlmResponseMetadata()
    {
        // Act
        var meta = new LlmResponseMetadata
        {
            Model = ModelGpt4,
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150,
            Latency = TimeSpan.FromMilliseconds(350),
            FinishReason = "stop",
            RequestId = "req-77",
            ProviderExtensions = new Dictionary<string, object> { ["system_fingerprint"] = "fp_1" }
        };

        // Assert
        Assert.Equal(ModelGpt4, meta.Model);
        Assert.Equal(100, meta.PromptTokens);
        Assert.Equal(50, meta.CompletionTokens);
        Assert.Equal(150, meta.TotalTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(350), meta.Latency);
        Assert.Equal("stop", meta.FinishReason);
        Assert.Equal("req-77", meta.RequestId);
        Assert.Single(meta.ProviderExtensions!);
    }
}
