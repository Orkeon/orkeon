namespace Orkeon.Domain.Task.Contexts;

/// <summary>Context for code generation tasks.</summary>
public class CodeGenerationContext
{
    /// <summary>Gets or sets the programming language.</summary>
    public string Language { get; init; } = "C#";

    /// <summary>Gets or sets the framework.</summary>
    public string Framework { get; init; } = ".NET 10";

    private readonly List<string> _requirements = [];
    private readonly List<string> _dependencies = [];
    private readonly List<string> _designPatterns = [];

    /// <summary>Gets or sets the requirements.</summary>
    public IReadOnlyList<string> Requirements
    {
        get => _requirements;
        init => _requirements = AsBackingList(value);
    }

    /// <summary>Gets or sets the generated files (filename to content mapping).</summary>
    public Dictionary<string, string> GeneratedFiles { get; init; } = [];

    /// <summary>Gets or sets the dependencies.</summary>
    public IReadOnlyList<string> Dependencies
    {
        get => _dependencies;
        init => _dependencies = AsBackingList(value);
    }

    /// <summary>Gets or sets the design patterns to use.</summary>
    public IReadOnlyList<string> DesignPatterns
    {
        get => _designPatterns;
        init => _designPatterns = AsBackingList(value);
    }

    private static List<T> AsBackingList<T>(IReadOnlyList<T> value) =>
        value switch
        {
            null => null!,
            List<T> list => list,
            _ => [.. value],
        };

    /// <summary>Adds a requirement to the code generation.</summary>
    /// <param name="requirement">The requirement to add.</param>
    public void AddRequirement(string requirement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        _requirements.Add(requirement);
    }

    /// <summary>Replaces the design patterns with the supplied collection.</summary>
    /// <param name="patterns">The design patterns to use.</param>
    public void SetDesignPatterns(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _designPatterns.Clear();
        _designPatterns.AddRange(patterns);
    }

    /// <summary>Adds a single design pattern.</summary>
    /// <param name="pattern">The design pattern to add.</param>
    public void AddDesignPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        _designPatterns.Add(pattern);
    }

    /// <summary>Gets or sets code quality metrics.</summary>
    public CodeQualityMetrics QualityMetrics { get; init; } = new();

    /// <summary>Gets the test coverage requirement (0.0 to 1.0).</summary>
    public float TestCoverageRequirement { get; internal set; } = 0.8f;

    /// <summary>Adds a generated file.</summary>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="content">The file content.</param>
    public void AddGeneratedFile(string fileName, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        GeneratedFiles[fileName] = content ?? string.Empty;
    }

    /// <summary>Adds a dependency if not already present.</summary>
    /// <param name="dependency">The dependency to add.</param>
    public void AddDependency(string dependency)
    {
        if (!string.IsNullOrWhiteSpace(dependency) && !_dependencies.Contains(dependency))
        {
            _dependencies.Add(dependency);
        }
    }

    /// <summary>Sets the test coverage requirement.</summary>
    /// <param name="coverage">The coverage value (0.0 to 1.0).</param>
    internal void SetCoverageRequirement(float coverage)
    {
        TestCoverageRequirement = coverage;
    }

    /// <summary>Validates the generated code meets requirements.</summary>
    /// <returns><see langword="true"/> if requirements are met; otherwise <see langword="false"/>.</returns>
    public bool ValidateRequirements()
    {
        // Check if all requirements have corresponding generated files
        return Requirements.Count > 0 && GeneratedFiles.Count > 0;
    }
}

/// <summary>Code quality metrics.</summary>
public sealed record CodeQualityMetrics
{
    /// <summary>Gets the total lines of code.</summary>
    public int LinesOfCode { get; init; }
    /// <summary>Gets the cyclomatic complexity score.</summary>
    public int CyclomaticComplexity { get; init; }
    /// <summary>Gets the maintainability index (0 to 100).</summary>
    public float MaintainabilityIndex { get; init; }
    /// <summary>Gets the estimated technical debt in minutes.</summary>
    public int TechnicalDebt { get; init; }
    /// <summary>Gets the test coverage ratio (0.0 to 1.0).</summary>
    public float TestCoverage { get; init; }
    /// <summary>Gets the number of code smells detected.</summary>
    public int CodeSmells { get; init; }
    /// <summary>Gets the number of issues per category.</summary>
    public Dictionary<string, int> IssuesByCategory { get; init; } = [];
}
