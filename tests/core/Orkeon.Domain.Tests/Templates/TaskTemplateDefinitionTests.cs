using Orkeon.Domain.Task;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
namespace Orkeon.Domain.Tests.Templates;

/// <summary>
/// Tests for TaskTemplateDefinition following Clean Architecture principles.
/// Tests the task template definition model and its behavior.
/// </summary>
public class TaskTemplateDefinitionTests
{
    private static readonly string[] DefaultMarkets = ["US", "EU", "Asia"];
    private static readonly string[] RequiredSchemaFields = ["summary", "trends"];
    private static readonly string[] SegmentFields = ["age", "location", "purchase_history"];
    private static readonly string[] ReportSections = ["executive_summary", "methodology", "findings", "recommendations"];
    private static readonly string[] IssueCategoriesArray = ["critical", "major", "minor", "suggestions"];
    private static readonly string[] RequiredResultsField = ["results"];
    private static readonly string[] DataSources = ["database1", "api2", "file3"];

    private static readonly string[] s_dependencies123 = ["task1", "task2", "task3"];

    #region Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingDefaultConstructor()
    {
        // Act
        var template = new TaskTemplateDefinition();

        // Assert
        Assert.NotNull(template.Id);
        Assert.NotNull(template.Id);
        Assert.NotNull(template.Id); // ID is ULID-based EntityId
        Assert.Equal(string.Empty, template.Name);
        Assert.Equal(string.Empty, template.Description);
        Assert.Equal(string.Empty, template.ExpectedOutput);
        Assert.Null(template.AgentRole);
        Assert.NotNull(template.RequiredTools);
        Assert.Empty(template.RequiredTools);
        Assert.NotNull(template.RequiredSkills);
        Assert.Empty(template.RequiredSkills);
        Assert.NotNull(template.DefaultParameters);
        Assert.Empty(template.DefaultParameters);
        Assert.NotNull(template.Dependencies);
        Assert.Empty(template.Dependencies);
        Assert.Null(template.EstimatedDuration);
        Assert.NotNull(template.OutputSchema);
        Assert.Empty(template.OutputSchema);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingMultipleInstances()
    {
        // Act
        var template1 = new TaskTemplateDefinition();
        var template2 = new TaskTemplateDefinition();
        var template3 = new TaskTemplateDefinition();

        // Assert
        Assert.NotEqual(template1.Id, template2.Id);
        Assert.NotEqual(template2.Id, template3.Id);
        Assert.NotEqual(template1.Id, template3.Id);
        Assert.All([template1.Id, template2.Id, template3.Id],
            id => Assert.NotNull(id));
    }

    #endregion

    #region Property Tests

    [Fact]
    public void ShouldBeSettable_WhenUsingId()
    {
        // Arrange
        var newId = TaskTemplateId.Create();

        // Act
        var template = new TaskTemplateDefinition { Id = newId };

        // Assert
        Assert.Equal(newId, template.Id);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingName()
    {
        // Act
        var template = new TaskTemplateDefinition { Name = "Market Analysis Task Template" };

        // Assert
        Assert.Equal("Market Analysis Task Template", template.Name);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDescription()
    {
        // Act
        var template = new TaskTemplateDefinition { Description = "Analyze market trends and provide comprehensive insights" };

        // Assert
        Assert.Equal("Analyze market trends and provide comprehensive insights", template.Description);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingExpectedOutput()
    {
        // Act
        var template = new TaskTemplateDefinition { ExpectedOutput = "A detailed market analysis report with trends, opportunities, and recommendations" };

        // Assert
        Assert.Equal("A detailed market analysis report with trends, opportunities, and recommendations", template.ExpectedOutput);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAgentRole()
    {
        // Act
        var template = new TaskTemplateDefinition { AgentRole = "Market Research Analyst" };

        // Assert
        Assert.Equal("Market Research Analyst", template.AgentRole);

        // Act - Set to null via new instance
        var template2 = new TaskTemplateDefinition { AgentRole = null };

        // Assert
        Assert.Null(template2.AgentRole);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRequiredTools()
    {
        // Arrange
        var tools = new List<string> { "MarketDataAPI", "ChartGenerator", "ReportBuilder" };

        // Act
        var template = new TaskTemplateDefinition { RequiredTools = tools };

        // Assert
        Assert.Equal(tools, template.RequiredTools);
        Assert.Equal(3, template.RequiredTools.Count);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRequiredSkills()
    {
        // Arrange
        var skills = new List<string> { "Data Analysis", "Market Research", "Report Writing", "Statistical Analysis" };

        // Act
        var template = new TaskTemplateDefinition { RequiredSkills = skills };

        // Assert
        Assert.Equal(skills, template.RequiredSkills);
        Assert.Equal(4, template.RequiredSkills.Count);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDefaultParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            { "timeframe", "last_quarter" },
            { "markets", DefaultMarkets },
            { "include_competitors", true },
            { "depth", "comprehensive" }
        };

        // Act
        var template = new TaskTemplateDefinition { DefaultParameters = parameters };

        // Assert
        Assert.Equal(parameters, template.DefaultParameters);
        Assert.Equal(4, template.DefaultParameters.Count);
        Assert.Equal("last_quarter", template.DefaultParameters["timeframe"]);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDependencies()
    {
        // Arrange
        var dependencies = new List<string> { "data_collection_task", "competitor_analysis_task" };

        // Act
        var template = new TaskTemplateDefinition { Dependencies = dependencies };

        // Assert
        Assert.Equal(dependencies, template.Dependencies);
        Assert.Equal(2, template.Dependencies.Count);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEstimatedDuration()
    {
        // Act
        var template = new TaskTemplateDefinition { EstimatedDuration = TimeSpan.FromHours(4) };

        // Assert
        Assert.Equal(TimeSpan.FromHours(4), template.EstimatedDuration);

        // Act - Set to null
        var template2 = new TaskTemplateDefinition { EstimatedDuration = null };

        // Assert
        Assert.Null(template2.EstimatedDuration);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingOutputSchema()
    {
        // Arrange
        var schema = new Dictionary<string, object>
        {
            { "type", "object" },
            { "properties", new Dictionary<string, object>
                {
                    { "summary", new { type = "string" } },
                    { "trends", new { type = "array" } },
                    { "recommendations", new { type = "array" } }
                }
            },
            { "required", RequiredSchemaFields }
        };

        // Act
        var template = new TaskTemplateDefinition { OutputSchema = schema };

        // Assert
        Assert.Equal(schema, template.OutputSchema);
        Assert.Equal(3, template.OutputSchema.Count);
    }

    #endregion

    #region Template Scenarios

    [Fact]
    public void ShouldScenario_WhenUsingTemplateDataAnalysis()
    {
        // Arrange & Act
        var template = new TaskTemplateDefinition
        {
            Name = "Customer Data Analysis",
            Description = "Analyze customer behavior patterns and provide insights for business strategy",
            ExpectedOutput = "Comprehensive analysis report with visualizations and actionable recommendations",
            AgentRole = RoleDataAnalyst,
            RequiredTools = ["SQLQuery", "DataVisualizer", "StatisticalAnalyzer"],
            RequiredSkills = ["SQL", "Statistics", "Data Visualization", "Business Intelligence"],
            DefaultParameters = new Dictionary<string, object>
            {
                { "analysis_period", "last_6_months" },
                { "segments", SegmentFields },
                { "confidence_level", 0.95 }
            },
            Dependencies = ["data_extraction_task", "data_cleaning_task"],
            EstimatedDuration = TimeSpan.FromHours(6),
            OutputSchema = new Dictionary<string, object>
            {
                { "format", "pdf_report" },
                { "sections", ReportSections }
            }
        };

        // Assert
        Assert.Equal("Customer Data Analysis", template.Name);
        Assert.Equal(RoleDataAnalyst, template.AgentRole);
        Assert.Equal(3, template.RequiredTools.Count);
        Assert.Equal(4, template.RequiredSkills.Count);
        Assert.Equal(TimeSpan.FromHours(6), template.EstimatedDuration);
        Assert.Contains("SQLQuery", template.RequiredTools);
        Assert.Contains("Statistics", template.RequiredSkills);
    }

    [Fact]
    public void ShouldScenario_WhenUsingTemplateContentCreation()
    {
        // Arrange & Act
        var template = new TaskTemplateDefinition
        {
            Name = "Blog Post Creation",
            Description = "Create engaging blog post on specified topic with SEO optimization",
            ExpectedOutput = "1500-word blog post with meta description, keywords, and social media snippets",
            AgentRole = "Content Writer",
            RequiredTools = ["KeywordResearch", "GrammarChecker", "SEOAnalyzer"],
            RequiredSkills = ["Creative Writing", "SEO", "Research", "Editing"],
            DefaultParameters = new Dictionary<string, object>
            {
                { "word_count", 1500 },
                { "tone", "professional_friendly" },
                { "include_images", true },
                { "seo_keywords", 5 }
            },
            Dependencies = ["topic_research_task"],
            EstimatedDuration = TimeSpan.FromHours(3),
            OutputSchema = new Dictionary<string, object>
            {
                { "content", "markdown" },
                { "metadata", new { title = "string", description = "string", keywords = "array" } }
            }
        };

        // Assert
        Assert.Equal("Blog Post Creation", template.Name);
        Assert.Equal(1500, template.DefaultParameters["word_count"]);
        Assert.Equal(TimeSpan.FromHours(3), template.EstimatedDuration);
        Assert.Contains("SEOAnalyzer", template.RequiredTools);
    }

    [Fact]
    public void ShouldScenario_WhenUsingTemplateCodeReview()
    {
        // Arrange & Act
        var template = new TaskTemplateDefinition
        {
            Name = "Pull Request Code Review",
            Description = "Review code changes for quality, security, and adherence to standards",
            ExpectedOutput = "Detailed review with categorized feedback and improvement suggestions",
            AgentRole = RoleSeniorDeveloper,
            RequiredTools = ["CodeAnalyzer", "SecurityScanner", "LintingTool"],
            RequiredSkills = ["Code Review", "Security Best Practices", "Design Patterns"],
            DefaultParameters = new Dictionary<string, object>
            {
                { "review_depth", "thorough" },
                { "check_security", true },
                { "check_performance", true },
                { "suggest_improvements", true }
            },
            Dependencies = [],
            EstimatedDuration = TimeSpan.FromMinutes(45),
            OutputSchema = new Dictionary<string, object>
            {
                { "categories", IssueCategoriesArray },
                { "format", "markdown_comments" }
            }
        };

        // Assert
        Assert.Equal("Pull Request Code Review", template.Name);
        Assert.Equal(TimeSpan.FromMinutes(45), template.EstimatedDuration);
        Assert.Empty(template.Dependencies);
        Assert.True((bool)template.DefaultParameters["check_security"]);
    }

    #endregion

    #region Collection Operations

    [Fact]
    public void ShouldModificationOperations_WhenUsingRequiredTools()
    {
        // Arrange
        var tools = new List<string>();
        var template = new TaskTemplateDefinition { RequiredTools = tools };

        // Act - Add tools (mutable backing list)
        tools.Add("Tool1");
        tools.Add("Tool2");
        tools.Add("Tool3");

        // Assert
        Assert.Equal(3, template.RequiredTools.Count);
        Assert.Contains("Tool1", template.RequiredTools);

        // Act - Insert at position
        tools.Insert(1, "Tool1.5");

        // Assert
        Assert.Equal(4, template.RequiredTools.Count);
        Assert.Equal("Tool1.5", template.RequiredTools[1]);

        // Act - Remove
        tools.RemoveAt(2);

        // Assert
        Assert.Equal(3, template.RequiredTools.Count);
        Assert.DoesNotContain("Tool2", template.RequiredTools);
    }

    [Fact]
    public void ShouldModificationOperations_WhenUsingRequiredSkills()
    {
        // Arrange
        var skills = new[] { "Skill1", "Skill2", "Skill3", "Skill4", "Skill5" };
        var template = new TaskTemplateDefinition { RequiredSkills = [.. skills] };

        // Assert
        Assert.Equal(5, template.RequiredSkills.Count);

        // Act - Find all matching
        var matchingSkills = template.RequiredSkills.Where(s => s.Contains('2') || s.Contains('4')).ToList();

        // Assert
        Assert.Equal(2, matchingSkills.Count);
        Assert.Contains("Skill2", matchingSkills);
        Assert.Contains("Skill4", matchingSkills);
    }

    [Fact]
    public void ShouldModificationOperations_WhenUsingDependencies()
    {
        // Arrange
        var deps = new List<string>();
        var template = new TaskTemplateDefinition { Dependencies = deps };

        // Act - Add dependencies
        deps.AddRange(s_dependencies123);

        // Assert
        Assert.Equal(3, template.Dependencies.Count);

        // Act - Check for circular dependency
        var wouldBeCircular = template.Dependencies.Contains("self_task");

        // Assert
        Assert.False(wouldBeCircular);

        // Act - Sort dependencies
        deps.Sort();

        // Assert
        Assert.Equal("task1", template.Dependencies[0]);
        Assert.Equal("task2", template.Dependencies[1]);
        Assert.Equal("task3", template.Dependencies[2]);
    }

    [Fact]
    public void ShouldComplexOperations_WhenUsingDefaultParameters()
    {
        // Arrange
        var template = new TaskTemplateDefinition();

        // Act - Add various parameter types
        template.DefaultParameters["string_param"] = "value";
        template.DefaultParameters["int_param"] = 42;
        template.DefaultParameters["bool_param"] = true;
        template.DefaultParameters["array_param"] = new[] { 1, 2, 3 };
        template.DefaultParameters["dict_param"] = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Assert
        Assert.Equal(5, template.DefaultParameters.Count);

        // Act - Try get value
        if (template.DefaultParameters.TryGetValue("int_param", out var value))
        {
            Assert.Equal(42, value);
        }

        // Act - Get all keys
        var keys = template.DefaultParameters.Keys.ToList();

        // Assert
        Assert.Equal(5, keys.Count);
        Assert.Contains("array_param", keys);
    }

    [Fact]
    public void ShouldComplexStructure_WhenUsingOutputSchema()
    {
        // Arrange & Act
        var template = new TaskTemplateDefinition
        {
            OutputSchema = new Dictionary<string, object>
            {
                { "$schema", "http://json-schema.org/draft-07/schema#" },
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "results", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "items", new Dictionary<string, object>
                                    {
                                        { "type", "object" },
                                        { "properties", new Dictionary<string, object>
                                            {
                                                { "id", new { type = "string" } },
                                                { "score", new { type = "number" } },
                                                { "tags", new { type = "array", items = new { type = "string" } } }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        { "metadata", new Dictionary<string, object>
                            {
                                { "type", "object" },
                                { "additionalProperties", true }
                            }
                        }
                    }
                },
                { "required", RequiredResultsField }
            }
        };

        // Assert
        Assert.Equal(4, template.OutputSchema.Count);
        Assert.True(template.OutputSchema.ContainsKey("$schema"));
        Assert.IsType<Dictionary<string, object>>(template.OutputSchema["properties"]);
    }

    #endregion

    #region Edge Cases and Validation

    [Fact]
    public void ShouldVariousTimeSpans_WhenUsingEstimatedDuration()
    {
        // Act & Assert
        Assert.Equal(TimeSpan.Zero, new TaskTemplateDefinition { EstimatedDuration = TimeSpan.Zero }.EstimatedDuration);
        Assert.Equal(500, new TaskTemplateDefinition { EstimatedDuration = TimeSpan.FromMilliseconds(500) }.EstimatedDuration!.Value.TotalMilliseconds);
        Assert.Equal(7, new TaskTemplateDefinition { EstimatedDuration = TimeSpan.FromDays(7) }.EstimatedDuration!.Value.TotalDays);
        Assert.Equal(TimeSpan.MaxValue, new TaskTemplateDefinition { EstimatedDuration = TimeSpan.MaxValue }.EstimatedDuration);
        Assert.True(new TaskTemplateDefinition { EstimatedDuration = TimeSpan.FromHours(-2) }.EstimatedDuration!.Value.TotalHours < 0);
    }

    [Fact]
    public void ShouldAccept_WhenUsingStringPropertiesWithNull()
    {
        // Act
        var template = new TaskTemplateDefinition
        {
            Id = null!,
            Name = null!,
            Description = null!,
            ExpectedOutput = null!,
            AgentRole = null
        };

        // Assert
        Assert.Null(template.Id);
        Assert.Null(template.Name);
        Assert.Null(template.Description);
        Assert.Null(template.ExpectedOutput);
        Assert.Null(template.AgentRole);
    }

    [Fact]
    public void ShouldAccept_WhenUsingCollectionsWithNull()
    {
        // Act
        var template = new TaskTemplateDefinition
        {
            RequiredTools = null!,
            RequiredSkills = null!,
            DefaultParameters = null!,
            Dependencies = null!,
            OutputSchema = null!
        };

        // Assert
        Assert.Null(template.RequiredTools);
        Assert.Null(template.RequiredSkills);
        Assert.Null(template.DefaultParameters);
        Assert.Null(template.Dependencies);
        Assert.Null(template.OutputSchema);
    }

    [Fact]
    public void ShouldAccept_WhenUsingIdWithCustomValues()
    {
        // Act & Assert
        var id1 = TaskTemplateId.Create();
        var id2 = TaskTemplateId.Create();
        var id3 = TaskTemplateId.Create();
        Assert.Equal(id1, new TaskTemplateDefinition { Id = id1 }.Id);
        Assert.Equal(id2, new TaskTemplateDefinition { Id = id2 }.Id);
        Assert.Equal(id3, new TaskTemplateDefinition { Id = id3 }.Id);
    }

    [Fact]
    public void ShouldAccept_WhenAccessingPropertiesWithUnicodeContent()
    {
        // Act
        var template = new TaskTemplateDefinition
        {
            Name = "数据分析任务 📊",
            Description = "分析客户数据并提供洞察 🔍",
            ExpectedOutput = "包含图表和建议的详细报告 📈",
            AgentRole = "数据分析师 👨‍💻",
            RequiredTools = ["数据可视化工具 📊"],
            RequiredSkills = ["统计分析 📈"],
            Dependencies = ["数据收集任务 🗃️"]
        };

        // Assert
        Assert.Contains("📊", template.Name);
        Assert.Contains("🔍", template.Description);
        Assert.Contains("📈", template.ExpectedOutput);
        Assert.Contains("👨‍💻", template.AgentRole);
        Assert.Contains("数据可视化工具 📊", template.RequiredTools);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingCollectionsWithDuplicates()
    {
        // Act
        var template = new TaskTemplateDefinition
        {
            RequiredTools = ["Tool1", "Tool2", "Tool1", "Tool3", "Tool2"],
            RequiredSkills = ["Skill1", "Skill1", "Skill2"],
            Dependencies = ["Task1", "Task1"]
        };

        // Assert - Duplicates are allowed
        Assert.Equal(5, template.RequiredTools.Count);
        Assert.Equal(3, template.RequiredSkills.Count);
        Assert.Equal(2, template.Dependencies.Count);

        // Count occurrences
        var tool1Count = template.RequiredTools.Count(t => t == "Tool1");
        Assert.Equal(2, tool1Count);
    }

    #endregion

    #region Very Long Content

    [Fact]
    public void ShouldAccept_WhenAccessingPropertiesWithVeryLongContent()
    {
        // Arrange
        var longString = new string('X', 50000);

        // Act
        var template = new TaskTemplateDefinition
        {
            Name = longString,
            Description = longString,
            ExpectedOutput = longString
        };

        // Assert
        Assert.Equal(50000, template.Name.Length);
        Assert.Equal(50000, template.Description.Length);
        Assert.Equal(50000, template.ExpectedOutput.Length);
    }

    [Fact]
    public void ShouldHandle_WhenUsingCollectionsWithManyItems()
    {
        // Arrange
        var tools = new List<string>();
        var skills = new List<string>();
        var deps = new List<string>();
        var template = new TaskTemplateDefinition
        {
            RequiredTools = tools,
            RequiredSkills = skills,
            Dependencies = deps
        };

        // Act - Add many items (mutable backing lists/dicts)
        for (int i = 0; i < 10000; i++)
        {
            tools.Add($"Tool{i}");
            skills.Add($"Skill{i}");
            deps.Add($"Dependency{i}");
            template.DefaultParameters[$"param{i}"] = i;
            template.OutputSchema[$"field{i}"] = $"type{i}";
        }

        // Assert
        Assert.Equal(10000, template.RequiredTools.Count);
        Assert.Equal(10000, template.RequiredSkills.Count);
        Assert.Equal(10000, template.Dependencies.Count);
        Assert.Equal(10000, template.DefaultParameters.Count);
        Assert.Equal(10000, template.OutputSchema.Count);
    }

    #endregion

    #region Object Methods

    [Fact]
    public void ShouldReturnNonNullString_WhenCallingToString()
    {
        // Arrange
        var template = new TaskTemplateDefinition { Name = "Test Task Template" };

        // Act
        var result = template.ToString();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void ShouldReturnConsistentValue_WhenCallingGetHashCode()
    {
        // Arrange
        var template = new TaskTemplateDefinition
        {
            Name = "Test Template",
            Description = "Test Description"
        };

        // Act
        var hash1 = template.GetHashCode();
        var hash2 = template.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameReference()
    {
        // Arrange
        var template = new TaskTemplateDefinition();

        // Act & Assert
        Assert.True(template.Equals(template));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        // Arrange
        var template = new TaskTemplateDefinition();

        // Act & Assert
        Assert.False(template.Equals(null));
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldScenario_WhenUsingTemplateMultiStepProcess()
    {
        // Arrange & Act - Complex multi-step data pipeline task
        var template = new TaskTemplateDefinition
        {
            Name = "ETL Pipeline Task",
            Description = "Extract, transform, and load data from multiple sources",
            ExpectedOutput = "Processed data loaded into warehouse with quality report",
            AgentRole = "Data Engineer",
            RequiredTools =
            [
                "DataExtractor",
                "DataTransformer",
                "DataValidator",
                "DataLoader",
                "QualityChecker"
            ],
            RequiredSkills =
            [
                "ETL",
                "SQL",
                "Python",
                "Data Quality",
                "Performance Optimization"
            ],
            DefaultParameters = new Dictionary<string, object>
            {
                { "sources", DataSources },
                { "transformations", new Dictionary<string, object>
                    {
                        { "clean_nulls", true },
                        { "standardize_dates", true },
                        { "validate_schema", true }
                    }
                },
                { "load_strategy", "incremental" },
                { "error_threshold", 0.01 }
            },
            Dependencies =
            [
                "source_connectivity_check",
                "schema_validation",
                "destination_preparation"
            ],
            EstimatedDuration = TimeSpan.FromHours(2.5),
            OutputSchema = new Dictionary<string, object>
            {
                { "records_processed", "integer" },
                { "errors", new { type = "array", items = "object" } },
                { "quality_metrics", new Dictionary<string, object>
                    {
                        { "completeness", "number" },
                        { "accuracy", "number" },
                        { "consistency", "number" }
                    }
                },
                { "load_timestamp", "datetime" }
            }
        };

        // Assert
        Assert.Equal(5, template.RequiredTools.Count);
        Assert.Equal(5, template.RequiredSkills.Count);
        Assert.Equal(3, template.Dependencies.Count);
        Assert.Equal("incremental", template.DefaultParameters["load_strategy"]);
        Assert.NotNull(template.OutputSchema["quality_metrics"]);
    }

    #endregion
}
