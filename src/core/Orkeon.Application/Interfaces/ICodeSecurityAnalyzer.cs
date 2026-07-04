namespace Orkeon.Application.Interfaces;

/// <summary>
/// Risk level for a security violation.
/// </summary>
public enum SecurityRiskLevel
{
    /// <summary>None.</summary>
    None,
    /// <summary>Low.</summary>
    Low,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>High.</summary>
    High,
    /// <summary>Critical.</summary>
    Critical
}

/// <summary>
/// A single security violation found during code analysis.
/// </summary>
public record SecurityViolation
{
    /// <summary>Name of the rule that was violated.</summary>
    public string Rule { get; init; } = string.Empty;

    /// <summary>Human-readable description of the violation.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Risk level of this violation.</summary>
    public SecurityRiskLevel Risk { get; init; }

    /// <summary>Line number where the violation was found (1-based).</summary>
    public int LineNumber { get; init; }

    /// <summary>The code snippet that triggered the violation.</summary>
    public string? CodeSnippet { get; init; }
}

/// <summary>
/// Options controlling what is allowed during security analysis.
/// </summary>
public class SecurityAnalysisOptions
{
    /// <summary>Allow file I/O operations.</summary>
    public bool AllowFileIO { get; set; }

    /// <summary>Allow networking operations.</summary>
    public bool AllowNetworking { get; set; }

    /// <summary>Allow reflection APIs.</summary>
    public bool AllowReflection { get; set; }

    /// <summary>Allow spawning processes.</summary>
    public bool AllowProcessExec { get; set; }

    /// <summary>Allow unsafe code blocks.</summary>
    public bool AllowUnsafeCode { get; set; }

    /// <summary>Namespaces that are allowed in using directives.</summary>
    public IReadOnlyList<string> AllowedNamespaces { get; init; } =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text",
        "System.Text.Json",
        "System.Text.RegularExpressions",
        "System.Math"
    ];

    /// <summary>Types that are always blocked.</summary>
    public IReadOnlyList<string> BlockedTypes { get; init; } =
    [
        "System.Diagnostics.Process",
        "System.IO.File",
        "System.IO.Directory",
        "System.Reflection.Assembly",
        "System.Runtime.InteropServices.Marshal",
        "System.Net.Sockets.Socket",
        "System.AppDomain"
    ];
}

/// <summary>
/// Full security report produced by analyzing code.
/// </summary>
public record CodeSecurityReport
{
    /// <summary>Whether the code is safe to execute based on the configured options.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>All violations found.</summary>
    public IReadOnlyList<SecurityViolation> Violations { get; init; } = Array.Empty<SecurityViolation>();

    /// <summary>Highest risk level among all violations.</summary>
    public SecurityRiskLevel OverallRisk { get; init; }
}

/// <summary>
/// Statically analyzes code for security violations before execution.
/// </summary>
public interface ICodeSecurityAnalyzer
{
    /// <summary>
    /// Analyzes the given code and returns a security report.
    /// </summary>
    CodeSecurityReport Analyze(string code, SecurityAnalysisOptions? options = null);
}
