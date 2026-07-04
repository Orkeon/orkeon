using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Common;
using DomainTask = Orkeon.Domain.Task;

namespace Orkeon.Domain.Tests.Task;

public class CodeGenerationTaskTests
{
    private static readonly string[] AdapterPatternArray = ["Adapter Pattern"];
    private static readonly string[] MvcRepositoryPatterns = ["MVC", "Repository"];

    // Test doubles
    private class TestTaskCallback : ITaskCallback
    {
        public List<string> CallbackEvents { get; } = [];

        public System.Threading.Tasks.Task OnTaskStartAsync(ICrewTask task)
        {
            CallbackEvents.Add($"Started: {task.TaskId}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskCompletedAsync(ICrewTask task, TaskOutput output)
        {
            CallbackEvents.Add($"Completed: {task.TaskId}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskFailedAsync(ICrewTask task, string error)
        {
            CallbackEvents.Add($"Failed: {task.TaskId} - {error}");
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private class TestCodeTemplate
    {
        public string Language { get; set; } = "csharp";
        public string Type { get; set; } = "class";
        public string Name { get; set; } = "TestClass";
    }

    [Fact]
    public void ShouldInitializeTask_WhenConstructingWithValidParameters()
    {
        // Arrange
        var description = TaskDescription.From("Generate REST API controller");
        var expectedOutput = ExpectedOutput.From("Generated controller code");
        var language = "C#";
        var framework = ".NET 9";

        // Act
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            description,
            expectedOutput,
            language,
            framework);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.NotNull(task.Context);
        var context = task.TypedContext;
        Assert.Equal(language, context.Language);
        Assert.Equal(framework, context.Framework);
        Assert.Equal(Orkeon.Domain.Task.ValueObjects.TaskStatus.Pending, task.Status);
        Assert.NotEqual(default(TaskId), task.Id);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenConstructingWithAllOptionalParameters()
    {
        // Arrange
        var description = TaskDescription.From("Generate microservice");
        var expectedOutput = ExpectedOutput.From("Complete microservice project");
        var language = "Java";
        var framework = "Spring Boot";
        var priority = TaskPriority.High;
        var asyncExecution = true;
        var outputJson = JsonSchema.From("{\"type\":\"object\",\"properties\":{\"files\":{\"type\":\"array\"}}}");
        var outputPydantic = typeof(TestCodeTemplate);
        var outputFile = "generated_code.zip";
        var callback = new TestTaskCallback();
        var humanInput = true;

        // Act
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            description,
            expectedOutput,
            language,
            framework,
            priority,
            new TaskOutputOptions
            {
                AsyncExecution = asyncExecution,
                OutputJson = outputJson,
                OutputPydantic = outputPydantic,
                OutputFile = outputFile,
                Callback = callback,
                HumanInput = humanInput
            });

        // Assert
        Assert.Equal(priority, task.Priority);
        Assert.Equal(asyncExecution, task.AsyncExecution);
        Assert.Equal(outputJson, task.OutputJson);
        Assert.Equal(outputPydantic, task.OutputPydantic);
        Assert.Equal(outputFile, task.OutputFile);
        Assert.Equal(callback, task.Callback);
        Assert.Equal(humanInput, task.HumanInput);
        var context = task.TypedContext;
        Assert.Equal(language, context.Language);
        Assert.Equal(framework, context.Framework);
    }

    [Fact]
    public void ShouldUseCSharpAndDotNet9_WhenConstructingWithDefaultParameters()
    {
        // Arrange
        var description = TaskDescription.From("Generate code");
        var expectedOutput = ExpectedOutput.From("Generated code");

        // Act
        var task = DomainTask.CodeGenerationTask.Create(TaskId.Create(), description, expectedOutput);

        // Assert
        var context = task.TypedContext;
        Assert.Equal("C#", context.Language);
        Assert.Equal(".NET 10", context.Framework);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullDescription()
    {
        // Arrange
        var expectedOutput = ExpectedOutput.From("Generated code");
        var language = "python";
        var requirements = "Django REST API";

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => DomainTask.CodeGenerationTask.Create(TaskId.Create(), null!, expectedOutput, language, requirements));
        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingRequirementWithValidRequirement()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate API"),
            ExpectedOutput.From("API code"),
            "Go",
            "Gin Framework");
        var requirement = "Implement authentication middleware";

        // Act
        task.AddRequirement(requirement);

        // Assert
        var context = task.TypedContext;
        Assert.Contains(requirement, context.Requirements);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenAddingRequirementWithEmptyRequirement()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate GraphQL schema"),
            ExpectedOutput.From("GraphQL schema definition"));

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => task.AddRequirement(""));
        Assert.Equal("requirement", exception.ParamName);
    }

    [Fact]
    public void ShouldAddAll_WhenAddingRequirementsWithMultipleRequirements()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate entity"),
            ExpectedOutput.From("Entity code"),
            "C#",
            ".NET 9");
        var requirements = new[] { "Use validation", "Support serialization", "Implement IEquatable" };

        // Act
        task.AddRequirements(requirements);

        // Assert
        var context = task.TypedContext;
        foreach (var req in requirements)
        {
            Assert.Contains(req, context.Requirements);
        }
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenAddingRequirementsWithNullList()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate code"),
            ExpectedOutput.From("Code output"),
            "TypeScript",
            "React");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => task.AddRequirements(null!));
        Assert.Equal("requirements", exception.ParamName);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingGeneratedFile()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate service"),
            ExpectedOutput.From("Service implementation"),
            "Java",
            "Spring Boot");
        var fileName = "UserService.java";
        var content = "@Service public class UserService { }";

        // Act
        task.AddGeneratedFile(fileName, content);

        // Assert
        var context = task.TypedContext;
        Assert.Contains(fileName, context.GeneratedFiles.Keys);
        Assert.Equal(content, context.GeneratedFiles[fileName]);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingCodeDependency()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate controller"),
            ExpectedOutput.From("Controller code"),
            "Kotlin",
            "Ktor");
        var dependency = "io.ktor:ktor-server-core";

        // Act
        task.AddCodeDependency(dependency);

        // Assert
        var context = task.TypedContext;
        Assert.Contains(dependency, context.Dependencies);
    }

    [Fact]
    public void ShouldReplaceExisting_WhenSettingDesignPatterns()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate module"),
            ExpectedOutput.From("Module code"),
            "Python",
            "FastAPI");
        var patterns = new[] { "Repository Pattern", "Factory Pattern", "Singleton" };

        // Act
        task.SetDesignPatterns(patterns);

        // Assert
        var context = task.TypedContext;
        Assert.Equal(patterns.Length, context.DesignPatterns.Count);
        foreach (var pattern in patterns)
        {
            Assert.Contains(pattern, context.DesignPatterns);
        }
    }

    [Fact]
    public void ShouldAddAll_WhenAddingCodeDependencyWithMultiple()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate project"),
            ExpectedOutput.From("Project structure"),
            "JavaScript",
            "Node.js");
        var dependencies = new[] { "express", "mongoose", "jsonwebtoken" };

        // Act
        foreach (var dep in dependencies)
        {
            task.AddCodeDependency(dep);
        }

        // Assert
        var context = task.TypedContext;
        Assert.Equal(dependencies.Length, context.Dependencies.Count);
        foreach (var dep in dependencies)
        {
            Assert.Contains(dep, context.Dependencies);
        }
    }

    [Fact]
    public void ShouldUpdateContext_WhenUsingCompleteTaskWithGeneratedFiles()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate tests"),
            ExpectedOutput.From("Unit tests"),
            "C#",
            ".NET 9");
        task.AssignTo(Orkeon.Domain.Common.AgentId.Create());
        task.Start(task.AssignedAgent!);

        var fileName1 = "UserServiceTests.cs";
        var content1 = "public class UserServiceTests { }";
        var fileName2 = "ProductServiceTests.cs";
        var content2 = "public class ProductServiceTests { }";

        // Act
        task.AddGeneratedFile(fileName1, content1);
        task.AddGeneratedFile(fileName2, content2);
        task.Complete(task.AssignedAgent!, TaskOutput.Text("Generated 2 test files"));

        // Assert
        var context = task.TypedContext;
        Assert.Equal(2, context.GeneratedFiles.Count);
        Assert.Equal(content1, context.GeneratedFiles[fileName1]);
        Assert.Equal(content2, context.GeneratedFiles[fileName2]);
        Assert.Equal(Orkeon.Domain.Task.ValueObjects.TaskStatus.Completed, task.Status);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSettingDesignPatternsWithNull()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate model"),
            ExpectedOutput.From("Domain model"),
            "F#",
            ".NET 9");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => task.SetDesignPatterns(null!));
        Assert.Equal("patterns", exception.ParamName);
    }

    [Fact]
    public void ShouldBeModifiableBeforeCompletion_WhenUsingContext()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate API client"),
            ExpectedOutput.From("API client library"),
            "TypeScript",
            "Node.js");

        // Act
        task.AddRequirement("Type-safe request/response");
        task.AddCodeDependency("axios");
        task.SetDesignPatterns(AdapterPatternArray);
        task.AddGeneratedFile("client.ts", "export class ApiClient { }");

        // Assert
        var context = task.TypedContext;
        Assert.Single(context.Requirements);
        Assert.Single(context.Dependencies);
        Assert.Single(context.DesignPatterns);
        Assert.Single(context.GeneratedFiles);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMultipleLanguageSupport()
    {
        // Arrange
        var languageFrameworks = new[]
        {
            ("C#", ".NET 9"),
            ("Java", "Spring Boot"),
            ("Python", "Django"),
            ("JavaScript", "Node.js"),
            ("Go", "Gin"),
            ("Rust", "Actix")
        };

        // Act
        var tasks = languageFrameworks.Select(lf => DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From($"Generate {lf.Item1} code"),
            ExpectedOutput.From($"{lf.Item1} implementation"),
            lf.Item1,
            lf.Item2)).ToList();

        // Assert
        Assert.Equal(languageFrameworks.Length, tasks.Count);
        for (int i = 0; i < languageFrameworks.Length; i++)
        {
            var context = tasks[i].TypedContext;
            Assert.Equal(languageFrameworks[i].Item1, context.Language);
            Assert.Equal(languageFrameworks[i].Item2, context.Framework);
        }
    }

    [Fact]
    public void ShouldPersistAcrossTaskLifecycle_WhenUsingContext()
    {
        // Arrange
        var task = DomainTask.CodeGenerationTask.Create(
            TaskId.Create(),
            TaskDescription.From("Generate full application"),
            ExpectedOutput.From("Complete application"),
            "Java",
            "Spring Boot");

        // Add context before assignment
        task.AddRequirement("Use reactive programming");
        task.SetDesignPatterns(MvcRepositoryPatterns);

        // Act - Assign and start
        task.AssignTo(Orkeon.Domain.Common.AgentId.Create());
        task.Start(task.AssignedAgent!);

        // Add more context during execution
        task.AddCodeDependency("spring-boot-starter-webflux");
        task.AddGeneratedFile("App.java", "@SpringBootApplication public class App { }");

        // Complete the task
        task.Complete(task.AssignedAgent!, TaskOutput.Text("Application generated"));

        // Assert - Context should contain all additions
        var context = task.TypedContext;
        Assert.Contains("Use reactive programming", context.Requirements);
        Assert.Equal(2, context.DesignPatterns.Count);
        Assert.Contains("spring-boot-starter-webflux", context.Dependencies);
        Assert.Single(context.GeneratedFiles);
        Assert.Equal(Orkeon.Domain.Task.ValueObjects.TaskStatus.Completed, task.Status);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskWithSpecificFramework()
    {
        // Arrange & Act
        var tasks = new[]
        {
            DomainTask.CodeGenerationTask.Create(
                TaskId.Create(),
                TaskDescription.From("Generate React app"),
                ExpectedOutput.From("React application"),
                "JavaScript",
                "React"),
            DomainTask.CodeGenerationTask.Create(
                TaskId.Create(),
                TaskDescription.From("Generate Flask API"),
                ExpectedOutput.From("Flask REST API"),
                "Python",
                "Flask"),
            DomainTask.CodeGenerationTask.Create(
                TaskId.Create(),
                TaskDescription.From("Generate Blazor app"),
                ExpectedOutput.From("Blazor WebAssembly app"),
                "C#",
                "Blazor")
        };

        // Assert
        Assert.Equal("React", tasks[0].TypedContext.Framework);
        Assert.Equal("Flask", tasks[1].TypedContext.Framework);
        Assert.Equal("Blazor", tasks[2].TypedContext.Framework);
    }
}
