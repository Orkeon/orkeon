using Orkeon.Domain.Task.Contexts;

namespace Orkeon.Domain.Tests.Contexts;

/// <summary>
/// Tests for CodeGenerationContext following Clean Architecture principles.
/// Tests the code generation task context classes and their behavior.
/// </summary>
public class CodeGenerationContextTests
{
    #region CodeGenerationContext Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingCodeGenerationContextWithDefaultConstructor()
    {
        // Act
        var context = new CodeGenerationContext();

        // Assert
        Assert.Equal("C#", context.Language);
        Assert.Equal(".NET 10", context.Framework);
        Assert.NotNull(context.Requirements);
        Assert.Empty(context.Requirements);
        Assert.NotNull(context.GeneratedFiles);
        Assert.Empty(context.GeneratedFiles);
        Assert.NotNull(context.Dependencies);
        Assert.Empty(context.Dependencies);
        Assert.NotNull(context.DesignPatterns);
        Assert.Empty(context.DesignPatterns);
        Assert.NotNull(context.QualityMetrics);
        Assert.Equal(0.8f, context.TestCoverageRequirement);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCodeGenerationContextUsingProperties()
    {
        // Arrange
        var requirements = new List<string> { "RESTful API", "Authentication", "Logging" };
        var generatedFiles = new Dictionary<string, string> { { "Program.cs", "// Main entry point" } };
        var dependencies = new List<string> { "Microsoft.AspNetCore", "Serilog" };
        var designPatterns = new List<string> { "Repository", "CQRS", "Mediator" };
        var qualityMetrics = new CodeQualityMetrics { LinesOfCode = 1500 };

        // Act
        var context = new CodeGenerationContext
        {
            Language = "Python",
            Framework = "Django",
            Requirements = requirements,
            GeneratedFiles = generatedFiles,
            Dependencies = dependencies,
            DesignPatterns = designPatterns,
            QualityMetrics = qualityMetrics,
            TestCoverageRequirement = 0.9f
        };

        // Assert
        Assert.Equal("Python", context.Language);
        Assert.Equal("Django", context.Framework);
        Assert.Equal(requirements, context.Requirements);
        Assert.Equal(generatedFiles, context.GeneratedFiles);
        Assert.Equal(dependencies, context.Dependencies);
        Assert.Equal(designPatterns, context.DesignPatterns);
        Assert.Equal(qualityMetrics, context.QualityMetrics);
        Assert.Equal(0.9f, context.TestCoverageRequirement);
    }

    #endregion

    #region AddGeneratedFile Tests

    [Fact]
    public void ShouldAddToGeneratedFiles_WhenAddingGeneratedFileWithValidFileNameAndContent()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddGeneratedFile("UserController.cs", "public class UserController { }");
        context.AddGeneratedFile("IUserService.cs", "public interface IUserService { }");
        context.AddGeneratedFile("UserService.cs", "public class UserService : IUserService { }");

        // Assert
        Assert.Equal(3, context.GeneratedFiles.Count);
        Assert.Equal("public class UserController { }", context.GeneratedFiles["UserController.cs"]);
        Assert.Equal("public interface IUserService { }", context.GeneratedFiles["IUserService.cs"]);
        Assert.Equal("public class UserService : IUserService { }", context.GeneratedFiles["UserService.cs"]);
    }

    [Fact]
    public void ShouldStoreEmptyString_WhenAddingGeneratedFileWithNullContent()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddGeneratedFile("EmptyFile.cs", null!);

        // Assert
        Assert.Single(context.GeneratedFiles);
        Assert.Equal(string.Empty, context.GeneratedFiles["EmptyFile.cs"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ShouldThrowArgumentException_WhenAddingGeneratedFileWithInvalidFileName(string invalidFileName)
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            context.AddGeneratedFile(invalidFileName, "content"));
        Assert.Equal("fileName", exception.ParamName);
    }

    [Fact]
    public void ShouldOverwrite_WhenAddingGeneratedFileWithExistingFileName()
    {
        // Arrange
        var context = new CodeGenerationContext();
        context.AddGeneratedFile("Config.cs", "// Original content");

        // Act
        context.AddGeneratedFile("Config.cs", "// Updated content");

        // Assert
        Assert.Single(context.GeneratedFiles);
        Assert.Equal("// Updated content", context.GeneratedFiles["Config.cs"]);
    }

    [Fact]
    public void ShouldStore_WhenAddingGeneratedFileWithLargeContent()
    {
        // Arrange
        var context = new CodeGenerationContext();
        var largeContent = string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"// Line {i}"));

        // Act
        context.AddGeneratedFile("LargeFile.cs", largeContent);

        // Assert
        Assert.Single(context.GeneratedFiles);
        Assert.Equal(largeContent, context.GeneratedFiles["LargeFile.cs"]);
    }

    [Fact]
    public void ShouldAccept_WhenAddingGeneratedFileWithSpecialCharactersInFileName()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddGeneratedFile("User.Model.cs", "// Model");
        context.AddGeneratedFile("Data/Entities/User.cs", "// Entity");
        context.AddGeneratedFile("_PrivateClass.cs", "// Private");
        context.AddGeneratedFile("Class-With-Dashes.cs", "// Dashes");

        // Assert
        Assert.Equal(4, context.GeneratedFiles.Count);
        Assert.Contains("User.Model.cs", context.GeneratedFiles.Keys);
        Assert.Contains("Data/Entities/User.cs", context.GeneratedFiles.Keys);
    }

    #endregion

    #region AddDependency Tests

    [Fact]
    public void ShouldAddToDependencies_WhenAddingDependencyWithValidDependency()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddDependency("Microsoft.EntityFrameworkCore");
        context.AddDependency("AutoMapper");
        context.AddDependency("FluentValidation");

        // Assert
        Assert.Equal(3, context.Dependencies.Count);
        Assert.Contains("Microsoft.EntityFrameworkCore", context.Dependencies);
        Assert.Contains("AutoMapper", context.Dependencies);
        Assert.Contains("FluentValidation", context.Dependencies);
    }

    [Fact]
    public void ShouldNotAddDuplicate_WhenAddingDependencyWithDuplicateDependency()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddDependency("Newtonsoft.Json");
        context.AddDependency("Newtonsoft.Json"); // Duplicate
        context.AddDependency("System.Linq");
        context.AddDependency("Newtonsoft.Json"); // Another duplicate

        // Assert
        Assert.Equal(2, context.Dependencies.Count);
        Assert.Single(context.Dependencies, d => d == "Newtonsoft.Json");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ShouldNotAdd_WhenAddingDependencyWithInvalidDependency(string invalidDependency)
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddDependency(invalidDependency);

        // Assert
        Assert.Empty(context.Dependencies);
    }

    [Fact]
    public void ShouldTreatDifferentCasesAsDifferent_WhenAddingDependencyCaseSensitive()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddDependency("Microsoft.AspNetCore");
        context.AddDependency("microsoft.aspnetcore");
        context.AddDependency("MICROSOFT.ASPNETCORE");

        // Assert
        Assert.Equal(3, context.Dependencies.Count);
    }

    [Fact]
    public void ShouldAccept_WhenAddingDependencyWithVersionedDependencies()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        context.AddDependency("Microsoft.EntityFrameworkCore@8.0.0");
        context.AddDependency("AutoMapper>=12.0.0");
        context.AddDependency("FluentValidation~11.5.0");

        // Assert
        Assert.Equal(3, context.Dependencies.Count);
        Assert.All(context.Dependencies, dep =>
        {
            bool hasVersion = dep.Contains('@') || dep.Contains('>') || dep.Contains('~');
            Assert.True(hasVersion, $"Dependency '{dep}' should contain version specifier");
        });
    }

    #endregion

    #region ValidateRequirements Tests

    [Fact]
    public void ShouldReturnTrue_WhenValidatingRequirementsWithRequirementsAndGeneratedFiles()
    {
        // Arrange
        var context = new CodeGenerationContext();
        context.AddRequirement("Create user authentication");
        context.AddRequirement("Implement logging");
        context.AddGeneratedFile("AuthController.cs", "// Auth implementation");
        context.AddGeneratedFile("LoggingService.cs", "// Logging implementation");

        // Act
        var isValid = context.ValidateRequirements();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingRequirementsWithNoRequirements()
    {
        // Arrange
        var context = new CodeGenerationContext();
        context.AddGeneratedFile("SomeFile.cs", "// Content");

        // Act
        var isValid = context.ValidateRequirements();

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingRequirementsWithNoGeneratedFiles()
    {
        // Arrange
        var context = new CodeGenerationContext();
        context.AddRequirement("Create API endpoints");

        // Act
        var isValid = context.ValidateRequirements();

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingRequirementsWithEmptyRequirementsAndFiles()
    {
        // Arrange
        var context = new CodeGenerationContext();

        // Act
        var isValid = context.ValidateRequirements();

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region CodeQualityMetrics Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingCodeQualityMetricsWithDefaultConstructor()
    {
        // Act
        var metrics = new CodeQualityMetrics();

        // Assert
        Assert.Equal(0, metrics.LinesOfCode);
        Assert.Equal(0, metrics.CyclomaticComplexity);
        Assert.Equal(0.0f, metrics.MaintainabilityIndex);
        Assert.Equal(0, metrics.TechnicalDebt);
        Assert.Equal(0.0f, metrics.TestCoverage);
        Assert.Equal(0, metrics.CodeSmells);
        Assert.NotNull(metrics.IssuesByCategory);
        Assert.Empty(metrics.IssuesByCategory);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCodeQualityMetricsUsingProperties()
    {
        // Arrange
        var issuesByCategory = new Dictionary<string, int>
        {
            { "Security", 3 },
            { "Performance", 5 },
            { "Maintainability", 12 }
        };

        // Act
        var metrics = new CodeQualityMetrics
        {
            LinesOfCode = 5000,
            CyclomaticComplexity = 15,
            MaintainabilityIndex = 78.5f,
            TechnicalDebt = 120,
            TestCoverage = 0.85f,
            CodeSmells = 8,
            IssuesByCategory = issuesByCategory
        };

        // Assert
        Assert.Equal(5000, metrics.LinesOfCode);
        Assert.Equal(15, metrics.CyclomaticComplexity);
        Assert.Equal(78.5f, metrics.MaintainabilityIndex);
        Assert.Equal(120, metrics.TechnicalDebt);
        Assert.Equal(0.85f, metrics.TestCoverage);
        Assert.Equal(8, metrics.CodeSmells);
        Assert.Equal(issuesByCategory, metrics.IssuesByCategory);
        Assert.Equal(3, metrics.IssuesByCategory.Count);
    }

    [Fact]
    public void ShouldSupportDictionaryContent_WhenUsingCodeQualityMetricsUsingIssuesByCategory()
    {
        // Arrange & Act — Dictionary init is still mutable
        var metrics = new CodeQualityMetrics
        {
            IssuesByCategory = new Dictionary<string, int>
            {
                { "Bugs", 2 },
                { "Code Smells", 8 },
                { "Vulnerabilities", 0 }
            }
        };

        // Mutable dictionary allows updates
        metrics.IssuesByCategory["Bugs"] = 3;

        // Assert
        Assert.Equal(3, metrics.IssuesByCategory.Count);
        Assert.Equal(3, metrics.IssuesByCategory["Bugs"]);
        Assert.Equal(8, metrics.IssuesByCategory["Code Smells"]);
        Assert.Equal(0, metrics.IssuesByCategory["Vulnerabilities"]);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldCompleteCodeGenerationScenario_WhenUsingCodeGenerationContext()
    {
        // Arrange
        var context = new CodeGenerationContext
        {
            Language = "C#",
            Framework = "ASP.NET Core",
            TestCoverageRequirement = 0.85f,
            QualityMetrics = new CodeQualityMetrics
            {
                LinesOfCode = 850,
                CyclomaticComplexity = 12,
                MaintainabilityIndex = 82.3f,
                TechnicalDebt = 45,
                TestCoverage = 0.87f,
                CodeSmells = 3,
                IssuesByCategory = new Dictionary<string, int>
                {
                    { "Minor", 5 },
                    { "Major", 2 },
                    { "Critical", 0 }
                }
            }
        };

        // Add requirements
        context.AddRequirement("Create REST API for user management");
        context.AddRequirement("Implement JWT authentication");
        context.AddRequirement("Add comprehensive logging");
        context.AddRequirement("Include unit tests");

        // Add design patterns
        context.AddDesignPattern("Repository Pattern");
        context.AddDesignPattern("Unit of Work");
        context.AddDesignPattern("Dependency Injection");

        // Add dependencies
        context.AddDependency("Microsoft.AspNetCore.Mvc");
        context.AddDependency("Microsoft.AspNetCore.Authentication.JwtBearer");
        context.AddDependency("Microsoft.EntityFrameworkCore");
        context.AddDependency("Serilog");
        context.AddDependency("xUnit");
        context.AddDependency("Moq");

        // Generate files
        context.AddGeneratedFile("Controllers/UserController.cs",
            @"[ApiController]
            [Route(""api/[controller]"")]
            public class UserController : ControllerBase
            {
                // Implementation
            }");

        context.AddGeneratedFile("Services/IUserService.cs",
            "public interface IUserService { }");

        context.AddGeneratedFile("Services/UserService.cs",
            "public class UserService : IUserService { }");

        context.AddGeneratedFile("Authentication/JwtService.cs",
            "public class JwtService { }");

        context.AddGeneratedFile("Tests/UserControllerTests.cs",
            "public class UserControllerTests { }");

        // Assert - Verify complete generation
        Assert.Equal("C#", context.Language);
        Assert.Equal("ASP.NET Core", context.Framework);
        Assert.Equal(4, context.Requirements.Count);
        Assert.Equal(3, context.DesignPatterns.Count);
        Assert.Equal(6, context.Dependencies.Count);
        Assert.Equal(5, context.GeneratedFiles.Count);

        // Verify validation passes
        Assert.True(context.ValidateRequirements());

        // Verify quality metrics
        Assert.Equal(850, context.QualityMetrics.LinesOfCode);
        Assert.True(context.QualityMetrics.TestCoverage >= context.TestCoverageRequirement);
        Assert.Equal(0, context.QualityMetrics.IssuesByCategory["Critical"]);
    }

    [Fact]
    public void ShouldMultiLanguageScenario_WhenUsingCodeGenerationContext()
    {
        // Arrange
        var contexts = new List<CodeGenerationContext>();

        // Create context for backend
        var backendContext = new CodeGenerationContext
        {
            Language = "Go",
            Framework = "Gin",
            TestCoverageRequirement = 0.9f
        };
        backendContext.AddRequirement("Create microservice architecture");
        backendContext.AddDependency("github.com/gin-gonic/gin");
        backendContext.AddGeneratedFile("main.go", "package main\n\nfunc main() { }");
        backendContext.AddGeneratedFile("handlers/user.go", "package handlers");
        contexts.Add(backendContext);

        // Create context for frontend
        var frontendContext = new CodeGenerationContext
        {
            Language = "TypeScript",
            Framework = "React",
            TestCoverageRequirement = 0.75f
        };
        frontendContext.AddRequirement("Create responsive UI");
        frontendContext.AddDependency("react");
        frontendContext.AddDependency("@types/react");
        frontendContext.AddGeneratedFile("App.tsx", "export const App = () => { }");
        contexts.Add(frontendContext);

        // Create context for mobile
        var mobileContext = new CodeGenerationContext
        {
            Language = "Kotlin",
            Framework = "Android",
            TestCoverageRequirement = 0.7f
        };
        mobileContext.AddRequirement("Create native Android app");
        mobileContext.AddDependency("androidx.core:core-ktx");
        mobileContext.AddGeneratedFile("MainActivity.kt", "class MainActivity");
        contexts.Add(mobileContext);

        // Assert - Verify all contexts
        Assert.Equal(3, contexts.Count);
        Assert.All(contexts, ctx => Assert.True(ctx.ValidateRequirements()));

        var languages = contexts.Select(c => c.Language).ToList();
        Assert.Contains("Go", languages);
        Assert.Contains("TypeScript", languages);
        Assert.Contains("Kotlin", languages);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCodeGenerationContextWithNullCollections()
    {
        // Arrange & Act
        var context = new CodeGenerationContext
        {
            Requirements = null!,
            GeneratedFiles = null!,
            Dependencies = null!,
            DesignPatterns = null!
        };

        // Assert
        Assert.Null(context.Requirements);
        Assert.Null(context.GeneratedFiles);
        Assert.Null(context.Dependencies);
        Assert.Null(context.DesignPatterns);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCodeGenerationContextWithUnicodeContent()
    {
        // Arrange
        var context = new CodeGenerationContext
        {
            Language = "Python 🐍",
            Framework = "Django 🎸"
        };

        // Act
        context.AddRequirement("支持多语言 🌐");
        context.AddDependency("パッケージ-1.0");
        context.AddGeneratedFile("文件.py", "# 中文注释\nprint('Hello 世界')");
        context.AddDesignPattern("Паттерн наблюдатель");

        // Assert
        Assert.Contains("🐍", context.Language);
        Assert.Contains("🎸", context.Framework);
        Assert.Contains("🌐", context.Requirements[0]);
        Assert.Contains("パッケージ", context.Dependencies[0]);
        Assert.Contains("世界", context.GeneratedFiles["文件.py"]);
        Assert.Contains("Паттерн", context.DesignPatterns[0]);
    }

    [Fact]
    public void ShouldAccept_WhenUsingCodeGenerationContextWithExtremeValues()
    {
        // Arrange & Act
        var context = new CodeGenerationContext
        {
            TestCoverageRequirement = -0.5f, // Invalid percentage
            QualityMetrics = new CodeQualityMetrics
            {
                LinesOfCode = -100, // Negative lines
                CyclomaticComplexity = int.MaxValue,
                TestCoverage = 1.5f // Over 100%
            }
        };

        // Assert - No validation in the class itself
        Assert.Equal(-0.5f, context.TestCoverageRequirement);
        Assert.Equal(-100, context.QualityMetrics.LinesOfCode);
        Assert.Equal(int.MaxValue, context.QualityMetrics.CyclomaticComplexity);
        Assert.Equal(1.5f, context.QualityMetrics.TestCoverage);
    }

    [Fact]
    public void ShouldAccept_WhenUsingCodeQualityMetricsWithNullIssuesByCategory()
    {
        // Arrange
        var metrics = new CodeQualityMetrics();

        // Act
        var nullMetrics = new CodeQualityMetrics { IssuesByCategory = null! };

        // Assert
        Assert.Null(nullMetrics.IssuesByCategory);
    }

    [Fact]
    public void ShouldAccept_WhenUsingCodeGenerationContextWithVeryLongFileName()
    {
        // Arrange
        var context = new CodeGenerationContext();
        var longFileName = string.Join("/", Enumerable.Range(1, 50).Select(i => $"folder{i}")) + "/VeryLongFileName.cs";

        // Act
        context.AddGeneratedFile(longFileName, "// Content");

        // Assert
        Assert.Single(context.GeneratedFiles);
        Assert.Contains(longFileName, context.GeneratedFiles.Keys);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingCodeGenerationContextToString()
    {
        // Arrange
        var context = new CodeGenerationContext { Language = "Java" };

        // Act
        var stringRepresentation = context.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("CodeGenerationContext", stringRepresentation);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingCodeQualityMetricsToString()
    {
        // Arrange
        var metrics = new CodeQualityMetrics { LinesOfCode = 1000 };

        // Act
        var stringRepresentation = metrics.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("CodeQualityMetrics", stringRepresentation);
    }

    #endregion
}
