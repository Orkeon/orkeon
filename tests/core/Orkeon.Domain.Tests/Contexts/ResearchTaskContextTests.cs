using Orkeon.Domain.Task.Contexts;

namespace Orkeon.Domain.Tests.Contexts;

/// <summary>
/// Tests for ResearchTaskContext following Clean Architecture principles.
/// Tests the research task context classes and their behavior.
/// </summary>
public class ResearchTaskContextTests
{
    #region ResearchTaskContext Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingResearchTaskContextWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var context = new ResearchTaskContext();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(string.Empty, context.Topic);
        Assert.NotNull(context.Sources);
        Assert.Empty(context.Sources);
        Assert.NotNull(context.RelevanceScores);
        Assert.Empty(context.RelevanceScores);
        Assert.NotNull(context.KeyFindings);
        Assert.Empty(context.KeyFindings);
        Assert.True(context.ResearchStarted >= beforeCreation);
        Assert.True(context.ResearchStarted <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, context.ResearchStarted.Kind);
        Assert.NotNull(context.SearchQueries);
        Assert.Empty(context.SearchQueries);
        Assert.NotNull(context.References);
        Assert.Empty(context.References);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingResearchTaskContextUsingProperties()
    {
        // Arrange
        var sources = new List<string> { "Wikipedia", "Research Paper", "Journal Article" };
        var relevanceScores = new Dictionary<string, float> { { "Wikipedia", 0.8f }, { "Research Paper", 0.95f } };
        var keyFindings = new List<string> { "Finding 1", "Finding 2" };
        var researchStarted = DateTime.UtcNow.AddHours(-2);
        var searchQueries = new List<string> { "machine learning algorithms", "neural networks" };
        var references = new List<ResearchReference> { new ResearchReference { Title = "ML Overview" } };

        // Act
        var context = new ResearchTaskContext
        {
            Topic = "Machine Learning in Healthcare",
            Sources = sources,
            RelevanceScores = relevanceScores,
            KeyFindings = keyFindings,
            ResearchStarted = researchStarted,
            SearchQueries = searchQueries,
            References = references
        };

        // Assert
        Assert.Equal("Machine Learning in Healthcare", context.Topic);
        Assert.Equal(sources, context.Sources);
        Assert.Equal(relevanceScores, context.RelevanceScores);
        Assert.Equal(keyFindings, context.KeyFindings);
        Assert.Equal(researchStarted, context.ResearchStarted);
        Assert.Equal(searchQueries, context.SearchQueries);
        Assert.Equal(references, context.References);
    }

    #endregion

    #region AddSource Tests

    [Fact]
    public void ShouldAddToSourcesAndScores_WhenAddingSourceWithValidSourceAndDefaultRelevance()
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddSource("https://arxiv.org/paper123");
        context.AddSource("Nature Journal Article");
        context.AddSource("IEEE Conference Paper");

        // Assert
        Assert.Equal(3, context.Sources.Count);
        Assert.Equal(3, context.RelevanceScores.Count);
        Assert.Contains("https://arxiv.org/paper123", context.Sources);
        Assert.Equal(1.0f, context.RelevanceScores["https://arxiv.org/paper123"]);
        Assert.Equal(1.0f, context.RelevanceScores["Nature Journal Article"]);
    }

    [Fact]
    public void ShouldAddWithCorrectScore_WhenAddingSourceWithValidSourceAndSpecificRelevance()
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddSource("High relevance source", 0.95f);
        context.AddSource("Medium relevance source", 0.6f);
        context.AddSource("Low relevance source", 0.3f);

        // Assert
        Assert.Equal(3, context.Sources.Count);
        Assert.Equal(0.95f, context.RelevanceScores["High relevance source"]);
        Assert.Equal(0.6f, context.RelevanceScores["Medium relevance source"]);
        Assert.Equal(0.3f, context.RelevanceScores["Low relevance source"]);
    }

    [Theory]
    [InlineData(-0.5f, 0.0f)]
    [InlineData(1.5f, 1.0f)]
    [InlineData(2.0f, 1.0f)]
    [InlineData(-10.0f, 0.0f)]
    public void ShouldClampToValidRange_WhenAddingSourceWithOutOfRangeRelevance(float inputRelevance, float expectedRelevance)
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddSource("Test source", inputRelevance);

        // Assert
        Assert.Equal(expectedRelevance, context.RelevanceScores["Test source"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ShouldNotAdd_WhenAddingSourceWithInvalidSource(string invalidSource)
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddSource(invalidSource);

        // Assert
        Assert.Empty(context.Sources);
        Assert.Empty(context.RelevanceScores);
    }

    [Fact]
    public void ShouldAddToListButUpdateScore_WhenAddingSourceWithDuplicateSource()
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddSource("Research Paper A", 0.7f);
        context.AddSource("Research Paper A", 0.9f); // Same source, different relevance

        // Assert
        Assert.Equal(2, context.Sources.Count); // Both added to list
        Assert.Single(context.RelevanceScores); // Only one entry in dictionary
        Assert.Equal(0.9f, context.RelevanceScores["Research Paper A"]); // Latest score
    }

    #endregion

    #region AddKeyFinding Tests

    [Fact]
    public void ShouldAddToKeyFindings_WhenAddingKeyFindingWithValidFinding()
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddKeyFinding("AI models show 95% accuracy in diagnosis");
        context.AddKeyFinding("Implementation costs reduced by 40%");
        context.AddKeyFinding("Patient satisfaction improved significantly");

        // Assert
        Assert.Equal(3, context.KeyFindings.Count);
        Assert.Contains("AI models show 95% accuracy in diagnosis", context.KeyFindings);
        Assert.Contains("Implementation costs reduced by 40%", context.KeyFindings);
        Assert.Contains("Patient satisfaction improved significantly", context.KeyFindings);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ShouldNotAdd_WhenAddingKeyFindingWithInvalidFinding(string invalidFinding)
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddKeyFinding(invalidFinding);

        // Assert
        Assert.Empty(context.KeyFindings);
    }

    [Fact]
    public void ShouldAddAll_WhenAddingKeyFindingWithDuplicateFinding()
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        context.AddKeyFinding("Important discovery");
        context.AddKeyFinding("Important discovery"); // Duplicate

        // Assert
        Assert.Equal(2, context.KeyFindings.Count);
        Assert.Equal(2, context.KeyFindings.Count(f => f == "Important discovery"));
    }

    #endregion

    #region GetTopSources Tests

    [Fact]
    public void ShouldReturnSortedByRelevance_WhenGettingTopSourcesWithMultipleSources()
    {
        // Arrange
        var context = new ResearchTaskContext();
        context.AddSource("Low relevance", 0.3f);
        context.AddSource("High relevance", 0.9f);
        context.AddSource("Medium relevance", 0.6f);
        context.AddSource("Very high relevance", 0.95f);
        context.AddSource("Very low relevance", 0.1f);

        // Act
        var topSources = context.GetTopSources(3).ToList();

        // Assert
        Assert.Equal(3, topSources.Count);
        Assert.Equal("Very high relevance", topSources[0]);
        Assert.Equal("High relevance", topSources[1]);
        Assert.Equal("Medium relevance", topSources[2]);
    }

    [Fact]
    public void ShouldReturnTopFive_WhenGettingTopSourcesWithDefaultCount()
    {
        // Arrange
        var context = new ResearchTaskContext();
        for (int i = 1; i <= 10; i++)
        {
            context.AddSource($"Source {i}", i / 10.0f);
        }

        // Act
        var topSources = context.GetTopSources().ToList();

        // Assert
        Assert.Equal(5, topSources.Count);
        Assert.Equal("Source 10", topSources[0]); // Highest relevance (1.0)
        Assert.Equal("Source 9", topSources[1]);  // 0.9
        Assert.Equal("Source 8", topSources[2]);  // 0.8
        Assert.Equal("Source 7", topSources[3]);  // 0.7
        Assert.Equal("Source 6", topSources[4]);  // 0.6
    }

    [Fact]
    public void ShouldReturnAllSources_WhenGettingTopSourcesWithCountGreaterThanSources()
    {
        // Arrange
        var context = new ResearchTaskContext();
        context.AddSource("Source 1", 0.5f);
        context.AddSource("Source 2", 0.7f);
        context.AddSource("Source 3", 0.3f);

        // Act
        var topSources = context.GetTopSources(10).ToList();

        // Assert
        Assert.Equal(3, topSources.Count);
        Assert.Equal("Source 2", topSources[0]);
        Assert.Equal("Source 1", topSources[1]);
        Assert.Equal("Source 3", topSources[2]);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingTopSourcesWithZeroCount()
    {
        // Arrange
        var context = new ResearchTaskContext();
        context.AddSource("Source 1", 0.8f);
        context.AddSource("Source 2", 0.9f);

        // Act
        var topSources = context.GetTopSources(0).ToList();

        // Assert
        Assert.Empty(topSources);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingTopSourcesWithNoSources()
    {
        // Arrange
        var context = new ResearchTaskContext();

        // Act
        var topSources = context.GetTopSources().ToList();

        // Assert
        Assert.Empty(topSources);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenGettingTopSourcesWithEqualRelevanceScores()
    {
        // Arrange
        var context = new ResearchTaskContext();
        context.AddSource("First", 0.8f);
        context.AddSource("Second", 0.8f);
        context.AddSource("Third", 0.8f);

        // Act
        var topSources = context.GetTopSources().ToList();

        // Assert
        Assert.Equal(3, topSources.Count);
        // Order should be maintained when scores are equal
        Assert.Contains("First", topSources);
        Assert.Contains("Second", topSources);
        Assert.Contains("Third", topSources);
    }

    #endregion

    #region ResearchReference Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingResearchReferenceWithDefaultConstructor()
    {
        // Act
        var reference = new ResearchReference();

        // Assert
        Assert.Equal(string.Empty, reference.Title);
        Assert.Null(reference.Url);
        Assert.Equal(string.Empty, reference.Author);
        Assert.Null(reference.PublishedDate);
        Assert.Equal(string.Empty, reference.Summary);
        Assert.Equal(0.5f, reference.Relevance);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingResearchReferenceProperties()
    {
        // Arrange
        var publishedDate = DateTime.UtcNow.AddDays(-30);

        // Act
        var reference = new ResearchReference
        {
            Title = "Advanced Machine Learning Techniques",
            Url = new Uri("https://journal.com/article/12345"),
            Author = "Dr. Jane Smith",
            PublishedDate = publishedDate,
            Summary = "This paper explores novel approaches to deep learning...",
            Relevance = 0.85f
        };

        // Assert
        Assert.Equal("Advanced Machine Learning Techniques", reference.Title);
        Assert.Equal(new Uri("https://journal.com/article/12345"), reference.Url);
        Assert.Equal("Dr. Jane Smith", reference.Author);
        Assert.Equal(publishedDate, reference.PublishedDate);
        Assert.Equal("This paper explores novel approaches to deep learning...", reference.Summary);
        Assert.Equal(0.85f, reference.Relevance);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldCompleteResearchScenario_WhenUsingResearchTaskContext()
    {
        // Arrange
        var context = new ResearchTaskContext
        {
            Topic = "Quantum Computing Applications in Cryptography",
            ResearchStarted = DateTime.UtcNow.AddHours(-3)
        };

        // Add search queries
        context.AddSearchQuery("quantum computing cryptography");
        context.AddSearchQuery("post-quantum cryptography algorithms");
        context.AddSearchQuery("quantum resistant encryption");

        // Add sources with relevance
        context.AddSource("https://arxiv.org/quantum-crypto-2023", 0.95f);
        context.AddSource("MIT Quantum Computing Lab Report", 0.88f);
        context.AddSource("IEEE Quantum Journal Vol.5", 0.82f);
        context.AddSource("Wikipedia: Quantum Cryptography", 0.65f);
        context.AddSource("Blog: Intro to Quantum Computing", 0.4f);
        context.AddSource("NIST Post-Quantum Standards", 0.92f);

        // Add key findings
        context.AddKeyFinding("Shor's algorithm poses significant threat to RSA encryption");
        context.AddKeyFinding("Lattice-based cryptography shows promise for post-quantum security");
        context.AddKeyFinding("Current quantum computers lack sufficient qubits for practical attacks");
        context.AddKeyFinding("Timeline for quantum threat estimated at 10-15 years");

        // Add references
        context.AddReference(new ResearchReference
        {
            Title = "Post-Quantum Cryptography: Current State and Future Directions",
            Url = new Uri("https://arxiv.org/abs/2023.12345"),
            Author = "Dr. Alice Johnson et al.",
            PublishedDate = DateTime.UtcNow.AddMonths(-2),
            Summary = "Comprehensive review of post-quantum cryptographic algorithms",
            Relevance = 0.95f
        });

        context.AddReference(new ResearchReference
        {
            Title = "Quantum Computing: A Gentle Introduction",
            Url = new Uri("https://mitpress.mit.edu/quantum"),
            Author = "Eleanor Rieffel and Wolfgang Polak",
            PublishedDate = DateTime.UtcNow.AddYears(-2),
            Summary = "Textbook covering quantum computing fundamentals",
            Relevance = 0.7f
        });

        // Act - Get top sources
        var topSources = context.GetTopSources(3).ToList();

        // Assert - Verify complete research
        Assert.Equal("Quantum Computing Applications in Cryptography", context.Topic);
        Assert.Equal(3, context.SearchQueries.Count);
        Assert.Equal(6, context.Sources.Count);
        Assert.Equal(6, context.RelevanceScores.Count);
        Assert.Equal(4, context.KeyFindings.Count);
        Assert.Equal(2, context.References.Count);

        // Verify top sources are correctly ordered
        Assert.Equal("https://arxiv.org/quantum-crypto-2023", topSources[0]);
        Assert.Equal("NIST Post-Quantum Standards", topSources[1]);
        Assert.Equal("MIT Quantum Computing Lab Report", topSources[2]);

        // Verify references
        var highRelevanceRef = context.References.First(r => r.Relevance > 0.9f);
        Assert.Contains("Post-Quantum Cryptography", highRelevanceRef.Title);
        Assert.NotNull(highRelevanceRef.PublishedDate);
    }

    [Fact]
    public void ShouldAccumulateFindings_WhenUsingResearchTaskContextUsingMultiStageResearch()
    {
        // Arrange
        var context = new ResearchTaskContext { Topic = "Climate Change Impact on Agriculture" };

        // Stage 1: Initial research
        context.AddSearchQuery("climate change crop yields");
        context.AddSource("IPCC Report 2023", 0.98f);
        context.AddKeyFinding("Global crop yields expected to decline 10-25% by 2050");

        // Stage 2: Deep dive into specific regions
        context.AddSearchQuery("drought impact midwest farming");
        context.AddSource("USDA Climate Report", 0.85f);
        context.AddSource("Agricultural Journal - Drought Studies", 0.78f);
        context.AddKeyFinding("Midwest corn production most vulnerable to temperature increases");

        // Stage 3: Solutions research
        context.AddSearchQuery("climate resilient crops GMO");
        context.AddSource("Nature Biotechnology - GMO Solutions", 0.82f);
        context.AddKeyFinding("Drought-resistant GMO varieties show 30% better yields");

        // Act
        var allSources = context.Sources.Count;
        var topSolution = context.GetTopSources(1).First();

        // Assert
        Assert.Equal(4, allSources);
        Assert.Equal(3, context.KeyFindings.Count);
        Assert.Equal("IPCC Report 2023", topSolution); // Highest relevance
        Assert.All(context.SearchQueries, query => Assert.NotEmpty(query));
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResearchTaskContextWithNullCollections()
    {
        // Arrange & Act
        var context = new ResearchTaskContext
        {
            Sources = null!,
            RelevanceScores = null!,
            KeyFindings = null!,
            SearchQueries = null!,
            References = null!
        };

        // Assert
        Assert.Null(context.Sources);
        Assert.Null(context.RelevanceScores);
        Assert.Null(context.KeyFindings);
        Assert.Null(context.SearchQueries);
        Assert.Null(context.References);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResearchTaskContextWithUnicodeContent()
    {
        // Arrange
        var context = new ResearchTaskContext
        {
            Topic = "人工智能研究 🤖"
        };

        // Act
        context.AddSource("中文期刊：AI发展", 0.9f);
        context.AddKeyFinding("AI准确率达到95% 📊");
        context.AddSearchQuery("искусственный интеллект");

        var reference = new ResearchReference
        {
            Title = "AIの未来 🚀",
            Author = "山田太郎",
            Summary = "日本語の要約..."
        };
        context.AddReference(reference);

        // Assert
        Assert.Contains("🤖", context.Topic);
        Assert.Contains("中文期刊", context.Sources[0]);
        Assert.Contains("📊", context.KeyFindings[0]);
        Assert.Contains("интеллект", context.SearchQueries[0]);
        Assert.Contains("🚀", context.References[0].Title);
    }

    [Fact]
    public void ShouldAccept_WhenUsingResearchReferenceWithNullValues()
    {
        // Arrange
        var reference = new ResearchReference();

        // Act
        var nullRef = new ResearchReference
        {
            Title = null!,
            Url = null!,
            Author = null!,
            Summary = null!
        };

        // Assert
        Assert.Null(nullRef.Title);
        Assert.Null(nullRef.Url);
        Assert.Null(nullRef.Author);
        Assert.Null(nullRef.Summary);
    }

    [Fact]
    public void ShouldAccept_WhenUsingResearchReferenceWithExtremeRelevanceValues()
    {
        // Arrange
        var reference = new ResearchReference();

        // Act & Assert
        var negRef = new ResearchReference { Relevance = -1.0f };
        Assert.Equal(-1.0f, negRef.Relevance);

        var highRef = new ResearchReference { Relevance = 2.0f };
        Assert.Equal(2.0f, highRef.Relevance);
    }

    [Fact]
    public void ShouldAccept_WhenUsingResearchTaskContextWithPastResearchStarted()
    {
        // Arrange
        var pastDate = DateTime.UtcNow.AddYears(-5);

        // Act
        var context = new ResearchTaskContext { ResearchStarted = pastDate };

        // Assert
        Assert.Equal(pastDate, context.ResearchStarted);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingTopSourcesWithNegativeCount()
    {
        // Arrange
        var context = new ResearchTaskContext();
        context.AddSource("Source 1", 0.8f);

        // Act
        var topSources = context.GetTopSources(-5).ToList();

        // Assert
        Assert.Empty(topSources);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingResearchTaskContextToString()
    {
        // Arrange
        var context = new ResearchTaskContext { Topic = "AI Research" };

        // Act
        var stringRepresentation = context.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("ResearchTaskContext", stringRepresentation);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingResearchReferenceToString()
    {
        // Arrange
        var reference = new ResearchReference { Title = "Test Paper" };

        // Act
        var stringRepresentation = reference.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("ResearchReference", stringRepresentation);
    }

    #endregion
}
