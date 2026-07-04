using Orkeon.Application.Rag;

namespace Orkeon.Application.Tests.Common;

public class ResearchFindingsTests
{
    private static readonly string[] s_pointArray = ["point"];
    private static readonly string[] s_keyPoints = ["key1", "key2", "key3"];
    private static readonly string[] s_keys12 = ["key1", "key2"];
    private static readonly string[] s_keys123 = ["key1", "key2", "key3"];
    private static readonly string[] s_point1 = ["point1"];
    private static readonly string[] s_point2 = ["point2"];
    private static readonly string[] s_point = ["point"];
    private static readonly string[] s_old = ["old"];
    private static readonly string[] s_new = ["new"];
    private static readonly string[] s_modified = ["modified"];
    private static readonly string[] s_academicKeyPoints = ["Breakthrough in quantum computing", "Error rates reduced by 90%", "Commercial viability by 2026"];
    private static readonly string[] s_industryKeyPoints = ["Market growth of 35% YoY", "Major investments from tech giants"];
    private static readonly string[] s_aiKeyPoints = ["Point 1: AI advances", "Point 2: Machine learning", "Point 3: Neural networks"];

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var findings = ResearchFindings.Empty;

        // Assert
        Assert.NotNull(findings);
        Assert.Empty(findings.Sources);
        Assert.Equal(0, findings.Count);
        Assert.Empty(findings.ToDictionary());
    }

    [Fact]
    public void ShouldReturnFinding_WhenGettingWithExistingSource()
    {
        // Arrange
        var findings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("source1", 0.9f, s_keys12, "Summary 1")
            .Build();

        // Act
        var finding = findings.Get<SourceAnalysis>("source1");

        // Assert
        Assert.NotNull(finding);
        Assert.Equal(0.9f, finding.Relevance);
        Assert.Equal(2, finding.KeyPoints.Length);
        Assert.Equal("Summary 1", finding.Summary);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingWithNonExistentSource()
    {
        // Arrange
        var findings = ResearchFindings.Empty;

        // Act
        var result = findings.Get<SourceAnalysis>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingWithWrongType()
    {
        // Arrange
        var findings = ResearchFindings.CreateBuilder()
            .AddFinding("source1", new GenericFinding("test"))
            .Build();

        // Act
        var result = findings.Get<SourceAnalysis>("source1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnFinding_WhenGettingFindingWithExistingSource()
    {
        // Arrange
        var findings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("source1", 0.85f, s_point1, "Summary")
            .AddFinding("source2", new GenericFinding("generic data"))
            .Build();

        // Act
        var finding1 = findings.GetFinding("source1");
        var finding2 = findings.GetFinding("source2");

        // Assert
        Assert.NotNull(finding1);
        Assert.IsType<SourceAnalysis>(finding1);
        Assert.NotNull(finding2);
        Assert.IsType<GenericFinding>(finding2);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingFindingWithNonExistentSource()
    {
        // Arrange
        var findings = ResearchFindings.Empty;

        // Act
        var result = findings.GetFinding("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsSource()
    {
        // Arrange
        var findings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("existing", 0.5f, [], "")
            .Build();

        // Act & Assert
        Assert.True(findings.ContainsSource("existing"));
        Assert.False(findings.ContainsSource("nonexistent"));
    }

    [Fact]
    public void ShouldReturnAllSourceKeys_WhenUsingSources()
    {
        // Arrange
        var findings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("source1", 0.9f, s_point1, "Summary 1")
            .AddSourceAnalysis("source2", 0.8f, s_point2, "Summary 2")
            .AddFinding("source3", new GenericFinding("data"))
            .Build();

        // Act
        var sources = findings.Sources.ToList();

        // Assert
        Assert.Equal(3, sources.Count);
        Assert.Contains("source1", sources);
        Assert.Contains("source2", sources);
        Assert.Contains("source3", sources);
    }

    [Fact]
    public void ShouldReturnCorrectNumber_WhenCounting()
    {
        // Arrange
        var empty = ResearchFindings.Empty;
        var withFindings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("s1", 0.5f, [], "")
            .AddSourceAnalysis("s2", 0.6f, [], "")
            .AddFinding("s3", new GenericFinding("data"))
            .Build();

        // Act & Assert
        Assert.Equal(0, empty.Count);
        Assert.Equal(3, withFindings.Count);
    }

    [Fact]
    public void ShouldConvertAllFindings_WhenUsingToDictionary()
    {
        // Arrange
        var findings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("source1", 0.95f, s_keys12, "Summary")
            .AddFinding("source2", new GenericFinding(new { Type = "test", Value = 42 }))
            .Build();

        // Act
        var dictionary = findings.ToDictionary();

        // Assert
        Assert.Equal(2, dictionary.Count);

        // Check source1 conversion
        var source1 = dictionary["source1"];
        Assert.NotNull(source1);
        Assert.Contains("Summary", source1.ToString());

        // Check source2 conversion
        var source2 = dictionary["source2"];
        Assert.NotNull(source2);
    }

    [Fact]
    public void ShouldCreateEmptyBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = ResearchFindings.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
        var findings = builder.Build();
        Assert.Empty(findings.Sources);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingBuilderAddFindingWithNullFinding()
    {
        // Arrange
        var builder = ResearchFindings.CreateBuilder();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            builder.AddFinding("source", null!));
        Assert.Equal("finding", exception.ParamName);
    }

    [Fact]
    public void ShouldAddCorrectFinding_WhenUsingBuilderAddSourceAnalysis()
    {
        // Arrange & Act
        var findings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis(
                "academic_paper",
                0.92f,
                s_aiKeyPoints,
                "This paper discusses recent advances in AI and ML")
            .Build();

        // Assert
        var finding = findings.Get<SourceAnalysis>("academic_paper");
        Assert.NotNull(finding);
        Assert.Equal(0.92f, finding.Relevance);
        Assert.Equal(3, finding.KeyPoints.Length);
        Assert.Contains("Point 1: AI advances", finding.KeyPoints);
        Assert.Contains("This paper discusses", finding.Summary);
    }

    [Fact]
    public void ShouldUpdateFinding_WhenUsingBuilderOverwriteExistingSource()
    {
        // Arrange & Act
        var findings = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("source", 0.5f, s_old, "old summary")
            .AddSourceAnalysis("source", 0.9f, s_new, "new summary")
            .Build();

        // Assert
        var finding = findings.Get<SourceAnalysis>("source");
        Assert.NotNull(finding);
        Assert.Equal(0.9f, finding.Relevance);
        Assert.Contains("new", finding.KeyPoints);
        Assert.Equal("new summary", finding.Summary);
    }

    [Fact]
    public void ShouldCreateFindings_WhenUsingFromDictionaryWithValidDictionary()
    {
        // Arrange
        var sourceAnalysis = new SourceAnalysis(0.8f, s_pointArray, "summary");
        var genericFinding = new GenericFinding("test data");

        var dictionary = new Dictionary<string, object>
        {
            { "source1", sourceAnalysis },
            { "source2", genericFinding },
            { "source3", new { data = "anonymous object" } }
        };

        // Act
        var findings = ResearchFindings.FromDictionary(dictionary);

        // Assert
        Assert.Equal(3, findings.Count);
        Assert.True(findings.ContainsSource("source1"));
        Assert.True(findings.ContainsSource("source2"));
        Assert.True(findings.ContainsSource("source3"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNullDictionary()
    {
        // Act
        var findings = ResearchFindings.FromDictionary(null);

        // Assert
        Assert.Same(ResearchFindings.Empty, findings);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var findings = ResearchFindings.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Same(ResearchFindings.Empty, findings);
    }

    [Fact]
    public void ShouldCreateEmptyArray_WhenUsingSourceAnalysisConstructorWithNullKeyPoints()
    {
        // Act
        var analysis = new SourceAnalysis(0.5f, null!, "summary");

        // Assert
        // ImmutableArray is a value type, no need to check for null
        Assert.Empty(analysis.KeyPoints);
    }

    [Fact]
    public void ShouldCreateEmptyString_WhenUsingSourceAnalysisConstructorWithNullSummary()
    {
        // Act
        var analysis = new SourceAnalysis(0.5f, s_pointArray, null!);

        // Assert
        Assert.NotNull(analysis.Summary);
        Assert.Equal(string.Empty, analysis.Summary);
    }

    [Fact]
    public void ShouldReturnCorrectStructure_WhenUsingSourceAnalysisToObject()
    {
        // Arrange
        var analysis = new SourceAnalysis(
            0.85f,
            s_keyPoints,
            "Test summary");

        // Act
        var obj = analysis.ToObject();

        // Assert
        // Use reflection to access properties of anonymous type
        var type = obj.GetType();
        var relevance = type.GetProperty("Relevance")?.GetValue(obj);
        var keyPoints = type.GetProperty("KeyPoints")?.GetValue(obj) as string[];
        var summary = type.GetProperty("Summary")?.GetValue(obj);

        Assert.Equal(0.85f, relevance);
        Assert.NotNull(keyPoints);
        Assert.Equal(3, keyPoints!.Length);
        Assert.Equal("key1", keyPoints[0]);
        Assert.Equal("Test summary", summary);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingGenericFindingConstructorWithNullValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new GenericFinding(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingGenericFindingToObject()
    {
        // Arrange
        var originalValue = new { Type = "test", Data = "value", Number = 42 };
        var finding = new GenericFinding(originalValue);

        // Act
        var result = finding.ToObject();

        // Assert
        Assert.Same(originalValue, result);
    }

    [Fact]
    public void ShouldMultiSourceResearch_WhenUsingComplexScenario()
    {
        // Arrange - Simulate research from multiple sources
        var findings = ResearchFindings.CreateBuilder()
            // Academic source with high relevance
            .AddSourceAnalysis(
                "academic_journal_2024",
                0.95f,
                s_academicKeyPoints,
                "Recent advances in quantum computing show promising commercial applications")

            // Industry report with medium relevance
            .AddSourceAnalysis(
                "industry_report_q4",
                0.75f,
                s_industryKeyPoints,
                "Quantum computing market analysis for Q4 2024")

            // Generic news article
            .AddFinding(
                "tech_news",
                new GenericFinding(new
                {
                    title = "Quantum Computing Goes Mainstream",
                    date = "2024-01-15",
                    author = "Tech Reporter",
                    relevance = 0.6
                }))

            // Expert opinion
            .AddFinding(
                "expert_interview",
                new GenericFinding(new
                {
                    expert = "Dr. Jane Smith",
                    institution = "MIT",
                    quote = "We're at an inflection point",
                    confidence = "high"
                }))
            .Build();

        // Act - Access findings in different ways
        var academicSource = findings.Get<SourceAnalysis>("academic_journal_2024");
        var industrySource = findings.Get<SourceAnalysis>("industry_report_q4");
        var allSources = findings.Sources.ToList();
        var dictionary = findings.ToDictionary();

        // Assert
        Assert.Equal(4, findings.Count);

        // Verify academic source
        Assert.NotNull(academicSource);
        Assert.Equal(0.95f, academicSource.Relevance);
        Assert.Equal(3, academicSource.KeyPoints.Length);

        // Verify industry source
        Assert.NotNull(industrySource);
        Assert.Equal(0.75f, industrySource.Relevance);
        Assert.Contains(industrySource.KeyPoints, k => k.Contains("35% YoY"));

        // Verify all sources present
        Assert.Equal(4, allSources.Count);
        Assert.Contains("tech_news", allSources);
        Assert.Contains("expert_interview", allSources);

        // Verify dictionary conversion
        Assert.Equal(4, dictionary.Count);
        Assert.All(dictionary.Values, value => Assert.NotNull(value));
    }

    [Fact]
    public void ShouldMaintain_WhenUsingFindingsImmutability()
    {
        // Arrange
        var original = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("source1", 0.8f, s_point1, "summary1")
            .Build();

        // Act - Create new builder and add more findings
        var modified = ResearchFindings.CreateBuilder()
            .AddSourceAnalysis("source1", 0.9f, s_modified, "modified summary")
            .AddSourceAnalysis("source2", 0.7f, s_point2, "summary2")
            .Build();

        // Assert - Original should remain unchanged
        Assert.Equal(1, original.Count);
        Assert.Equal(0.8f, original.Get<SourceAnalysis>("source1")?.Relevance);

        // Modified should have new values
        Assert.Equal(2, modified.Count);
        Assert.Equal(0.9f, modified.Get<SourceAnalysis>("source1")?.Relevance);
        Assert.True(modified.ContainsSource("source2"));
    }
}
