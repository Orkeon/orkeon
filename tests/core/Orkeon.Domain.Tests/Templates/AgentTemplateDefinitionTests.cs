using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
namespace Orkeon.Domain.Tests.Templates;

/// <summary>
/// Tests for AgentTemplateDefinition following Clean Architecture principles.
/// Tests the agent template definition model and its behavior.
/// </summary>
public class AgentTemplateDefinitionTests
{
    private static readonly string[] s_skills = ["Skill1", "Skill2", "Skill3", "Skill4"];

    #region Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingDefaultConstructor()
    {
        // Act
        var template = new AgentTemplateDefinition();

        // Assert
        Assert.NotNull(template.Id);
        Assert.NotNull(template.Id);
        Assert.NotNull(template.Id); // ID is ULID-based EntityId
        Assert.Equal(string.Empty, template.Name);
        Assert.Equal(string.Empty, template.Role);
        Assert.Equal(string.Empty, template.Goal);
        Assert.Equal(string.Empty, template.Backstory);
        Assert.NotNull(template.RequiredTools);
        Assert.Empty(template.RequiredTools);
        Assert.NotNull(template.Skills);
        Assert.Empty(template.Skills);
        Assert.NotNull(template.DefaultParameters);
        Assert.Empty(template.DefaultParameters);
        Assert.True(template.AllowDelegation);
        Assert.Equal(25, template.MaxIterations);
        Assert.True(template.Verbose);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingMultipleInstances()
    {
        // Act
        var template1 = new AgentTemplateDefinition();
        var template2 = new AgentTemplateDefinition();
        var template3 = new AgentTemplateDefinition();

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
        var newId = AgentTemplateId.Create();
        var template = new AgentTemplateDefinition { Id = newId };
        Assert.Equal(newId, template.Id);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingName()
    {
        var template = new AgentTemplateDefinition { Name = "Research Specialist Template" };
        Assert.Equal("Research Specialist Template", template.Name);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRole()
    {
        var template = new AgentTemplateDefinition { Role = "Senior Research Analyst" };
        Assert.Equal("Senior Research Analyst", template.Role);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingGoal()
    {
        var template = new AgentTemplateDefinition { Goal = "Conduct thorough research and provide comprehensive analysis" };
        Assert.Equal("Conduct thorough research and provide comprehensive analysis", template.Goal);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingBackstory()
    {
        var template = new AgentTemplateDefinition { Backstory = "You are an experienced research analyst with 15 years of expertise..." };
        Assert.Equal("You are an experienced research analyst with 15 years of expertise...", template.Backstory);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRequiredTools()
    {
        var tools = new List<string> { "WebSearch", "FileReader", "DataAnalyzer" };
        var template = new AgentTemplateDefinition { RequiredTools = tools };
        Assert.Equal(tools, template.RequiredTools);
        Assert.Equal(3, template.RequiredTools.Count);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingSkills()
    {
        var skills = new List<string> { "Research", "Analysis", "Writing", "Critical Thinking" };
        var template = new AgentTemplateDefinition { Skills = skills };
        Assert.Equal(skills, template.Skills);
        Assert.Equal(4, template.Skills.Count);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDefaultParameters()
    {
        var parameters = new Dictionary<string, object>
        {
            { "temperature", 0.7 },
            { "max_tokens", 2000 },
            { "model", ModelGpt4 },
            { "retry_count", 3 }
        };
        var template = new AgentTemplateDefinition { DefaultParameters = parameters };
        Assert.Equal(parameters, template.DefaultParameters);
        Assert.Equal(4, template.DefaultParameters.Count);
        Assert.Equal(0.7, template.DefaultParameters["temperature"]);
    }

    [Fact]
    public void ShouldBeSettable_WhenAllowingDelegation()
    {
        var templateNo = new AgentTemplateDefinition { AllowDelegation = false };
        var templateYes = new AgentTemplateDefinition { AllowDelegation = true };
        Assert.False(templateNo.AllowDelegation);
        Assert.True(templateYes.AllowDelegation);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingMaxIterations()
    {
        var template = new AgentTemplateDefinition { MaxIterations = 50 };
        Assert.Equal(50, template.MaxIterations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void ShouldAcceptVariousValues_WhenUsingMaxIterations(int iterations)
    {
        var template = new AgentTemplateDefinition { MaxIterations = iterations };
        Assert.Equal(iterations, template.MaxIterations);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingVerbose()
    {
        var templateOff = new AgentTemplateDefinition { Verbose = false };
        var templateOn = new AgentTemplateDefinition { Verbose = true };
        Assert.False(templateOff.Verbose);
        Assert.True(templateOn.Verbose);
    }

    #endregion

    #region Template Scenarios

    [Fact]
    public void ShouldScenario_WhenUsingTemplateResearchAnalyst()
    {
        var template = new AgentTemplateDefinition
        {
            Name = "Research Analyst Template",
            Role = "Senior Research Analyst",
            Goal = "Conduct comprehensive research and provide actionable insights",
            Backstory = "You are a seasoned research analyst with expertise in market analysis, " +
                       "data interpretation, and strategic recommendations. You have worked with " +
                       "Fortune 500 companies and have a deep understanding of various industries.",
            RequiredTools = ["WebSearch", "PDFReader", "DataAnalyzer", "ReportGenerator"],
            Skills = ["Research", "Analysis", "Critical Thinking", "Report Writing", "Data Visualization"],
            DefaultParameters = new Dictionary<string, object>
            {
                { "temperature", 0.3 },
                { "max_tokens", 3000 },
                { "model", ModelGpt4 },
                { "search_depth", "comprehensive" }
            },
            AllowDelegation = true,
            MaxIterations = 30,
            Verbose = true
        };

        Assert.Equal("Research Analyst Template", template.Name);
        Assert.Equal(4, template.RequiredTools.Count);
        Assert.Equal(5, template.Skills.Count);
        Assert.Equal(0.3, template.DefaultParameters["temperature"]);
        Assert.Contains("WebSearch", template.RequiredTools);
        Assert.Contains("Critical Thinking", template.Skills);
    }

    [Fact]
    public void ShouldScenario_WhenUsingTemplateCodeReviewer()
    {
        var template = new AgentTemplateDefinition
        {
            Name = "Code Reviewer Template",
            Role = "Senior Software Engineer - Code Review Specialist",
            Goal = "Review code for quality, security, performance, and best practices",
            Backstory = "You are an experienced software engineer specializing in code review. " +
                       "You have expertise in multiple programming languages and frameworks.",
            RequiredTools = ["CodeAnalyzer", "SecurityScanner", "PerformanceProfiler"],
            Skills = ["Code Review", "Security Analysis", "Performance Optimization", "Best Practices"],
            DefaultParameters = new Dictionary<string, object>
            {
                { "temperature", 0.1 },
                { "strict_mode", true },
                { "include_suggestions", true }
            },
            AllowDelegation = false,
            MaxIterations = 15,
            Verbose = false
        };

        Assert.Equal("Code Reviewer Template", template.Name);
        Assert.False(template.AllowDelegation);
        Assert.False(template.Verbose);
        Assert.Equal(15, template.MaxIterations);
        Assert.True((bool)template.DefaultParameters["strict_mode"]);
    }

    [Fact]
    public void ShouldScenario_WhenUsingTemplateContentWriter()
    {
        var template = new AgentTemplateDefinition
        {
            Name = "Content Writer Template",
            Role = "Creative Content Writer",
            Goal = "Create engaging, high-quality content for various platforms",
            Backstory = "You are a creative writer with expertise in crafting compelling content " +
                       "for blogs, social media, and marketing materials.",
            RequiredTools = ["GrammarChecker", "SEOAnalyzer", "ContentPlanner"],
            Skills = ["Creative Writing", "SEO", "Content Strategy", "Editing"],
            DefaultParameters = new Dictionary<string, object>
            {
                { "temperature", 0.8 },
                { "creativity_level", "high" },
                { "tone", "professional" }
            },
            AllowDelegation = true,
            MaxIterations = 20,
            Verbose = true
        };

        Assert.Equal(0.8, template.DefaultParameters["temperature"]);
        Assert.Equal("high", template.DefaultParameters["creativity_level"]);
        Assert.Contains("SEOAnalyzer", template.RequiredTools);
    }

    #endregion

    #region Collection Operations

    [Fact]
    public void ShouldExposeRequiredTools_WhenProvidedAtConstruction()
    {
        var template = new AgentTemplateDefinition
        {
            RequiredTools = ["Tool1", "Tool2", "Tool3"]
        };

        Assert.Equal(3, template.RequiredTools.Count);
        Assert.Contains("Tool1", template.RequiredTools);
        Assert.Contains("Tool3", template.RequiredTools);
    }

    [Fact]
    public void ShouldExposeSkills_WhenProvidedAtConstruction()
    {
        var template = new AgentTemplateDefinition
        {
            Skills = [.. s_skills]
        };
        Assert.Equal(4, template.Skills.Count);

        var hasSkill = template.Skills.Contains("Skill2");
        Assert.True(hasSkill);

        var index = template.Skills.ToList().IndexOf("Skill3");
        Assert.Equal(2, index);
    }

    [Fact]
    public void ShouldModificationOperations_WhenUsingDefaultParameters()
    {
        var template = new AgentTemplateDefinition();

        template.DefaultParameters["param1"] = "value1";
        template.DefaultParameters["param2"] = 42;
        template.DefaultParameters["param3"] = true;
        template.DefaultParameters["param4"] = 3.14;
        Assert.Equal(4, template.DefaultParameters.Count);
        Assert.Equal("value1", template.DefaultParameters["param1"]);
        Assert.Equal(42, template.DefaultParameters["param2"]);

        template.DefaultParameters["param2"] = 100;
        Assert.Equal(100, template.DefaultParameters["param2"]);

        template.DefaultParameters.Remove("param3");
        Assert.Equal(3, template.DefaultParameters.Count);
        Assert.False(template.DefaultParameters.ContainsKey("param3"));
    }

    #endregion

    #region Edge Cases and Validation

    [Fact]
    public void ShouldAccept_WhenUsingIdWithCustomValue()
    {
        var id1 = AgentTemplateId.Create();
        var id2 = AgentTemplateId.Create();
        var id3 = AgentTemplateId.Create();
        Assert.Equal(id1, new AgentTemplateDefinition { Id = id1 }.Id);
        Assert.Equal(id2, new AgentTemplateDefinition { Id = id2 }.Id);
        Assert.Equal(id3, new AgentTemplateDefinition { Id = id3 }.Id);
    }

    [Fact]
    public void ShouldAccept_WhenUsingStringPropertiesWithNull()
    {
        var template = new AgentTemplateDefinition
        {
            Id = null!,
            Name = null!,
            Role = null!,
            Goal = null!,
            Backstory = null!
        };

        Assert.Null(template.Id);
        Assert.Null(template.Name);
        Assert.Null(template.Role);
        Assert.Null(template.Goal);
        Assert.Null(template.Backstory);
    }

    [Fact]
    public void ShouldAccept_WhenUsingCollectionsWithNull()
    {
        var template = new AgentTemplateDefinition
        {
            RequiredTools = null!,
            Skills = null!,
            DefaultParameters = null!
        };

        Assert.Null(template.RequiredTools);
        Assert.Null(template.Skills);
        Assert.Null(template.DefaultParameters);
    }

    [Fact]
    public void ShouldAccept_WhenUsingMaxIterationsWithNegativeValue()
    {
        var template = new AgentTemplateDefinition { MaxIterations = -10 };
        Assert.Equal(-10, template.MaxIterations);
    }

    [Fact]
    public void ShouldAccept_WhenUsingStringPropertiesWithUnicodeContent()
    {
        var template = new AgentTemplateDefinition
        {
            Name = "AI助手模板 🤖",
            Role = "高级研究员 👨‍🔬",
            Goal = "进行深入研究并提供见解 📊",
            Backstory = "您是一位经验丰富的研究人员...",
            RequiredTools = ["搜索工具 🔍"],
            Skills = ["数据分析 📈"]
        };

        Assert.Contains("🤖", template.Name);
        Assert.Contains("👨‍🔬", template.Role);
        Assert.Contains("📊", template.Goal);
        Assert.Contains("搜索工具 🔍", template.RequiredTools);
        Assert.Contains("数据分析 📈", template.Skills);
    }

    [Fact]
    public void ShouldAccept_WhenUsingDefaultParametersWithComplexTypes()
    {
        var template = new AgentTemplateDefinition();

        template.DefaultParameters["array"] = new[] { 1, 2, 3, 4, 5 };
        template.DefaultParameters["dictionary"] = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };
        template.DefaultParameters["object"] = new
        {
            Name = "Test",
            Value = 123,
            Nested = new { Level = 2 }
        };
        template.DefaultParameters["null"] = null!;

        Assert.IsType<int[]>(template.DefaultParameters["array"]);
        Assert.IsType<Dictionary<string, string>>(template.DefaultParameters["dictionary"]);
        Assert.NotNull(template.DefaultParameters["object"]);
        Assert.Null(template.DefaultParameters["null"]);
    }

    #endregion

    #region Very Long Content

    [Fact]
    public void ShouldAccept_WhenAccessingPropertiesWithVeryLongContent()
    {
        var longString = new string('A', 10000);
        var template = new AgentTemplateDefinition
        {
            Name = longString,
            Role = longString,
            Goal = longString,
            Backstory = longString
        };

        Assert.Equal(10000, template.Name.Length);
        Assert.Equal(10000, template.Role.Length);
        Assert.Equal(10000, template.Goal.Length);
        Assert.Equal(10000, template.Backstory.Length);
    }

    [Fact]
    public void ShouldHandle_WhenUsingCollectionsWithManyItems()
    {
        var tools = new List<string>();
        var skills = new List<string>();
        var template = new AgentTemplateDefinition
        {
            RequiredTools = tools,
            Skills = skills
        };

        for (int i = 0; i < 1000; i++)
        {
            tools.Add($"Tool{i}");
            skills.Add($"Skill{i}");
            template.DefaultParameters[$"param{i}"] = i;
        }

        Assert.Equal(1000, template.RequiredTools.Count);
        Assert.Equal(1000, template.Skills.Count);
        Assert.Equal(1000, template.DefaultParameters.Count);
        Assert.Equal("Tool500", template.RequiredTools[500]);
        Assert.Equal(500, template.DefaultParameters["param500"]);
    }

    #endregion

    #region Object Methods

    [Fact]
    public void ShouldReturnNonNullString_WhenCallingToString()
    {
        var template = new AgentTemplateDefinition { Name = "Test Template" };
        var result = template.ToString();
        Assert.NotNull(result);
    }

    [Fact]
    public void ShouldReturnConsistentValue_WhenCallingGetHashCode()
    {
        var template = new AgentTemplateDefinition { Name = "Test Template", Role = "Test Role" };
        var hash1 = template.GetHashCode();
        var hash2 = template.GetHashCode();
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameReference()
    {
        var template = new AgentTemplateDefinition();
        Assert.True(template.Equals(template));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        var template = new AgentTemplateDefinition();
        Assert.False(template.Equals(null));
    }

    #endregion
}
