using Orkeon.Domain.Common;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Domain.Tests.Knowledge;

/// <summary>
/// Tests for IKnowledgeSource interface and KnowledgeContent class following Clean Architecture principles.
/// Tests knowledge source behavior and content management.
/// </summary>
public class IKnowledgeSourceTests
{
    private static readonly string[] AiTutorialTags = ["AI", "Tutorial"];
    private static readonly string[] CjkLanguages = ["zh", "ja", "ko", "ar"];
    #region Test Doubles

    /// <summary>
    /// Test implementation of IKnowledgeSource for testing purposes.
    /// </summary>
    private class TestKnowledgeSource : IKnowledgeSource
    {
        public KnowledgeSourceId Id { get; set; } = KnowledgeSourceId.Create();
        public string Name { get; set; } = "Test Knowledge Source";
        public string Type { get; set; } = "test";

        private readonly List<KnowledgeContent> _contents = [];
        private int _getContentCallCount;
        private int _searchCallCount;

        public int GetContentCallCount => _getContentCallCount;
        public int SearchCallCount => _searchCallCount;

        public TestKnowledgeSource()
        {
            // Initialize with some test content
            _contents.Add(new KnowledgeContent
            {
                Title = "Introduction to AI",
                Content = "Artificial Intelligence is a broad field of computer science...",
                Source = Id.ToString(),
                Metadata = new Dictionary<string, object> { { "category", "AI" }, { "level", "beginner" } }
            });

            _contents.Add(new KnowledgeContent
            {
                Title = "Machine Learning Basics",
                Content = "Machine Learning is a subset of AI that enables systems to learn...",
                Source = Id.ToString(),
                Metadata = new Dictionary<string, object> { { "category", "ML" }, { "level", "intermediate" } }
            });

            _contents.Add(new KnowledgeContent
            {
                Title = "Deep Learning Fundamentals",
                Content = "Deep Learning uses neural networks with multiple layers...",
                Source = Id.ToString(),
                Metadata = new Dictionary<string, object> { { "category", "DL" }, { "level", "advanced" } }
            });
        }

        public void AddContent(KnowledgeContent content)
        {
            _contents.Add(content);
        }

        public System.Threading.Tasks.Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default)
        {
            _getContentCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return System.Threading.Tasks.Task.FromResult(_contents.FirstOrDefault() ?? new KnowledgeContent());
        }

        public System.Threading.Tasks.Task<IEnumerable<KnowledgeContent>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            _searchCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            var results = _contents
                .Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           c.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .OrderByDescending(c => c.Relevance);

            return System.Threading.Tasks.Task.FromResult(results.AsEnumerable());
        }
    }

    /// <summary>
    /// Test implementation that throws exceptions for error testing.
    /// </summary>
    private class FaultyKnowledgeSource : IKnowledgeSource
    {
        public KnowledgeSourceId Id { get; } = KnowledgeSourceId.Create();
        public string Name => "Faulty Source";
        public string Type => "faulty";

        public System.Threading.Tasks.Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Source unavailable");
        }

        public System.Threading.Tasks.Task<IEnumerable<KnowledgeContent>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Search service down");
        }
    }

    /// <summary>
    /// Test implementation with delayed responses for timeout testing.
    /// </summary>
    private class SlowKnowledgeSource : IKnowledgeSource
    {
        public KnowledgeSourceId Id { get; } = KnowledgeSourceId.Create();
        public string Name => "Slow Source";
        public string Type => "slow";

        private readonly int _delayMilliseconds;

        public SlowKnowledgeSource(int delayMilliseconds = 1000)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public async System.Threading.Tasks.Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Delay(_delayMilliseconds, cancellationToken);
            return new KnowledgeContent { Content = "Delayed content" };
        }

        public async System.Threading.Tasks.Task<IEnumerable<KnowledgeContent>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Delay(_delayMilliseconds, cancellationToken);
            return [new KnowledgeContent { Content = "Delayed search result" }];
        }
    }

    #endregion

    #region KnowledgeContent Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingKnowledgeContentWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var content = new KnowledgeContent();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(content.Id);
        Assert.Equal(string.Empty, content.Title);
        Assert.Equal(string.Empty, content.Content);
        Assert.Equal(string.Empty, content.Source);
        Assert.NotNull(content.Metadata);
        Assert.Empty(content.Metadata);
        Assert.True(content.CreatedAt >= beforeCreation);
        Assert.True(content.CreatedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, content.CreatedAt.Kind);
        Assert.Equal(1.0, content.Relevance);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingKnowledgeContentProperties()
    {
        // Arrange
        var createdAt = DateTime.UtcNow.AddDays(-7);
        var contentId = KnowledgeContentId.Create();
        var metadata = new Dictionary<string, object>
        {
            { "author", "John Doe" },
            { "version", 2 },
            { "tags", AiTutorialTags }
        };

        // Act
        var content = new KnowledgeContent
        {
            Id = contentId,
            Title = "Advanced AI Concepts",
            Content = "This document covers advanced concepts in artificial intelligence...",
            Source = "knowledge-base-001",
            Metadata = metadata,
            CreatedAt = createdAt,
            Relevance = 0.95
        };

        // Assert
        Assert.Equal(contentId, content.Id);
        Assert.Equal("Advanced AI Concepts", content.Title);
        Assert.Equal("This document covers advanced concepts in artificial intelligence...", content.Content);
        Assert.Equal("knowledge-base-001", content.Source);
        Assert.Equal(metadata, content.Metadata);
        Assert.Equal(createdAt, content.CreatedAt);
        Assert.Equal(0.95, content.Relevance);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void ShouldAcceptVariousValues_WhenUsingKnowledgeContentRelevance(double relevance)
    {
        // Act
        var content = new KnowledgeContent { Relevance = relevance };

        // Assert
        Assert.Equal(relevance, content.Relevance);
    }

    #endregion

    #region IKnowledgeSource Implementation Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnContent_WhenGettingContentAsync()
    {
        // Arrange
        var source = new TestKnowledgeSource();

        // Act
        var content = await source.GetContentAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(content);
        Assert.NotNull(content.Id);
        Assert.Equal("Introduction to AI", content.Title);
        Assert.Equal(1, source.GetContentCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnResults_WhenSearchingAsyncWithMatchingQuery()
    {
        // Arrange
        var source = new TestKnowledgeSource();

        // Act
        var results = await source.SearchAsync("Learning", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var resultList = results.ToList();
        Assert.Equal(2, resultList.Count);
        Assert.Contains(resultList, c => c.Title.Contains("Machine Learning"));
        Assert.Contains(resultList, c => c.Title.Contains("Deep Learning"));
        Assert.Equal(1, source.SearchCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectLimit_WhenSearchingAsyncWithLimit()
    {
        // Arrange
        var source = new TestKnowledgeSource();

        // Act
        var results = await source.SearchAsync("Learning", limit: 1, TestContext.Current.CancellationToken);

        // Assert
        var resultList = results.ToList();
        Assert.Single(resultList);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmpty_WhenSearchingAsyncWithNoMatch()
    {
        // Arrange
        var source = new TestKnowledgeSource();

        // Act
        var results = await source.SearchAsync("Quantum Computing", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFindMatches_WhenSearchingAsyncCaseInsensitive()
    {
        // Arrange
        var source = new TestKnowledgeSource();

        // Act
        var results = await source.SearchAsync("MACHINE learning", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var resultList = results.ToList();
        Assert.Single(resultList);
        Assert.Contains("Machine Learning", resultList[0].Title);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenGettingContentAsyncWithCancellation()
    {
        // Arrange
        var source = new SlowKnowledgeSource(5000);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            async () => await source.GetContentAsync(cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenSearchingAsyncWithCancellation()
    {
        // Arrange
        var source = new SlowKnowledgeSource(5000);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            async () => await source.SearchAsync("test", cancellationToken: cts.Token));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenGettingContentAsyncWithFaultySource()
    {
        // Arrange
        var source = new FaultyKnowledgeSource();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.GetContentAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Source unavailable", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenSearchingAsyncWithFaultySource()
    {
        // Arrange
        var source = new FaultyKnowledgeSource();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.SearchAsync("test", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Search service down", exception.Message);
    }

    #endregion

    #region Knowledge Source Scenarios

    [Fact]
    public async System.Threading.Tasks.Task ShouldScenario_WhenUsingKnowledgeSourceDocumentRepository()
    {
        // Arrange
        var source = new TestKnowledgeSource
        {
            Name = "Company Documentation Repository",
            Type = "document"
        };

        // Add company documents
        source.AddContent(new KnowledgeContent
        {
            Title = "Company Security Policy",
            Content = "All employees must follow these security guidelines...",
            Source = source.Id.ToString(),
            Metadata = new Dictionary<string, object>
            {
                { "department", "IT" },
                { "lastUpdated", DateTime.UtcNow.AddDays(-30) },
                { "version", "2.1" }
            },
            Relevance = 0.9
        });

        // Act
        var searchResults = await source.SearchAsync("security", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var results = searchResults.ToList();
        Assert.Single(results);
        Assert.Equal("Company Security Policy", results[0].Title);
        Assert.Equal("IT", results[0].Metadata["department"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldScenario_WhenUsingKnowledgeSourceWikiIntegration()
    {
        // Arrange
        var source = new TestKnowledgeSource
        {
            Name = "Company Wiki",
            Type = "wiki"
        };

        // Add wiki articles
        var articles = new[]
        {
            ("Onboarding Guide", "New employee onboarding process..."),
            ("Development Standards", "Code quality and development standards..."),
            ("Architecture Patterns", "Recommended architecture patterns...")
        };

        foreach (var (title, content) in articles)
        {
            source.AddContent(new KnowledgeContent
            {
                Title = title,
                Content = content,
                Source = source.Id.ToString()
            });
        }

        // Act
        var allContent = await source.GetContentAsync(TestContext.Current.CancellationToken);
        var searchResults = await source.SearchAsync("standards", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(allContent);
        var resultList = searchResults.ToList();
        Assert.Single(resultList);
        Assert.Contains("Development Standards", resultList[0].Title);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldAccept_WhenUsingKnowledgeContentWithNullProperties()
    {
        // Act
        var content = new KnowledgeContent
        {
            Id = null!,
            Title = null!,
            Content = null!,
            Source = null!,
            Metadata = null!
        };

        // Assert - Id is a reference type so null is possible with null!
        Assert.Null(content.Id);
        Assert.Null(content.Title);
        Assert.Null(content.Content);
        Assert.Null(content.Source);
        Assert.Null(content.Metadata);
    }

    [Fact]
    public void ShouldHandle_WhenUsingKnowledgeContentWithUnicodeContent()
    {
        // Arrange & Act
        var content = new KnowledgeContent
        {
            Title = "多语言支持 🌍",
            Content = "支持中文、日本語、한국어、العربية、emoji 😊",
            Source = "international-kb",
            Metadata = new Dictionary<string, object>
            {
                { "languages", CjkLanguages },
                { "emoji", "✅" }
            }
        };

        // Assert
        Assert.Contains("🌍", content.Title);
        Assert.Contains("😊", content.Content);
        Assert.Contains("✅", content.Metadata["emoji"].ToString());
    }

    [Fact]
    public void ShouldHandle_WhenUsingKnowledgeContentWithVeryLargeContent()
    {
        // Arrange
        var largeContent = new string('A', 1_000_000); // 1MB of text

        // Act
        var content = new KnowledgeContent
        {
            Content = largeContent
        };

        // Assert
        Assert.Equal(1_000_000, content.Content.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenSearchingAsyncWithEmptyQuery()
    {
        // Arrange
        var source = new TestKnowledgeSource();

        // Act
        var results = await source.SearchAsync("", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenSearchingAsyncWithNegativeLimit()
    {
        // Arrange
        var source = new TestKnowledgeSource();

        // Act
        var results = await source.SearchAsync("test", limit: -1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results); // Take(-1) returns empty
    }

    #endregion

    #region Multiple Knowledge Sources

    [Fact]
    public async System.Threading.Tasks.Task ShouldIntegration_WhenUsingMultipleKnowledgeSources()
    {
        // Arrange
        var sources = new List<IKnowledgeSource>
        {
            new TestKnowledgeSource { Name = "Technical Docs", Type = "docs" },
            new TestKnowledgeSource { Name = "Blog Posts", Type = "blog" },
            new TestKnowledgeSource { Name = "Research Papers", Type = "research" }
        };

        // Act - Search across all sources
        var allResults = new List<KnowledgeContent>();
        foreach (var source in sources)
        {
            var results = await source.SearchAsync("AI", cancellationToken: TestContext.Current.CancellationToken);
            allResults.AddRange(results);
        }

        // Assert
        Assert.NotEmpty(allResults);
        Assert.Equal(6, allResults.Count); // Each test source has 2 AI-related content (title or content matches)
        Assert.All(allResults, r => Assert.True(
            r.Title.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
            r.Content.Contains("AI", StringComparison.OrdinalIgnoreCase)));
    }

    #endregion
}
