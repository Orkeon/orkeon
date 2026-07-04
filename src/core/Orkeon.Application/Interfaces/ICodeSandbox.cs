using Orkeon.Domain.Constants.Http;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// Request to execute code in a sandbox environment.
/// </summary>
public record SandboxExecutionRequest
{
    /// <summary>Code to execute.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Language of the code (default: csharp).</summary>
    public string Language { get; init; } = "csharp";

    /// <summary>Maximum execution time.</summary>
    public TimeSpan Timeout { get; init; } = HttpDefaults.DefaultHttpTimeout;

    /// <summary>Maximum memory in bytes (default: 256 MB).</summary>
    public long MaxMemoryBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>Maximum CPU percentage.</summary>
    public int MaxCpuPercent { get; init; } = 50;

    /// <summary>Maximum output bytes (default: 50 KB).</summary>
    public long MaxOutputBytes { get; init; } = 50_000;

    /// <summary>Optional list of allowed namespaces.</summary>
    public IReadOnlyList<string>? AllowedNamespaces { get; init; }

    /// <summary>Optional variables to inject.</summary>
    public IReadOnlyDictionary<string, object>? Variables { get; init; }

    /// <summary>Permissions granted to the sandbox.</summary>
    public SandboxPermissions Permissions { get; init; } = new();
}

/// <summary>
/// Result of a sandbox code execution.
/// </summary>
public record SandboxExecutionResult
{
    /// <summary>Whether execution completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Standard output from execution.</summary>
    public string? Output { get; init; }

    /// <summary>Error message if execution failed.</summary>
    public string? Error { get; init; }

    /// <summary>Process exit code.</summary>
    public int ExitCode { get; init; }

    /// <summary>Total execution duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Peak memory usage in bytes.</summary>
    public long MemoryUsedBytes { get; init; }

    /// <summary>CPU time consumed in milliseconds.</summary>
    public long CpuTimeMs { get; init; }

    /// <summary>Whether execution was terminated due to timeout.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Whether execution was terminated due to memory limit.</summary>
    public bool MemoryExceeded { get; init; }
}

/// <summary>
/// Permissions controlling what the sandbox can access.
/// </summary>
public record SandboxPermissions
{
    /// <summary>Allow reading files.</summary>
    public bool AllowFileRead { get; init; }

    /// <summary>Allow writing files.</summary>
    public bool AllowFileWrite { get; init; }

    /// <summary>Allow network access.</summary>
    public bool AllowNetworkAccess { get; init; }

    /// <summary>Allow spawning child processes.</summary>
    public bool AllowProcessExec { get; init; }

    /// <summary>Allow reflection APIs.</summary>
    public bool AllowReflection { get; init; }

    /// <summary>Working directory for the sandbox.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>List of allowed file paths.</summary>
    public IReadOnlyList<string>? AllowedPaths { get; init; }
}

/// <summary>
/// Describes what a sandbox implementation supports.
/// </summary>
public record SandboxCapabilities
{
    /// <summary>Whether memory limits can be enforced.</summary>
    public bool SupportsMemoryLimits { get; init; }

    /// <summary>Whether CPU limits can be enforced.</summary>
    public bool SupportsCpuLimits { get; init; }

    /// <summary>Whether network isolation is supported.</summary>
    public bool SupportsNetworkIsolation { get; init; }

    /// <summary>Whether file system isolation is supported.</summary>
    public bool SupportsFileSystemIsolation { get; init; }

    /// <summary>Type of sandbox (e.g., "process", "docker").</summary>
    public string SandboxType { get; init; } = string.Empty;
}

/// <summary>
/// Executes code in an isolated sandbox environment with resource limits.
/// </summary>
public interface ICodeSandbox : IAsyncDisposable
{
    /// <summary>
    /// Executes the given code in the sandbox.
    /// </summary>
    System.Threading.Tasks.Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the sandbox runtime is available (e.g., dotnet CLI, Docker).
    /// </summary>
    System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the capabilities of this sandbox implementation.
    /// </summary>
    SandboxCapabilities Capabilities { get; }
}
