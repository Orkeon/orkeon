using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// Docker-based sandbox that runs C# code in an isolated container.
/// Uses <c>docker run</c> with memory, CPU, and network restrictions.
/// File I/O is routed through <see cref="IFileSystemService"/> via the <c>/sandbox/docker</c> VFS mount.
/// The physical path is extracted via <see cref="IFileSystemService.ResolveAndValidate"/> only at the
/// Docker boundary (for the <c>-v</c> volume argument), never before.
/// </summary>
public partial class DockerSandbox : ICodeSandbox
{
    private readonly ILogger<DockerSandbox> _logger;
    private readonly SandboxOptions _sandboxOptions;
    private readonly DockerSandboxOptions _dockerOptions;
    private readonly IFileSystemService _fs;

    /// <summary>Virtual mount path for Docker sandbox run directories (under /sandbox).</summary>
    private const string SandboxMount = "/sandbox/docker";

    private static readonly string[] KnownDockerPaths = ["/usr/bin/docker", "/usr/local/bin/docker"];

    /// <inheritdoc />
    public SandboxCapabilities Capabilities { get; } = new()
    {
        SupportsMemoryLimits = true,
        SupportsCpuLimits = true,
        SupportsNetworkIsolation = true,
        SupportsFileSystemIsolation = true,
        SandboxType = "docker"
    };

    /// <summary>Initializes a new instance of <see cref="DockerSandbox"/>.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="sandboxOptions">General sandbox configuration options.</param>
    /// <param name="dockerOptions">Docker-specific configuration options.</param>
    /// <param name="fs">The virtual file system service used to create and clean up run directories.</param>
    public DockerSandbox(
        ILogger<DockerSandbox> logger,
        IOptions<SandboxOptions> sandboxOptions,
        IOptions<DockerSandboxOptions> dockerOptions,
        IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(sandboxOptions);
        ArgumentNullException.ThrowIfNull(dockerOptions);
        _logger = logger;
        _sandboxOptions = sandboxOptions.Value;
        _dockerOptions = dockerOptions.Value;
        _fs = fs;
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure launching/querying the docker CLI is reported as 'not available' (false) rather than propagated.")]
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var process = new SysProcess();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = GetDockerExecutable(),
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(SandboxDefaults.ContainerTimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // R10.3 (ORG-012). WaitForExitAsync only stops *waiting* on cancellation  —
                // the probe child process would otherwise linger. Kill it (entire tree,
                // best effort — TryKillProcess tolerates an already-exited process).
                TryKillProcess(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Sandbox-execution fault barrier: any error running untrusted code in Docker is logged and converted into a failed SandboxExecutionResult so one execution cannot crash the agent loop (cancellation/timeout is handled separately).")]
    public Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<SandboxExecutionResult> ExecuteCoreAsync()
        {
        var sw = Stopwatch.StartNew();
        var vRunDir = $"{SandboxMount}/{Guid.NewGuid():N}";

        try
        {
            await _fs.CreateDirectoryAsync(vRunDir, ct).ConfigureAwait(false);
            LogCreatedDockerSandboxTempDirectory(vRunDir);

            // Write the project files via VFS
            await WriteProjectFilesAsync(vRunDir, request, ct).ConfigureAwait(false);

            // Boundary Docker: extract physical path for the -v volume argument.
            // Physical paths are only resolved here, at the Docker process boundary.
            var resolved = _fs.ResolveAndValidate(vRunDir,
                FileAccessRights.Read | FileAccessRights.Write);
            if (!resolved.IsAllowed)
                throw new InvalidOperationException(
                    $"Cannot mount sandbox directory into Docker: {resolved.DenialReason}");

            var physicalRunDir = resolved.ResolvedPath!;

            // Build docker run arguments
            var timeout = request.Timeout > TimeSpan.Zero
                ? request.Timeout
                : TimeSpan.FromSeconds(_sandboxOptions.TimeoutSeconds);

            var maxMemory = request.MaxMemoryBytes > 0
                ? request.MaxMemoryBytes
                : _sandboxOptions.MaxMemoryBytes;

            var args = BuildDockerArgs(physicalRunDir, request, maxMemory, timeout);

            // Run docker
            var result = await RunDockerAsync(args, timeout, ct).ConfigureAwait(false);

            sw.Stop();
            return result with { Duration = sw.Elapsed };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new SandboxExecutionResult
            {
                Success = false,
                Error = "Execution was cancelled",
                ExitCode = -1,
                Duration = sw.Elapsed,
                TimedOut = true
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogDockerSandboxExecutionFailed(ex);
            return new SandboxExecutionResult
            {
                Success = false,
                Error = $"Docker sandbox error: {ex.Message}",
                ExitCode = -1,
                Duration = sw.Elapsed
            };
        }
        finally
        {
            await CleanupRunDirectoryAsync(vRunDir).ConfigureAwait(false);
        }
        }
    }

    private async Task WriteProjectFilesAsync(
        string vRunDir, SandboxExecutionRequest request, CancellationToken ct)
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;
        await _fs.WriteAllTextAsync($"{vRunDir}/Sandbox.csproj", csproj, ct).ConfigureAwait(false);

        var programCs = HostProcessRunner.GenerateProgramCs(request.Code);
        await _fs.WriteAllTextAsync($"{vRunDir}/Program.cs", programCs, ct).ConfigureAwait(false);
    }

    private string BuildDockerArgs(
        string physicalRunDir, SandboxExecutionRequest request, long maxMemory, TimeSpan timeout)
    {
        var sb = new StringBuilder("run --rm");

        // Memory limit
        sb.Append(CultureInfo.InvariantCulture, $" --memory={maxMemory}");

        // CPU limit
        if (request.MaxCpuPercent > 0)
        {
            var cpus = request.MaxCpuPercent / 100.0;
            sb.Append(CultureInfo.InvariantCulture, $" --cpus={cpus:F2}");
        }

        // Network isolation
        if (!request.Permissions.AllowNetworkAccess)
        {
            sb.Append(" --network=none");
        }

        // Read-only filesystem unless write is allowed
        if (!request.Permissions.AllowFileWrite)
        {
            sb.Append(" --read-only");
            // Need tmpfs for dotnet to work
            sb.Append(" --tmpfs /tmp:rw,noexec,nosuid,size=256m");
        }

        // Mount source code — physicalRunDir was validated via ResolveAndValidate above
        sb.Append(CultureInfo.InvariantCulture, $" -v \"{physicalRunDir}:/app:ro\"");

        // Working directory
        sb.Append(" -w /tmp/build");

        // Timeout as stop timeout
        var stopTimeout = (int)timeout.TotalSeconds + 5;
        sb.Append(CultureInfo.InvariantCulture, $" --stop-timeout={stopTimeout}");

        // Image
        sb.Append(CultureInfo.InvariantCulture, $" {_dockerOptions.ImageName}");

        // Command: copy source, build, and run
        sb.Append(" sh -c \"cp -r /app/* /tmp/build/ && dotnet build -c Release --nologo -v q && dotnet bin/Release/net10.0/Sandbox.dll\"");

        return sb.ToString();
    }

    private static async Task<SandboxExecutionResult> RunDockerAsync(
        string args, TimeSpan timeout, CancellationToken ct)
    {
        using var process = new SysProcess();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = GetDockerExecutable(),
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Add extra time for docker overhead (build + run)
        cts.CancelAfter(timeout + TimeSpan.FromSeconds(SandboxDefaults.KillGracePeriodSeconds));

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            return new SandboxExecutionResult
            {
                Success = process.ExitCode == 0,
                Output = string.IsNullOrEmpty(output) ? null : output,
                Error = string.IsNullOrEmpty(error) ? null : error,
                ExitCode = process.ExitCode,
                TimedOut = false,
                MemoryExceeded = process.ExitCode == 137 // OOM kill
            };
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);

            return new SandboxExecutionResult
            {
                Success = false,
                Error = "Docker execution timed out",
                ExitCode = -1,
                TimedOut = true
            };
        }
    }

    /// <summary>
    /// Returns the absolute path to the docker executable.
    /// Absolute paths prevent PATH-hijacking attacks (S4036).
    /// </summary>
    private static string GetDockerExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var winPath = Path.Combine(programFiles, "Docker", "resources", "bin", "docker.exe");
            // OUT-OF-SCOPE: binary discovery on PATH, not a VFS mount.
            if (File.Exists(winPath))
                return winPath;
        }
        else
        {
            // OUT-OF-SCOPE: binary discovery on PATH, not a VFS mount.
            var existing = KnownDockerPaths.FirstOrDefault(File.Exists);
            if (existing != null)
                return existing;
        }

        // Fall back to bare name if absolute path cannot be resolved
        return "docker";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a failed Kill of a process that may already have exited is swallowed and must not mask the primary result.")]
    private static void TryKillProcess(SysProcess process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a failure deleting the sandbox temp directory is logged and swallowed so it cannot mask the execution result.")]
    private async Task CleanupRunDirectoryAsync(string vRunDir)
    {
        try
        {
            await _fs.DeleteAsync(vRunDir, recursive: true, CancellationToken.None)
                .ConfigureAwait(false);
            LogCleanedUpDockerSandboxTemp(vRunDir);
        }
        catch (Exception ex)
        {
            LogFailedToCleanUpDocker(ex, vRunDir);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Created Docker sandbox run directory: {RunDir}")]
    private partial void LogCreatedDockerSandboxTempDirectory(object RunDir);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Docker sandbox execution failed")]
    private partial void LogDockerSandboxExecutionFailed(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Cleaned up Docker sandbox run directory: {RunDir}")]
    private partial void LogCleanedUpDockerSandboxTemp(object RunDir);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to clean up Docker sandbox run directory: {RunDir}")]
    private partial void LogFailedToCleanUpDocker(Exception ex, object RunDir);

}
