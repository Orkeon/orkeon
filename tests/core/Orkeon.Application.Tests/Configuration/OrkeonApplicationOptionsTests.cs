using Orkeon.Application.Configuration;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Configuration;

public class OrkeonApplicationOptionsTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var options = new OrkeonApplicationOptions();

        // Assert
        Assert.Equal(384, options.EmbeddingDimension);
        Assert.Equal("InMemory", options.DefaultMemoryProvider);
        Assert.False(options.EnableDebugLogging);
        Assert.Equal(TimeoutStandard, options.DefaultTimeout);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEmbeddingDimension()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.EmbeddingDimension = 768;

        // Assert
        Assert.Equal(768, options.EmbeddingDimension);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDefaultMemoryProvider()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.DefaultMemoryProvider = "Redis";

        // Assert
        Assert.Equal("Redis", options.DefaultMemoryProvider);
    }

    [Fact]
    public void ShouldBeSettable_WhenEnablingDebugLogging()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.EnableDebugLogging = true;

        // Assert
        Assert.True(options.EnableDebugLogging);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDefaultTimeout()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.DefaultTimeout = TimeoutExtended;

        // Assert
        Assert.Equal(TimeoutExtended, options.DefaultTimeout);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEmbeddingDimensionWithZeroValue()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.EmbeddingDimension = 0;

        // Assert
        Assert.Equal(0, options.EmbeddingDimension);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEmbeddingDimensionWithNegativeValue()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.EmbeddingDimension = -100;

        // Assert
        Assert.Equal(-100, options.EmbeddingDimension);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingEmbeddingDimensionWithCommonValues()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();
        var commonDimensions = new[] { 128, 256, 384, 512, 768, 1024, 1536 };

        foreach (var dimension in commonDimensions)
        {
            // Act
            options.EmbeddingDimension = dimension;

            // Assert
            Assert.Equal(dimension, options.EmbeddingDimension);
        }
    }

    [Fact]
    public void ShouldBeValid_WhenUsingDefaultMemoryProviderWithDifferentProviders()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();
        var providers = new[] { "InMemory", "Redis", "SQLite", "ChromaDB", "Pinecone" };

        foreach (var provider in providers)
        {
            // Act
            options.DefaultMemoryProvider = provider;

            // Assert
            Assert.Equal(provider, options.DefaultMemoryProvider);
        }
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingDefaultMemoryProviderWithNullValue()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.DefaultMemoryProvider = null!;

        // Assert
        Assert.Null(options.DefaultMemoryProvider);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingDefaultMemoryProviderWithEmptyString()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.DefaultMemoryProvider = string.Empty;

        // Assert
        Assert.Equal(string.Empty, options.DefaultMemoryProvider);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingDefaultTimeoutWithZeroTimeout()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.DefaultTimeout = TimeSpan.Zero;

        // Assert
        Assert.Equal(TimeSpan.Zero, options.DefaultTimeout);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingDefaultTimeoutWithNegativeTimeout()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.DefaultTimeout = TimeSpan.FromSeconds(-30);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(-30), options.DefaultTimeout);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingDefaultTimeoutWithCommonValues()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();
        var commonTimeouts = new[]
        {
            TimeoutQuick,
            TimeSpan.FromMinutes(1),
            TimeoutStandard,
            TimeoutExtended,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1)
        };

        foreach (var timeout in commonTimeouts)
        {
            // Act
            options.DefaultTimeout = timeout;

            // Assert
            Assert.Equal(timeout, options.DefaultTimeout);
        }
    }

    [Fact]
    public void ShouldBeIndependent_WhenAccessingProperties()
    {
        // Arrange
        var options = new OrkeonApplicationOptions();

        // Act
        options.EmbeddingDimension = 512;
        options.DefaultMemoryProvider = "Redis";
        options.EnableDebugLogging = true;
        options.DefaultTimeout = TimeoutLong;

        // Assert
        Assert.Equal(512, options.EmbeddingDimension);
        Assert.Equal("Redis", options.DefaultMemoryProvider);
        Assert.True(options.EnableDebugLogging);
        Assert.Equal(TimeoutLong, options.DefaultTimeout);
    }

    [Fact]
    public void ShouldSupportMultipleInstances_WhenUsingOptions()
    {
        // Arrange & Act
        var options1 = new OrkeonApplicationOptions
        {
            EmbeddingDimension = 256,
            DefaultMemoryProvider = "SQLite",
            EnableDebugLogging = true,
            DefaultTimeout = TimeSpan.FromMinutes(3)
        };

        var options2 = new OrkeonApplicationOptions
        {
            EmbeddingDimension = 768,
            DefaultMemoryProvider = "ChromaDB",
            EnableDebugLogging = false,
            DefaultTimeout = TimeSpan.FromMinutes(8)
        };

        // Assert
        Assert.NotEqual(options1.EmbeddingDimension, options2.EmbeddingDimension);
        Assert.NotEqual(options1.DefaultMemoryProvider, options2.DefaultMemoryProvider);
        Assert.NotEqual(options1.EnableDebugLogging, options2.EnableDebugLogging);
        Assert.NotEqual(options1.DefaultTimeout, options2.DefaultTimeout);
    }

    // ── Remaining property defaults ──

    [Fact]
    public void ShouldSetAllDefaults_WhenConstructingFreshInstance()
    {
        // Act
        var opt = new OrkeonApplicationOptions();

        // Assert
        Assert.Equal(100, opt.MaxShortTermMemoryItems);
        Assert.Equal(RepositoryType.Yaml, opt.CrewRepositoryType);
        Assert.Equal("crews", opt.CrewsPath);
        Assert.True(opt.EnablePersistence);
        Assert.Equal(15, opt.DefaultMaxIterations);
        Assert.False(opt.EnableRAG);
        Assert.Equal("orkeon_memory.db", opt.MemoryDatabasePath);
        Assert.Equal(384, opt.EmbeddingDimension);
        Assert.False(opt.EnableFlowPersistence);
        Assert.True(opt.EnablePlanning);
        Assert.Equal(ModelGpt4oMini, opt.PlanningLlmModel);
        Assert.Equal("Simple", opt.EmbeddingProvider);
        Assert.Null(opt.OpenAIApiKey);
        Assert.Equal("text-embedding-ada-002", opt.OpenAIEmbeddingModel);
        Assert.Null(opt.AzureOpenAIEndpoint);
        Assert.Null(opt.AzureOpenAIDeploymentName);
        Assert.Equal("InMemory", opt.DefaultMemoryProvider);
        Assert.False(opt.EnableDebugLogging);
        Assert.Equal(TimeoutStandard, opt.DefaultTimeout);
    }

    [Theory]
    [InlineData(RepositoryType.Yaml)]
    [InlineData(RepositoryType.Json)]
    public void ShouldAcceptRepositoryType_WhenSetting(RepositoryType repoType)
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.CrewRepositoryType = repoType;

        // Assert
        Assert.Equal(repoType, opt.CrewRepositoryType);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEnableRAG()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.EnableRAG = true;

        // Assert
        Assert.True(opt.EnableRAG);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEnableFlowPersistence()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.EnableFlowPersistence = true;

        // Assert
        Assert.True(opt.EnableFlowPersistence);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingPlanningLlmModel()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.PlanningLlmModel = "claude-3-opus";

        // Assert
        Assert.Equal("claude-3-opus", opt.PlanningLlmModel);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAzureOpenAIEndpoint()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();
        var uri = new Uri("https://myresource.openai.azure.com/");

        // Act
        opt.AzureOpenAIEndpoint = uri;

        // Assert
        Assert.Equal(uri, opt.AzureOpenAIEndpoint);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingOpenAIApiKey()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.OpenAIApiKey = "sk-test-key";

        // Assert
        Assert.Equal("sk-test-key", opt.OpenAIApiKey);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingMaxShortTermMemoryItems()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.MaxShortTermMemoryItems = 500;

        // Assert
        Assert.Equal(500, opt.MaxShortTermMemoryItems);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingMemoryDatabasePath()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.MemoryDatabasePath = "/data/memory.db";

        // Assert
        Assert.Equal("/data/memory.db", opt.MemoryDatabasePath);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEmbeddingProvider()
    {
        // Arrange
        var opt = new OrkeonApplicationOptions();

        // Act
        opt.EmbeddingProvider = "OpenAI";

        // Assert
        Assert.Equal("OpenAI", opt.EmbeddingProvider);
    }
}
