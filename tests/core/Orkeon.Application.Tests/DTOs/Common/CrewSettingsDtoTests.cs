using System.Text.Json;
using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Tests.DTOs.Common;

public class CrewSettingsDtoTests
{
    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        var dto = new CrewSettingsDto();

        Assert.False(dto.ShareCrew);
        Assert.False(dto.MemoryEnabled);
        Assert.True(dto.EnableMemorySharing);
        Assert.False(dto.CacheEnabled);
        Assert.False(dto.EnableOutputCaching);
        Assert.NotNull(dto.Language);
        Assert.NotNull(dto.CustomOptions);
        Assert.Null(dto.MaxRpm);
        Assert.Null(dto.MaxIterations);
        Assert.Null(dto.MaxExecutionTime);
        Assert.Null(dto.RetryConfig);
        Assert.Null(dto.TimeoutConfig);
        Assert.Null(dto.CallbackConfig);
    }

    [Fact]
    public void ShouldSerializeAndDeserialize_RoundTrip()
    {
        var dto = new CrewSettingsDto
        {
            MaxRpm = 60.0,
            ShareCrew = true,
            MaxIterations = 10,
            MemoryEnabled = true,
            CacheEnabled = true,
            Language = "en"
        };

        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<CrewSettingsDto>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(dto.MaxRpm, deserialized.MaxRpm);
        Assert.Equal(dto.ShareCrew, deserialized.ShareCrew);
        Assert.Equal(dto.MaxIterations, deserialized.MaxIterations);
        Assert.Equal(dto.MemoryEnabled, deserialized.MemoryEnabled);
        Assert.Equal(dto.CacheEnabled, deserialized.CacheEnabled);
        Assert.Equal(dto.Language, deserialized.Language);
    }

    [Fact]
    public void ShouldSerializeWithSnakeCasePropertyNames()
    {
        var dto = new CrewSettingsDto { MaxRpm = 30.0, ShareCrew = true };

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("\"max_rpm\"", json);
        Assert.Contains("\"share_crew\"", json);
    }

    [Fact]
    public void RetryConfig_ShouldSerializeCorrectly()
    {
        var dto = new CrewSettingsDto
        {
            RetryConfig = new RetryConfigDto
            {
                MaxAttempts = 5,
                UseExponentialBackoff = true,
                BackoffMultiplier = 3.0
            }
        };

        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<CrewSettingsDto>(json);

        Assert.NotNull(deserialized?.RetryConfig);
        Assert.Equal(5, deserialized.RetryConfig.MaxAttempts);
        Assert.True(deserialized.RetryConfig.UseExponentialBackoff);
        Assert.Equal(3.0, deserialized.RetryConfig.BackoffMultiplier);
    }

    [Fact]
    public void TimeoutConfig_ShouldSerializeCorrectly()
    {
        var dto = new CrewSettingsDto
        {
            TimeoutConfig = new TimeoutConfigDto
            {
                LlmTimeout = TimeSpan.FromSeconds(30),
                ToolTimeout = TimeSpan.FromSeconds(10)
            }
        };

        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<CrewSettingsDto>(json);

        Assert.NotNull(deserialized?.TimeoutConfig);
        Assert.Equal(TimeSpan.FromSeconds(30), deserialized.TimeoutConfig.LlmTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), deserialized.TimeoutConfig.ToolTimeout);
    }

    [Fact]
    public void CallbackConfig_DefaultValues_ShouldBeCorrect()
    {
        var dto = new CallbackConfigDto();

        Assert.False(dto.NotifyOnCompletion);
        Assert.True(dto.NotifyOnFailure);
        Assert.Null(dto.WebhookUrl);
        Assert.NotNull(dto.CustomSettings);
    }

    [Fact]
    public void MemoryConfig_ShouldSerializeWithDefaults()
    {
        var dto = new CrewSettingsDto
        {
            MemoryConfig = new MemoryConfigDto()
        };

        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<CrewSettingsDto>(json);

        Assert.NotNull(deserialized?.MemoryConfig);
        Assert.NotNull(deserialized.MemoryConfig.Provider);
    }

    [Fact]
    public void VectorConfig_DefaultValues_ShouldBeCorrect()
    {
        var dto = new VectorConfigDto();

        Assert.True(dto.Dimension > 0);
        Assert.Equal("cosine", dto.SimilarityMetric);
        Assert.True(dto.MinSimilarity >= 0);
        Assert.Equal(10, dto.MaxResults);
    }

    [Fact]
    public void MemoryLimitsDto_ShouldSerializeAllNulls()
    {
        var dto = new MemoryLimitsDto();

        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<MemoryLimitsDto>(json);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.MaxShortTermItems);
        Assert.Null(deserialized.MaxLongTermItems);
        Assert.Null(deserialized.MaxEpisodicItems);
        Assert.Null(deserialized.MaxMemorySize);
    }

    [Fact]
    public void MemoryCleanupDto_DefaultValues_ShouldBeCorrect()
    {
        var dto = new MemoryCleanupDto();

        Assert.False(dto.AutoCleanup);
        Assert.Null(dto.CleanupInterval);
        Assert.Null(dto.RetentionPeriod);
        Assert.Null(dto.MinRelevanceScore);
    }
}
