using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task;

/// <summary>
/// Task specialized for code generation activities.
/// </summary>
public class CodeGenerationTask : CrewTaskBase<CodeGenerationContext>
{
    /// <summary>
    /// Private constructor for code generation task.
    /// </summary>
    private CodeGenerationTask(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        string language,
        string framework,
        TaskPriority priority,
        TaskOutputOptions? outputOptions)
        : base(
            id,
            description,
            expectedOutput,
            priority,
            outputOptions,
            new CodeGenerationContext
            {
                Language = language,
                Framework = framework
            })
    {
    }

    /// <summary>
    /// Creates a new code generation task with the specified parameters.
    /// </summary>
    /// <param name="id">The unique task identifier.</param>
    /// <param name="description">The task description.</param>
    /// <param name="expectedOutput">The expected output description.</param>
    /// <param name="language">The programming language.</param>
    /// <param name="framework">The target framework.</param>
    /// <param name="priority">The task priority.</param>
    /// <param name="outputOptions">Optional output configuration.</param>
    /// <returns>A new <see cref="CodeGenerationTask"/> instance.</returns>
    public static CodeGenerationTask Create(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        string language = "C#",
        string framework = ".NET 10",
        TaskPriority? priority = null,
        TaskOutputOptions? outputOptions = null)
    {
        return new CodeGenerationTask(id, description, expectedOutput, language, framework, priority ?? TaskPriority.Normal, outputOptions);
    }

    /// <summary>
    /// Adds a requirement to the code generation.
    /// </summary>
    public void AddRequirement(string requirement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);

        UpdateContext(ctx => ctx.AddRequirement(requirement));
    }

    /// <summary>
    /// Adds multiple requirements.
    /// </summary>
    public void AddRequirements(IEnumerable<string> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        foreach (var requirement in requirements.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            AddRequirement(requirement);
        }
    }

    /// <summary>
    /// Adds a generated file.
    /// </summary>
    public void AddGeneratedFile(string fileName, string content)
    {
        UpdateContext(ctx => ctx.AddGeneratedFile(fileName, content));
    }

    /// <summary>
    /// Adds a dependency.
    /// </summary>
    public void AddCodeDependency(string dependency)
    {
        UpdateContext(ctx => ctx.AddDependency(dependency));
    }

    /// <summary>
    /// Sets the design patterns to use.
    /// </summary>
    public void SetDesignPatterns(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        UpdateContext(ctx => ctx.SetDesignPatterns(patterns));
    }

    /// <summary>
    /// Updates quality metrics.
    /// </summary>
    public void UpdateQualityMetrics(Action<CodeQualityMetrics> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        UpdateContext(ctx => updateAction(ctx.QualityMetrics));
    }

    /// <summary>
    /// Sets the test coverage requirement.
    /// </summary>
    public void SetTestCoverageRequirement(float coverage)
    {
        if (coverage < 0 || coverage > 1)
            throw new ArgumentOutOfRangeException(nameof(coverage), "Coverage must be between 0 and 1");

        UpdateContext(ctx => ctx.SetCoverageRequirement(coverage));
    }

    /// <summary>
    /// Validates that all requirements are met.
    /// </summary>
    public bool ValidateRequirements()
    {
        return TypedContext.ValidateRequirements();
    }

    /// <summary>
    /// Gets the generated files.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetGeneratedFiles()
    {
        return TypedContext.GeneratedFiles.AsReadOnly();
    }

    /// <summary>
    /// Gets a summary of the code generation context.
    /// </summary>
    public override string GetContextSummary()
    {
        var context = TypedContext;
        return $"Code generation in {context.Language}/{context.Framework} with {context.Requirements.Count} requirements and {context.GeneratedFiles.Count} files";
    }
}
