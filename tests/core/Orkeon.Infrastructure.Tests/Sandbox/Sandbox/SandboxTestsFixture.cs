using Orkeon.Application.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Sandbox;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests;

#region RoslynCodeSecurityAnalyzer Fixture

public class RoslynCodeSecurityAnalyzerTestsFixture
{
    private readonly RoslynCodeSecurityAnalyzer _analyzer = new();
    private SecurityAnalysisOptions? _options;

    // --- Fluent configuration ---

    public RoslynCodeSecurityAnalyzerTestsFixture WithFileIOAllowed(bool allowed = true)
    {
        EnsureOptions().AllowFileIO = allowed;
        return this;
    }

    public RoslynCodeSecurityAnalyzerTestsFixture WithNetworkingAllowed(bool allowed = true)
    {
        EnsureOptions().AllowNetworking = allowed;
        return this;
    }

    public RoslynCodeSecurityAnalyzerTestsFixture WithReflectionAllowed(bool allowed = true)
    {
        EnsureOptions().AllowReflection = allowed;
        return this;
    }

    public RoslynCodeSecurityAnalyzerTestsFixture WithUnsafeCodeAllowed(bool allowed = true)
    {
        EnsureOptions().AllowUnsafeCode = allowed;
        return this;
    }

    public RoslynCodeSecurityAnalyzerTestsFixture WithAllowedNamespaces(params string[] namespaces)
    {
        var current = EnsureOptions();
        _options = new SecurityAnalysisOptions
        {
            AllowFileIO = current.AllowFileIO,
            AllowNetworking = current.AllowNetworking,
            AllowReflection = current.AllowReflection,
            AllowProcessExec = current.AllowProcessExec,
            AllowUnsafeCode = current.AllowUnsafeCode,
            AllowedNamespaces = [.. namespaces],
            BlockedTypes = current.BlockedTypes,
        };
        return this;
    }

    public RoslynCodeSecurityAnalyzerTestsFixture WithOptions(SecurityAnalysisOptions options)
    {
        _options = options;
        return this;
    }

    // --- Execution ---

    public CodeSecurityReport Analyze(string code)
        => _options != null ? _analyzer.Analyze(code, _options) : _analyzer.Analyze(code);

    // --- Inspection ---

    public RoslynCodeSecurityAnalyzer GetAnalyzer() => _analyzer;

    private SecurityAnalysisOptions EnsureOptions()
    {
        _options ??= new SecurityAnalysisOptions();
        return _options;
    }
}

#endregion

#region SecureCodeInterpreterTool Fixture

public sealed class SecureCodeInterpreterToolTestsFixture : IAsyncDisposable
{
    private readonly MockCodeSandbox _sandbox = new();
    private readonly MockCodeSecurityAnalyzer _analyzer = new();
    private SandboxOptions _options = new()
    {
        TimeoutSeconds = 30,
        MaxMemoryBytes = 256 * 1024 * 1024,
        MaxOutputBytes = 50_000
    };
    private SecureCodeInterpreterTool? _tool;

    public SecureCodeInterpreterToolTestsFixture()
    {
        // Default mock sandbox advertises full OS isolation so behavioral tests pass the
        // fail-closed isolation gate. Use WithSandboxIsolation(false) to test the gate.
        _sandbox.Capabilities = new SandboxCapabilities
        {
            SandboxType = "test",
            SupportsNetworkIsolation = true,
            SupportsFileSystemIsolation = true
        };
    }

    private SecureCodeInterpreterTool Tool => _tool ??= new SecureCodeInterpreterTool(
        _sandbox,
        _analyzer,
        Options.Create(_options),
        NullLogger<SecureCodeInterpreterTool>.Instance);

    // --- Fluent configuration ---

    public SecureCodeInterpreterToolTestsFixture WithSandboxIsolation(bool isolated)
    {
        _sandbox.Capabilities = _sandbox.Capabilities with
        {
            SupportsNetworkIsolation = isolated,
            SupportsFileSystemIsolation = isolated
        };
        return this;
    }

    public SecureCodeInterpreterToolTestsFixture WithAllowHostExecution(bool allow = true)
    {
        _options = new SandboxOptions
        {
            TimeoutSeconds = _options.TimeoutSeconds,
            MaxMemoryBytes = _options.MaxMemoryBytes,
            MaxOutputBytes = _options.MaxOutputBytes,
            AllowHostExecution = allow
        };
        return this;
    }

    public SecureCodeInterpreterToolTestsFixture WithAnalyzeResult(CodeSecurityReport report)
    {
        _analyzer.SetAnalyzeResult(report);
        return this;
    }

    public SecureCodeInterpreterToolTestsFixture WithAnalyzerAllowed()
    {
        _analyzer.SetAllowed();
        return this;
    }

    public SecureCodeInterpreterToolTestsFixture WithSandboxResult(SandboxExecutionResult result)
    {
        _sandbox.SetExecuteResult(result);
        return this;
    }

    // --- Execution ---

    public async Task<ToolCallResponse> CallAsync(ToolCallRequest request)
        => await Tool.CallAsync(request);

    public bool ValidateInput(string input) => Tool.ValidateInput(input);

    // --- Inspection ---

    public SecureCodeInterpreterTool GetTool() => Tool;
    public MockCodeSandbox GetSandbox() => _sandbox;
    public MockCodeSecurityAnalyzer GetAnalyzer() => _analyzer;

    public async ValueTask DisposeAsync()
    {
        await _sandbox.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region ProcessIsolationSandbox Fixture

public sealed class ProcessIsolationSandboxTestsFixture : IDisposable
{
    private SandboxOptions _options = new()
    {
        TimeoutSeconds = 60,
        MaxMemoryBytes = 256 * 1024 * 1024,
        MaxOutputBytes = 50_000
    };

    /// <summary>
    /// Physical temp dir backing the /sandbox VFS mount for this fixture.
    /// Created once per fixture instance, deleted on Dispose.
    /// </summary>
    private readonly string _physicalSandboxRoot;

    public ProcessIsolationSandboxTestsFixture()
    {
        _physicalSandboxRoot = Path.Combine(
            Path.GetTempPath(),
            $"orkeon-sandbox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_physicalSandboxRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_physicalSandboxRoot, recursive: true); }
        catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // --- Fluent configuration ---

    public ProcessIsolationSandboxTestsFixture WithTimeoutSeconds(int seconds)
    {
        _options.TimeoutSeconds = seconds;
        return this;
    }

    public ProcessIsolationSandboxTestsFixture WithMaxMemoryBytes(long bytes)
    {
        _options.MaxMemoryBytes = bytes;
        return this;
    }

    // --- Build / Execution ---

    /// <summary>
    /// Builds a <see cref="ProcessIsolationSandbox"/> wired to a real
    /// <see cref="DiskBackedFileSystemService"/> mounted at <c>/sandbox</c>.
    /// </summary>
    public HostProcessRunner Build()
    {
        // DiskBackedFileSystemService maps /sandbox → _physicalSandboxRoot
        IFileSystemService fs = new DiskBackedFileSystemService(_physicalSandboxRoot, "/sandbox");
        return new HostProcessRunner(
            NullLogger<HostProcessRunner>.Instance,
            Options.Create(_options),
            fs);
    }

    public async Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request)
        => await Build().ExecuteAsync(request);

    public async Task<bool> IsAvailableAsync()
        => await Build().IsAvailableAsync();
}

#endregion

#region DockerSandbox Fixture

public sealed class DockerSandboxTestsFixture : IDisposable
{
    /// <summary>
    /// Physical temp dir backing the /sandbox VFS mount for this fixture.
    /// Created once per fixture instance, deleted on Dispose.
    /// </summary>
    private readonly string _physicalSandboxRoot;

    public DockerSandboxTestsFixture()
    {
        _physicalSandboxRoot = Path.Combine(
            Path.GetTempPath(),
            $"orkeon-docker-sandbox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_physicalSandboxRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_physicalSandboxRoot, recursive: true); }
        catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // --- Build ---

    /// <summary>
    /// Builds a <see cref="DockerSandbox"/> wired to a real
    /// <see cref="DiskBackedFileSystemService"/> mounted at <c>/sandbox</c>.
    /// </summary>
    public DockerSandbox Build()
    {
        IFileSystemService fs = new DiskBackedFileSystemService(_physicalSandboxRoot, "/sandbox");
        return new DockerSandbox(
            NullLogger<DockerSandbox>.Instance,
            Options.Create(new SandboxOptions()),
            Options.Create(new DockerSandboxOptions()),
            fs);
    }
}

#endregion

#region LazyProbingCodeSandbox Fixture

/// <summary>
/// Fixture for <see cref="LazyProbingCodeSandbox"/> (R10.3 — ORG-012): wires the lazy
/// selector around two instrumented <see cref="MockCodeSandbox"/> doubles shaped like the
/// production pair (isolating "docker" primary, non-isolating "process" fallback).
/// </summary>
public sealed class LazyProbingCodeSandboxTestsFixture : IAsyncDisposable
{
    private readonly MockCodeSandbox _primary = new();
    private readonly MockCodeSandbox _fallback = new();
    private readonly SandboxOptions _options = new();
    private LazyProbingCodeSandbox? _wrapper;

    public LazyProbingCodeSandboxTestsFixture()
    {
        _primary.Capabilities = new SandboxCapabilities
        {
            SandboxType = "docker",
            SupportsMemoryLimits = true,
            SupportsCpuLimits = true,
            SupportsNetworkIsolation = true,
            SupportsFileSystemIsolation = true
        };
        _fallback.Capabilities = new SandboxCapabilities
        {
            SandboxType = "process",
            SupportsMemoryLimits = true,
            SupportsCpuLimits = false,
            SupportsNetworkIsolation = false,
            SupportsFileSystemIsolation = false
        };
    }

    // --- Fluent configuration ---

    public LazyProbingCodeSandboxTestsFixture WithPrimaryAvailable(bool available = true)
    {
        _primary.SetAvailable(available);
        return this;
    }

    public LazyProbingCodeSandboxTestsFixture WithAllowHostExecution(bool allow = true)
    {
        _options.AllowHostExecution = allow;
        return this;
    }

    public LazyProbingCodeSandboxTestsFixture WithFallbackIsolation(bool isolated)
    {
        _fallback.Capabilities = _fallback.Capabilities with
        {
            SupportsNetworkIsolation = isolated,
            SupportsFileSystemIsolation = isolated
        };
        return this;
    }

    public LazyProbingCodeSandboxTestsFixture WithPrimaryOutput(string output)
    {
        _primary.SetExecuteSuccess(output);
        return this;
    }

    public LazyProbingCodeSandboxTestsFixture WithFallbackOutput(string output)
    {
        _fallback.SetExecuteSuccess(output);
        return this;
    }

    // --- Build / Execution ---

    /// <summary>
    /// Builds the wrapper exactly like the DI factory does (pure synchronous wiring).
    /// </summary>
    public LazyProbingCodeSandbox GetWrapper() => _wrapper ??= new LazyProbingCodeSandbox(
        _primary,
        _fallback,
        Options.Create(_options),
        NullLogger<LazyProbingCodeSandbox>.Instance);

    public async Task<SandboxExecutionResult> ExecuteAsync(CancellationToken ct)
        => await GetWrapper().ExecuteAsync(
            new SandboxExecutionRequest { Code = "Console.WriteLine(1);" }, ct);

    // --- Inspection ---

    public MockCodeSandbox GetPrimary() => _primary;
    public MockCodeSandbox GetFallback() => _fallback;

    public async ValueTask DisposeAsync()
    {
        if (_wrapper != null)
            await _wrapper.DisposeAsync();
        await _primary.DisposeAsync();
        await _fallback.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region SandboxOptions Fixture

public class SandboxOptionsTestsFixture
{
    // --- Build ---

    public static SandboxOptions CreateSandboxOptions() => new();
    public static DockerSandboxOptions CreateDockerSandboxOptions() => new();
}

#endregion

#region SandboxDependencyInjection Fixture

public sealed class SandboxDependencyInjectionTestsFixture : IDisposable
{
    private readonly string _physicalSandboxRoot;

    public SandboxDependencyInjectionTestsFixture()
    {
        _physicalSandboxRoot = Path.Combine(
            Path.GetTempPath(),
            $"orkeon-di-sandbox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_physicalSandboxRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_physicalSandboxRoot, recursive: true); }
        catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // --- Build ---

    public ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Register a DiskBackedFileSystemService so IFileSystemService is available
        // for ProcessIsolationSandbox (and DockerSandbox if needed).
        IFileSystemService fs = new DiskBackedFileSystemService(_physicalSandboxRoot, "/sandbox");
        services.AddSingleton(fs);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddOrkeonCodeSandbox();

        return services.BuildServiceProvider();
    }
}

#endregion

#region SecurityAnalysisOptions Fixture

public class SecurityAnalysisOptionsTestsFixture
{
    // --- Build ---

    public static SecurityAnalysisOptions CreateOptions() => new();
}

#endregion

#region SandboxPermissions Fixture

public class SandboxPermissionsTestsFixture
{
    // --- Build ---

    public static SandboxPermissions CreatePermissions() => new();
}

#endregion

#region SandboxExecutionRequest Fixture

public class SandboxExecutionRequestTestsFixture
{
    // --- Build ---

    public static SandboxExecutionRequest CreateRequest() => new();
}

#endregion
