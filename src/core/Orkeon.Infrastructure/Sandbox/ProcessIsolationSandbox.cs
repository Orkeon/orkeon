using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// Runs C# code in a child process on the HOST — there is NO OS-level isolation
/// (no namespaces, seccomp, chroot, cgroups, or network isolation). The child process runs
/// with the same privileges as the calling process and can read/write any path and reach the
/// network that the host can. Only a coarse memory-usage monitor and a restricted environment
/// are applied. This is a "host process runner" for TRUSTED code only.
/// <para>
/// Do NOT use this to execute LLM-generated or otherwise untrusted code: that path requires an
/// OS-isolating sandbox (<see cref="DockerSandbox"/>). Untrusted execution on this runner is
/// only reached via an explicit, noisy opt-in (<c>SandboxOptions.AllowHostExecution</c>).
/// </para>
/// Creates a temporary .NET project under the <c>/sandbox/process</c> VFS mount,
/// builds it, and runs the compiled DLL with restricted environment variables and memory monitoring.
/// </summary>
public partial class HostProcessRunner : ICodeSandbox
{
    private readonly ILogger<HostProcessRunner> _logger;
    private readonly SandboxOptions _options;
    private readonly IFileSystemService _fs;

    /// <summary>Virtual mount path for process sandbox run directories (under /sandbox).</summary>
    private const string SandboxMount = "/sandbox/process";

    /// <inheritdoc />
    public SandboxCapabilities Capabilities { get; } = new()
    {
        SupportsMemoryLimits = true,
        SupportsCpuLimits = false,
        SupportsNetworkIsolation = false,
        SupportsFileSystemIsolation = false,
        SandboxType = "process"
    };

    /// <summary>Initializes a new instance of <see cref="HostProcessRunner"/>.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="options">Sandbox configuration options.</param>
    /// <param name="fs">The virtual file system service used to create and clean up run directories.</param>
    public HostProcessRunner(
        ILogger<HostProcessRunner> logger,
        IOptions<SandboxOptions> options,
        IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _options = options.Value;
        _fs = fs;
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure launching/querying the dotnet CLI is reported as 'not available' (false) rather than propagated.")]
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var process = new SysProcess();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = GetDotnetExecutable(),
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(SandboxDefaults.ContainerTimeoutSeconds));

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Sandbox-execution fault barrier: any error compiling/running untrusted code is logged and converted into a failed SandboxExecutionResult so one execution cannot crash the agent loop (cancellation/timeout is handled separately).")]
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
            // REMOVED: File.SetUnixFileMode — Path.GetTempPath() respects user-scoped ACLs
            // (%USERPROFILE%\AppData\Local\Temp on Windows, /tmp with user umask on Linux).
            // Revisit if multi-tenant isolation becomes a requirement.
            LogCreatedSandboxTempDirectory(vRunDir);

            // Generate project files via VFS
            await GenerateProjectAsync(vRunDir, request, ct).ConfigureAwait(false);

            // Resolve the physical path for the dotnet CLI working directory
            var resolved = _fs.ResolveAndValidate(vRunDir,
                FileAccessRights.Read | FileAccessRights.Write);
            if (!resolved.IsAllowed)
                throw new InvalidOperationException(
                    $"Cannot access sandbox directory: {resolved.DenialReason}");

            var physicalRunDir = resolved.ResolvedPath!;

            // Build the project
            var buildResult = await RunDotnetCommandAsync(
                "build", physicalRunDir,
                TimeSpan.FromSeconds(SandboxDefaults.BuildTimeoutSeconds), ct).ConfigureAwait(false);

            if (buildResult.ExitCode != 0)
            {
                sw.Stop();
                var errorDetail = !string.IsNullOrWhiteSpace(buildResult.Error)
                    ? buildResult.Error
                    : buildResult.Output ?? "(no output captured)";
                return new SandboxExecutionResult
                {
                    Success = false,
                    Error = $"Compilation failed:\n{errorDetail}",
                    ExitCode = buildResult.ExitCode,
                    Duration = sw.Elapsed
                };
            }

            // Find the compiled DLL (physical path — inside the resolved sandbox dir)
            var dllPath = FindCompiledDll(physicalRunDir);
            if (dllPath == null)
            {
                sw.Stop();
                return new SandboxExecutionResult
                {
                    Success = false,
                    Error = "Could not find compiled DLL after build",
                    ExitCode = -1,
                    Duration = sw.Elapsed
                };
            }

            // Execute the compiled DLL with resource monitoring
            var timeout = request.Timeout > TimeSpan.Zero
                ? request.Timeout
                : TimeSpan.FromSeconds(_options.TimeoutSeconds);

            var execResult = await ExecuteCompiledAsync(dllPath, request, timeout, ct).ConfigureAwait(false);

            sw.Stop();
            return execResult with { Duration = sw.Elapsed };
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
            LogSandboxExecutionFailed(ex);
            return new SandboxExecutionResult
            {
                Success = false,
                Error = $"Sandbox error: {ex.Message}",
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

    private async Task GenerateProjectAsync(
        string vRunDir, SandboxExecutionRequest request, CancellationToken ct)
    {
        // Generate .csproj
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

        // Wrap user code in a Program.cs with try/catch
        var programCs = GenerateProgramCs(request.Code);
        await _fs.WriteAllTextAsync($"{vRunDir}/Program.cs", programCs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates a Program.cs file that wraps user code with standard using directives and try/catch error handling.
    /// </summary>
    /// <param name="userCode">The user-provided C# code to wrap.</param>
    /// <returns>The generated Program.cs content.</returns>
    public static string GenerateProgramCs(string userCode)
    {
        ArgumentNullException.ThrowIfNull(userCode);
        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Text;
            using System.Text.Json;
            using System.Text.RegularExpressions;

            try
            {
            {{IndentCode(userCode, "    ")}}
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Runtime error: {ex.GetType().Name}: {ex.Message}");
                Environment.ExitCode = 1;
            }
            """;
    }

    private static string IndentCode(string code, string indent)
    {
        var lines = code.Split('\n');
        return string.Join('\n', lines.Select(l => indent + l.TrimEnd('\r')));
    }

    private static async Task<(int ExitCode, string? Output, string? Error)> RunDotnetCommandAsync(
        string command, string workingDir, TimeSpan timeout, CancellationToken ct)
    {
        using var process = new SysProcess();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = GetDotnetExecutable(),
            Arguments = command,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Minimal environment
        ConfigureRestrictedEnvironment(process.StartInfo);

        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return (process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            return (-1, null, "Process timed out");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025",
        Justification = "The memory-monitor task captures the using-scoped 'process' and 'cts', but it is always awaited in the finally block before those instances are disposed (scope exits only after the await completes), so no background work touches a disposed instance. CA2025's syntactic check cannot see the deferred await through the local variable.")]
    private async Task<SandboxExecutionResult> ExecuteCompiledAsync(
        string dllPath, SandboxExecutionRequest request, TimeSpan timeout, CancellationToken ct)
    {
        using var process = CreateExecutionProcess(dllPath, request);
        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var maxMemory = request.MaxMemoryBytes > 0 ? request.MaxMemoryBytes : _options.MaxMemoryBytes;
        var memoryState = new MemoryMonitorState();

        var monitorTask = RunMemoryMonitorAsync(process, maxMemory, memoryState, cts.Token);
        try
        {
            var (outputBuilder, errorBuilder, timedOut) = await CaptureProcessOutputAsync(
                process, request, cts.Token).ConfigureAwait(false);

            return BuildExecutionResult(process, outputBuilder, errorBuilder,
                timedOut, memoryState.MemoryExceeded, memoryState.PeakMemory);
        }
        finally
        {
            // Always await the memory-monitor task before the using-scoped 'process' and
            // 'cts' it captures are disposed, so no background task touches a disposed instance.
            try { await monitorTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected on timeout/cancellation */ }
        }
    }

    private static SysProcess CreateExecutionProcess(string dllPath, SandboxExecutionRequest request)
    {
        var process = new SysProcess();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = GetDotnetExecutable(),
            Arguments = dllPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ConfigureRestrictedEnvironment(process.StartInfo);

        if (request.Permissions.WorkingDirectory != null)
            process.StartInfo.WorkingDirectory = request.Permissions.WorkingDirectory;

        return process;
    }

    private static void ConfigureRestrictedEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Clear();
        startInfo.Environment["PATH"] = GetMinimalPath();
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;

        if (OperatingSystem.IsWindows())
        {
            // Windows requires these environment variables for dotnet CLI to function.
            // TEMP/TMP point to the system temp directory by design: the sandbox process
            // needs a writable temp location to compile and run user code. The actual sandbox
            // work-dir is created uniquely per-execution in ExecuteAsync (via Guid) and on
            // non-Windows hosts has user-only permissions applied below (S108/S5443).
            startInfo.Environment["USERPROFILE"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            startInfo.Environment["TEMP"] = Path.GetTempPath(); // NOSONAR — required for dotnet CLI; sandbox dir itself is Guid-scoped
            startInfo.Environment["TMP"] = Path.GetTempPath(); // NOSONAR — required for dotnet CLI; sandbox dir itself is Guid-scoped

            // Required for dotnet SDK resolution and NuGet package cache
            PropagateEnvVar(startInfo, "SystemRoot");
            PropagateEnvVar(startInfo, "SystemDrive");
            PropagateEnvVar(startInfo, "APPDATA");
            PropagateEnvVar(startInfo, "LOCALAPPDATA");
            PropagateEnvVar(startInfo, "ProgramFiles");
            PropagateEnvVar(startInfo, "ProgramFiles(x86)");
            PropagateEnvVar(startInfo, "NUGET_PACKAGES");
        }
        else
        {
            startInfo.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    private static void PropagateEnvVar(ProcessStartInfo startInfo, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(value))
            startInfo.Environment[name] = value;
    }

    private sealed class MemoryMonitorState
    {
        /// <summary>Peak working set observed during monitoring.</summary>
        public long PeakMemory;

        /// <summary>Whether the memory limit was exceeded and the process was killed.</summary>
        public bool MemoryExceeded;
    }

    private static async Task RunMemoryMonitorAsync(
        SysProcess process, long maxMemory, MemoryMonitorState state, CancellationToken ct)
    {
        try
        {
            while (!process.HasExited && !ct.IsCancellationRequested)
            {
                if (!TryCheckMemory(process, maxMemory, state))
                    return;

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentionally swallowed — cancellation is the expected shutdown signal for the monitor loop
        }
    }

    private static bool TryCheckMemory(SysProcess process, long maxMemory, MemoryMonitorState state)
    {
        try
        {
            process.Refresh();
            var currentMemory = process.WorkingSet64;
            if (currentMemory > state.PeakMemory)
                state.PeakMemory = currentMemory;

            if (currentMemory <= maxMemory)
                return true;

            state.MemoryExceeded = true;
            TryKillProcess(process);
            return false;
        }
        catch (InvalidOperationException)
        {
            // Intentionally swallowed — process already exited, nothing to monitor
            return false;
        }
    }

    private async Task<(StringBuilder Output, StringBuilder Error, bool TimedOut)> CaptureProcessOutputAsync(
        SysProcess process, SandboxExecutionRequest request, CancellationToken ct)
    {
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var maxOutput = request.MaxOutputBytes > 0 ? request.MaxOutputBytes : _options.MaxOutputBytes;
        var timedOut = false;

        var outputTask = ReadLimitedAsync(process.StandardOutput, outputBuilder, maxOutput, ct);
        var errorTask = ReadLimitedAsync(process.StandardError, errorBuilder, maxOutput, ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TryKillProcess(process);
        }

        await WaitForTasksSafelyAsync(outputTask, errorTask).ConfigureAwait(false);
        return (outputBuilder, errorBuilder, timedOut);
    }

    private static async Task WaitForTasksSafelyAsync(params Task[] tasks)
    {
        foreach (var task in tasks)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // Intentionally swallowed — cleanup errors should not mask the original exception
            }
        }
    }

    private static SandboxExecutionResult BuildExecutionResult(
        SysProcess process, StringBuilder outputBuilder, StringBuilder errorBuilder,
        bool timedOut, bool memoryExceeded, long peakMemory)
    {
        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var exitCode = timedOut || memoryExceeded ? -1 : process.ExitCode;

        return new SandboxExecutionResult
        {
            Success = exitCode == 0 && !timedOut && !memoryExceeded,
            Output = string.IsNullOrEmpty(output) ? null : output,
            Error = BuildErrorMessage(error, timedOut, memoryExceeded),
            ExitCode = exitCode,
            MemoryUsedBytes = peakMemory,
            CpuTimeMs = GetCpuTimeMs(process),
            TimedOut = timedOut,
            MemoryExceeded = memoryExceeded
        };
    }

    private static async Task ReadLimitedAsync(
        System.IO.StreamReader reader, StringBuilder builder, long maxBytes, CancellationToken ct)
    {
        var buffer = new char[4096];
        long totalRead = 0;
        int read;

        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            var toAppend = (int)Math.Min(read, maxBytes - totalRead);
            if (toAppend > 0)
            {
                builder.Append(buffer, 0, toAppend);
                totalRead += toAppend;
            }

            if (totalRead >= maxBytes)
                break;
        }
    }

    private static string? FindCompiledDll(string physicalDir)
    {
        // OUT-OF-SCOPE: probes the .NET SDK install path (host-level, outside VFS mounts).
        // physicalDir is the resolved sandbox run directory passed from ExecuteAsync.
        var binDir = Path.Combine(physicalDir, "bin");
        if (!Directory.Exists(binDir)) return null;

        // Search for Sandbox.dll in bin directory tree
        return Directory.GetFiles(binDir, "Sandbox.dll", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the absolute path to the dotnet executable.
    /// Uses DOTNET_ROOT if set, otherwise falls back to well-known locations.
    /// Absolute paths prevent PATH-hijacking attacks (S4036).
    /// </summary>
    private static string GetDotnetExecutable()
    {
        // OUT-OF-SCOPE: probes the .NET SDK install path (host-level, outside VFS mounts).
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            var candidate = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var winPath = Path.Combine(programFiles, "dotnet", "dotnet.exe");
            if (File.Exists(winPath))
                return winPath;
        }
        else
        {
            foreach (var dir in new[] { "/usr/local/share/dotnet", "/usr/share/dotnet", "/usr/local/bin", "/usr/bin" })
            {
                var nixPath = Path.Combine(dir, "dotnet");
                if (File.Exists(nixPath))
                    return nixPath;
            }
        }

        // Fall back to bare name if absolute path cannot be resolved
        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    private static string GetMinimalPath()
    {
        var paths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            paths.Add(Path.Combine(programFiles, "dotnet"));
            paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
        }
        else
        {
            paths.Add("/usr/local/bin");
            paths.Add("/usr/bin");
            paths.Add("/bin");
            // Common dotnet install locations
            paths.Add("/usr/share/dotnet");
            paths.Add("/usr/local/share/dotnet");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            paths.Add(Path.Combine(home, ".dotnet"));
        }

        return string.Join(Path.PathSeparator, paths);
    }

    private static string? BuildErrorMessage(string error, bool timedOut, bool memoryExceeded)
    {
        var parts = new List<string>();

        if (timedOut)
            parts.Add("Execution timed out");
        if (memoryExceeded)
            parts.Add("Memory limit exceeded");
        if (!string.IsNullOrWhiteSpace(error))
            parts.Add(error.Trim());

        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort metric: any failure reading the process CPU time (e.g. the process has already exited) yields 0 rather than propagating.")]
    private static long GetCpuTimeMs(SysProcess process)
    {
        try
        {
            return (long)process.TotalProcessorTime.TotalMilliseconds;
        }
        catch
        {
            return 0;
        }
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
            LogCleanedUpSandboxTempDirectory(vRunDir);
        }
        catch (Exception ex)
        {
            LogFailedToCleanUpSandbox(ex, vRunDir);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Created sandbox run directory: {RunDir}")]
    private partial void LogCreatedSandboxTempDirectory(object RunDir);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Sandbox execution failed")]
    private partial void LogSandboxExecutionFailed(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Cleaned up sandbox run directory: {RunDir}")]
    private partial void LogCleanedUpSandboxTempDirectory(object RunDir);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to clean up sandbox run directory: {RunDir}")]
    private partial void LogFailedToCleanUpSandbox(Exception ex, object RunDir);

}

/// <summary>
/// Obsolete alias for <see cref="HostProcessRunner"/>. The previous name
/// "ProcessIsolationSandbox" wrongly suggested OS-level isolation, which this type does NOT provide.
/// Kept transitionally for source compatibility; use <see cref="HostProcessRunner"/> instead.
/// </summary>
[Obsolete("Renamed to HostProcessRunner: this type provides NO OS isolation (no namespaces/seccomp/chroot/network isolation). Use HostProcessRunner for trusted code, or DockerSandbox for untrusted/LLM code.")]
public sealed class ProcessIsolationSandbox : HostProcessRunner
{
    /// <summary>Initializes a new instance of the obsolete <see cref="ProcessIsolationSandbox"/> alias.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="options">Sandbox configuration options.</param>
    /// <param name="fs">The virtual file system service used to create and clean up run directories.</param>
    public ProcessIsolationSandbox(
        ILogger<HostProcessRunner> logger,
        IOptions<SandboxOptions> options,
        IFileSystemService fs)
        : base(logger, options, fs)
    {
    }
}
