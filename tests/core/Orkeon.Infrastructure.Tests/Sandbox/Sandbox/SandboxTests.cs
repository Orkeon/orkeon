using Orkeon.Application.Interfaces;
using Orkeon.Domain.Tools.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Sandbox;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Infrastructure.Tests.Sandbox;

#region RoslynCodeSecurityAnalyzer Tests

public class RoslynCodeSecurityAnalyzerTests
{
    private readonly RoslynCodeSecurityAnalyzerTestsFixture _fixture = new();

    [Fact]
    public void ShouldReturnIsAllowed_WhenCodeIsSafe()
    {
        var report = _fixture.Analyze("var x = 42;\nvar y = x * 2;\nConsole.WriteLine(y);");

        Assert.True(report.IsAllowed);
        Assert.Equal(SecurityRiskLevel.None, report.OverallRisk);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void ShouldReturnIsAllowed_WhenCodeUsesLinq()
    {
        var report = _fixture.Analyze("""
            using System.Linq;
            var numbers = new List<int> { 1, 2, 3, 4, 5 };
            var sum = numbers.Where(n => n > 2).Sum();
            Console.WriteLine(sum);
            """);

        Assert.True(report.IsAllowed);
    }

    [Fact]
    public void ShouldReturnCritical_WhenCodeUsesProcessStart()
    {
        var report = _fixture.Analyze("""
            using System.Diagnostics;
            Process.Start("cmd.exe", "/c dir");
            """);

        Assert.False(report.IsAllowed);
        Assert.Equal(SecurityRiskLevel.Critical, report.OverallRisk);
        Assert.Contains(report.Violations, v => v.Rule == "ProcessExecution" || v.Rule == "BlockedNamespace");
    }

    [Fact]
    public void ShouldReturnCritical_WhenCodeCreatesProcessStartInfo()
    {
        var report = _fixture.Analyze("var psi = new ProcessStartInfo(\"bash\", \"-c whoami\");");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "ProcessExecution" || v.Rule == "BlockedType");
    }

    [Fact]
    public void ShouldReturnHigh_WhenCodeUsesAssemblyLoad()
    {
        var report = _fixture.Analyze("Assembly.Load(\"SomeDangerous.Assembly\");");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "Reflection");
        Assert.True((int)report.OverallRisk >= (int)SecurityRiskLevel.High);
    }

    [Fact]
    public void ShouldReturnHigh_WhenFileIODisabledAndFileReadUsed()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithFileIOAllowed(false)
            .Analyze("var content = File.ReadAllText(\"/etc/passwd\");");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "FileIO");
    }

    [Fact]
    public void ShouldNotBlock_WhenFileIOEnabledAndFileReadUsed()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithFileIOAllowed(true)
            .Analyze("var content = File.ReadAllText(\"data.txt\");");

        Assert.DoesNotContain(report.Violations, v => v.Rule == "FileIO");
    }

    [Fact]
    public void ShouldReturnHigh_WhenNetworkDisabledAndHttpClientUsed()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithNetworkingAllowed(false)
            .Analyze("""
                var client = new HttpClient();
                var result = await client.GetStringAsync("http://example.com");
                """);

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "Networking");
    }

    [Fact]
    public void ShouldNotBlock_WhenNetworkEnabledAndHttpClientCreated()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithNetworkingAllowed(true)
            .Analyze("var client = new HttpClient();");

        Assert.DoesNotContain(report.Violations, v => v.Rule == "Networking");
    }

    [Fact]
    public void ShouldReturnCritical_WhenCodeContainsUnsafeBlock()
    {
        var report = _fixture.Analyze("unsafe\n{\n    int x = 42;\n    int* p = &x;\n}");

        Assert.False(report.IsAllowed);
        Assert.Equal(SecurityRiskLevel.Critical, report.OverallRisk);
        Assert.Contains(report.Violations, v => v.Rule == "UnsafeCode");
    }

    [Fact]
    public void ShouldReturnAllowed_WhenUnsafeBlockAllowed()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithUnsafeCodeAllowed(true)
            .Analyze("unsafe\n{\n    int x = 42;\n    int* p = &x;\n}");

        Assert.DoesNotContain(report.Violations, v => v.Rule == "UnsafeCode");
    }

    [Fact]
    public void ShouldReturnViolation_WhenBlockedNamespaceUsed()
    {
        var report = _fixture.Analyze("""
            using Microsoft.Win32;
            var key = Registry.GetValue("HKEY_LOCAL_MACHINE", "test", null);
            """);

        Assert.Contains(report.Violations, v => v.Rule == "BlockedNamespace");
    }

    [Fact]
    public void ShouldDetectReflection_WhenTypeOfGetMethodUsed()
    {
        var report = _fixture.Analyze("""
            var method = typeof(Console).GetMethod("WriteLine");
            method.Invoke(null, new object[] { "hello" });
            """);

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "Reflection");
    }

    [Fact]
    public void ShouldNotBlock_WhenReflectionIsAllowed()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithReflectionAllowed(true)
            .Analyze("var method = typeof(Console).GetMethod(\"WriteLine\");");

        Assert.DoesNotContain(report.Violations, v => v.Rule == "Reflection");
    }

    [Fact]
    public void ShouldReturnHigh_WhenActivatorCreateInstanceUsed()
    {
        var report = _fixture.Analyze("var obj = Activator.CreateInstance(typeof(object));");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "Reflection");
    }

    [Fact]
    public void ShouldReturnHigh_WhenNetworkDisabledAndSocketCreated()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithNetworkingAllowed(false)
            .Analyze("var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "Networking" || v.Rule == "BlockedType");
    }

    [Fact]
    public void ShouldReturnHigh_WhenFileIODisabledAndStreamReaderCreated()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithFileIOAllowed(false)
            .Analyze("var reader = new StreamReader(\"/etc/passwd\");");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "FileIO");
    }

    [Fact]
    public void ShouldReportCorrectLineNumber_WhenViolationDetected()
    {
        var report = _fixture.Analyze("var x = 1;\nvar y = 2;\nProcess.Start(\"cmd\");");

        var processViolation = report.Violations.FirstOrDefault(v => v.Rule == "ProcessExecution");
        Assert.NotNull(processViolation);
        Assert.Equal(3, processViolation!.LineNumber);
    }

    [Fact]
    public void ShouldIncludeCodeSnippet_WhenViolationDetected()
    {
        var report = _fixture.Analyze("Process.Start(\"cmd\");");

        Assert.False(string.IsNullOrEmpty(report.Violations[0].CodeSnippet));
    }

    [Fact]
    public void ShouldSetOverallRiskToMax_WhenMultipleViolationsDetected()
    {
        var report = _fixture.Analyze("using System.IO;\nFile.ReadAllText(\"test.txt\");\nProcess.Start(\"cmd\");");

        Assert.Equal(SecurityRiskLevel.Critical, report.OverallRisk);
    }

    [Fact]
    public void ShouldReturnIsAllowed_WhenCodeIsEmpty()
    {
        var report = _fixture.Analyze("");

        Assert.True(report.IsAllowed);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void ShouldNotBlock_WhenNamespacesAreAllowed()
    {
        var report = _fixture.Analyze("""
            using System.Collections.Generic;
            using System.Linq;
            using System.Text;
            using System.Text.Json;
            var list = new List<int> { 1, 2, 3 };
            """);

        Assert.True(report.IsAllowed);
        Assert.DoesNotContain(report.Violations, v => v.Rule == "BlockedNamespace");
    }

    [Fact]
    public void ShouldNotBlock_WhenCustomAllowedNamespacesConfigured()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithFileIOAllowed(true)
            .WithAllowedNamespaces("System", "System.Collections.Generic", "System.Linq", "System.Text", "System.IO")
            .Analyze("using System.IO;\nvar data = File.ReadAllText(\"test.txt\");");

        Assert.DoesNotContain(report.Violations, v => v.Rule == "BlockedNamespace");
        Assert.DoesNotContain(report.Violations, v => v.Rule == "FileIO");
    }

    [Fact]
    public void ShouldBlock_WhenFileIODisabledAndDirectoryGetFilesUsed()
    {
        var report = new RoslynCodeSecurityAnalyzerTestsFixture()
            .WithFileIOAllowed(false)
            .Analyze("var files = Directory.GetFiles(\"/tmp\");");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "FileIO");
    }

    // --- R2.2 hardening: indirect reflection / dynamic / P-Invoke evasion vectors ---
    // These vectors evaded the syntactic blocklist on b5179b3c (classified below High → allowed).

    [Fact]
    public void ShouldClassifyHigh_WhenResolvingProcessViaAssemblyGetType()
    {
        var report = _fixture.Analyze(
            "var t = typeof(int).Assembly.GetType(\"System.Diagnostics.Process\");");

        Assert.False(report.IsAllowed);
        Assert.True((int)report.OverallRisk >= (int)SecurityRiskLevel.High);
        Assert.Contains(report.Violations, v => v.Rule == "Reflection");
    }

    [Fact]
    public void ShouldBlock_WhenUsingDynamicDispatch()
    {
        var report = _fixture.Analyze("""
            dynamic d = GetSomething();
            d.Invoke();
            """);

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "Reflection");
    }

    [Fact]
    public void ShouldBlock_WhenUsingExpressionCompile()
    {
        var report = _fixture.Analyze("var fn = expr.Compile();");

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "Reflection");
    }

    [Fact]
    public void ShouldClassifyCritical_WhenDeclaringDllImport()
    {
        var report = _fixture.Analyze("""
            using System.Runtime.InteropServices;
            class Native { [DllImport("libc")] static extern int system(string cmd); }
            """);

        Assert.False(report.IsAllowed);
        Assert.Contains(report.Violations, v => v.Rule == "PInvoke");
    }
}

#endregion

#region SecureCodeInterpreterTool Tests

public sealed class SecureCodeInterpreterToolTests : IAsyncDisposable
{
    private readonly SecureCodeInterpreterToolTestsFixture _fixture = new();

    [Fact]
    public void ShouldReturnCodeInterpreter_WhenAccessingName()
    {
        Assert.Equal("code_interpreter", _fixture.GetTool().Name);
    }

    [Fact]
    public void ShouldHaveRequiredCodeParameter_WhenAccessingSchema()
    {
        var tool = _fixture.GetTool();
        Assert.True(tool.Schema.Parameters.ContainsKey("code"));
        Assert.True(tool.Schema.Parameters["code"].Required);
    }

    [Fact]
    public async Task ShouldReturnSuccess_WhenCodeIsSafe()
    {
        var response = await _fixture
            .WithAnalyzeResult(new CodeSecurityReport
            {
                IsAllowed = true,
                Violations = Array.Empty<SecurityViolation>(),
                OverallRisk = SecurityRiskLevel.None
            })
            .WithSandboxResult(new SandboxExecutionResult
            {
                Success = true,
                Output = "42\n",
                ExitCode = 0,
                Duration = TimeSpan.FromMilliseconds(500)
            })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "Console.WriteLine(42);"
            }));

        Assert.True(response.Success);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task ShouldBlockExecution_WhenCodeIsDangerous()
    {
        var response = await _fixture
            .WithAnalyzeResult(new CodeSecurityReport
            {
                IsAllowed = false,
                Violations =
                [
                    new SecurityViolation
                    {
                        Rule = "ProcessExecution",
                        Description = "Process execution is not allowed",
                        Risk = SecurityRiskLevel.Critical, LineNumber = 1
                    }
                ],
                OverallRisk = SecurityRiskLevel.Critical
            })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "Process.Start(\"cmd\");"
            }));

        Assert.False(response.Success);
        Assert.Contains("security analysis", response.Error);
        Assert.NotNull(response.Metadata);
        Assert.True(response.Metadata.ContainsKey("security_blocked"));
        Assert.Equal(0, _fixture.GetSandbox().ExecuteCallCount);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCodeParameterMissing()
    {
        var response = await _fixture.CallAsync(
            new ToolCallRequest("code_interpreter", []));

        Assert.False(response.Success);
        Assert.Contains("code", response.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCodeIsEmpty()
    {
        var response = await _fixture.CallAsync(
            new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "   "
            }));

        Assert.False(response.Success);
        Assert.Contains("empty", response.Error);
    }

    [Fact]
    public async Task ShouldReturnTimeoutError_WhenExecutionTimesOut()
    {
        var response = await _fixture
            .WithAnalyzerAllowed()
            .WithSandboxResult(new SandboxExecutionResult
            {
                Success = false,
                TimedOut = true,
                ExitCode = -1,
                Duration = TimeoutQuick
            })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "while(true) {}"
            }));

        Assert.False(response.Success);
        Assert.Contains("timed out", response.Error);
        Assert.NotNull(response.Metadata);
        Assert.True(response.Metadata.ContainsKey("timed_out"));
    }

    [Fact]
    public async Task ShouldReturnMemoryError_WhenMemoryLimitExceeded()
    {
        var response = await _fixture
            .WithAnalyzerAllowed()
            .WithSandboxResult(new SandboxExecutionResult
            {
                Success = false,
                MemoryExceeded = true,
                MemoryUsedBytes = 512 * 1024 * 1024,
                ExitCode = -1,
                Duration = TimeSpan.FromSeconds(5)
            })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "var data = new byte[1_000_000_000];"
            }));

        Assert.False(response.Success);
        Assert.Contains("memory limit", response.Error);
        Assert.NotNull(response.Metadata);
        Assert.True(response.Metadata.ContainsKey("memory_exceeded"));
    }

    [Fact]
    public async Task ShouldReturnError_WhenCompilationFails()
    {
        var response = await _fixture
            .WithAnalyzerAllowed()
            .WithSandboxResult(new SandboxExecutionResult
            {
                Success = false,
                Error = "Compilation failed: CS1002 ; expected",
                ExitCode = 1,
                Duration = TimeSpan.FromSeconds(2)
            })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "this is not valid C#!!!"
            }));

        Assert.False(response.Success);
        Assert.Contains("Compilation failed", response.Error);
    }

    [Fact]
    public async Task ShouldPassCustomTimeout_WhenTimeoutParameterProvided()
    {
        await _fixture
            .WithAnalyzerAllowed()
            .WithSandboxResult(new SandboxExecutionResult
            {
                Success = true,
                Output = "1\n",
                ExitCode = 0,
                Duration = TimeSpan.FromSeconds(1)
            })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "Console.WriteLine(1);",
                ["timeout_seconds"] = 10
            }));

        Assert.Equal(1, _fixture.GetSandbox().ExecuteCallCount);
        Assert.Equal(TimeSpan.FromSeconds(10), _fixture.GetSandbox().LastExecuteRequest!.Timeout);
    }

    // --- R2.2 fail-closed isolation gate ---

    [Fact]
    public async Task ShouldRefuseExecution_WhenSandboxNotIsolatedAndNoHostOptIn()
    {
        var response = await _fixture
            .WithSandboxIsolation(false)
            .WithAnalyzerAllowed()
            .WithSandboxResult(new SandboxExecutionResult { Success = true, Output = "x" })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "Console.WriteLine(1);"
            }));

        Assert.False(response.Success);
        Assert.Contains("no OS-isolating sandbox", response.Error);
        Assert.Equal(0, _fixture.GetSandbox().ExecuteCallCount);
        Assert.NotNull(response.Metadata);
        Assert.True(response.Metadata!.ContainsKey("isolation_unavailable"));
    }

    [Fact]
    public async Task ShouldExecute_WhenSandboxNotIsolatedButHostExecutionOptedIn()
    {
        var response = await _fixture
            .WithSandboxIsolation(false)
            .WithAllowHostExecution()
            .WithAnalyzerAllowed()
            .WithSandboxResult(new SandboxExecutionResult { Success = true, Output = "ok\n", ExitCode = 0 })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "Console.WriteLine(\"ok\");"
            }));

        Assert.True(response.Success);
        Assert.Equal(1, _fixture.GetSandbox().ExecuteCallCount);
    }

    [Fact]
    public async Task ShouldExecute_WhenSandboxIsIsolated()
    {
        var response = await _fixture
            .WithSandboxIsolation(true)
            .WithAnalyzerAllowed()
            .WithSandboxResult(new SandboxExecutionResult { Success = true, Output = "ok\n", ExitCode = 0 })
            .CallAsync(new ToolCallRequest("code_interpreter", new Dictionary<string, object?>
            {
                ["code"] = "Console.WriteLine(\"ok\");"
            }));

        Assert.True(response.Success);
        Assert.Equal(1, _fixture.GetSandbox().ExecuteCallCount);
    }

    [Fact]
    public void ShouldReturnTrue_WhenInputIsValidJson()
    {
        Assert.True(_fixture.ValidateInput("""{"code": "Console.WriteLine(42);"}"""));
    }

    [Fact]
    public void ShouldReturnFalse_WhenInputIsEmpty()
    {
        Assert.False(_fixture.ValidateInput(""));
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region ProcessIsolationSandbox Tests

public sealed class ProcessIsolationSandboxTests : IDisposable
{
    private readonly ProcessIsolationSandboxTestsFixture _fixture = new();

    [Fact]
    public void ShouldReportCorrectCapabilities_WhenAccessingProcessSandbox()
    {
        var sandbox = _fixture.Build();
        var caps = sandbox.Capabilities;

        Assert.Equal("process", caps.SandboxType);
        Assert.True(caps.SupportsMemoryLimits);
        Assert.False(caps.SupportsCpuLimits);
        Assert.False(caps.SupportsNetworkIsolation);
        Assert.False(caps.SupportsFileSystemIsolation);
    }

    [Fact]
    public void ShouldReportProcessType_WhenAccessingCapabilities()
    {
        var sandbox = _fixture.Build();
        Assert.Equal("process", sandbox.Capabilities.SandboxType);
        Assert.True(sandbox.Capabilities.SupportsMemoryLimits);
        Assert.False(sandbox.Capabilities.SupportsCpuLimits);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenDotnetIsAvailable()
    {
        Assert.True(await _fixture.IsAvailableAsync());
    }

    [Fact]
    public async Task ShouldProduceOutput_WhenExecutingSimpleCode()
    {
        var result = await _fixture.ExecuteAsync(new SandboxExecutionRequest
        {
            Code = "Console.WriteLine(\"Hello Sandbox\");",
            Timeout = TimeSpan.FromSeconds(60)
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains("Hello Sandbox", result.Output);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ShouldReportError_WhenCodeHasCompilationError()
    {
        var result = await _fixture.ExecuteAsync(new SandboxExecutionRequest
        {
            Code = "this is not valid C# code @@@ !!!",
            Timeout = TimeSpan.FromSeconds(60)
        });

        Assert.False(result.Success);
        Assert.Contains("Compilation failed", result.Error);
    }

    [Fact]
    public async Task ShouldReportNonZeroExit_WhenRuntimeExceptionThrown()
    {
        var result = await _fixture.ExecuteAsync(new SandboxExecutionRequest
        {
            Code = "throw new InvalidOperationException(\"boom\");",
            Timeout = TimeSpan.FromSeconds(60)
        });

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task ShouldProduceCorrectOutput_WhenExecutingMathComputation()
    {
        var result = await _fixture.ExecuteAsync(new SandboxExecutionRequest
        {
            Code = "var result = Enumerable.Range(1, 10).Sum();\nConsole.WriteLine(result);",
            Timeout = TimeSpan.FromSeconds(60)
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains("55", result.Output);
    }

    [Fact]
    public async Task ShouldCompleteSuccessfully_WhenProcessSandboxDisposed()
    {
        var sandbox = _fixture.Build();
        var exception = await Record.ExceptionAsync(async () => await sandbox.DisposeAsync());
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region DockerSandbox Tests

public sealed class DockerSandboxTests : IDisposable
{
    private readonly DockerSandboxTestsFixture _fixture = new();

    [Fact]
    public void ShouldReportDockerType_WhenAccessingDockerCapabilities()
    {
        var sandbox = _fixture.Build();
        Assert.Equal("docker", sandbox.Capabilities.SandboxType);
        Assert.True(sandbox.Capabilities.SupportsMemoryLimits);
        Assert.True(sandbox.Capabilities.SupportsCpuLimits);
        Assert.True(sandbox.Capabilities.SupportsNetworkIsolation);
        Assert.True(sandbox.Capabilities.SupportsFileSystemIsolation);
    }

    [Fact]
    public async Task ShouldCompleteSuccessfully_WhenDockerSandboxDisposed()
    {
        var sandbox = _fixture.Build();
        var exception = await Record.ExceptionAsync(async () => await sandbox.DisposeAsync());
        Assert.Null(exception);
    }

    // --- R10.3 (ORG-012): availability probe cancellation/timeout behavior ---

    [Fact]
    public async Task ShouldReturnFalseWithoutThrowing_WhenAvailabilityProbeAlreadyCancelled()
    {
        // On cancellation/timeout the probe must stay fail-closed (report "unavailable",
        // leak no exception) and kill the probe child process — WaitForExitAsync only
        // stops waiting, it never stops the process. The kill itself is not hermetically
        // observable without a real hanging `docker version`; this test exercises the
        // cancellation path deterministically whether or not Docker is installed
        // (missing binary → start failure swallowed; present binary → cancelled wait → kill).
        await using var sandbox = _fixture.Build();
        var alreadyCancelled = new CancellationToken(canceled: true);

        bool? available = null;
        var exception = await Record.ExceptionAsync(
            async () => available = await sandbox.IsAvailableAsync(alreadyCancelled));

        Assert.Null(exception);
        Assert.False(available);
    }

    [Fact]
    public async Task ShouldNotThrow_WhenProbingAvailability()
    {
        // Environment-independent fail-closed contract: with Docker installed the probe
        // exits normally; without it every failure is swallowed and reported as false.
        await using var sandbox = _fixture.Build();

        var exception = await Record.ExceptionAsync(
            async () => await sandbox.IsAvailableAsync(TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region LazyProbingCodeSandbox Tests (R10.3 — ORG-012)

public sealed class LazyProbingCodeSandboxTests : IAsyncDisposable
{
    private readonly LazyProbingCodeSandboxTestsFixture _fixture = new();

    // --- Probe laziness + memoization (the probe moved out of the DI factory) ---

    [Fact]
    public void ShouldNotProbeAvailability_WhenConstructed()
    {
        // Construction is exactly what the DI factory now does at ICodeSandbox
        // resolution. The old factory probed Docker synchronously at this point
        // (sync-over-async under the container's singleton-resolution lock); the
        // lazy selector must not touch either sandbox.
        _ = _fixture.GetWrapper();

        Assert.Equal(0, _fixture.GetPrimary().IsAvailableCallCount);
        Assert.Equal(0, _fixture.GetFallback().IsAvailableCallCount);
    }

    [Fact]
    public async Task ShouldProbeExactlyOnce_WhenFirstExecuteRuns()
    {
        await _fixture.WithPrimaryAvailable().ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _fixture.GetPrimary().IsAvailableCallCount);
    }

    [Fact]
    public async Task ShouldNotProbeAgain_WhenSecondExecuteRuns()
    {
        _fixture.WithPrimaryAvailable();

        await _fixture.ExecuteAsync(TestContext.Current.CancellationToken);
        await _fixture.ExecuteAsync(TestContext.Current.CancellationToken);

        // Memoized: still a single probe, both executions routed to the primary.
        Assert.Equal(1, _fixture.GetPrimary().IsAvailableCallCount);
        Assert.Equal(2, _fixture.GetPrimary().ExecuteCallCount);
    }

    [Fact]
    public async Task ShouldProbeOnlyOnce_WhenExecutionsRaceConcurrently()
    {
        // ORG-012 symptom was concurrent resolutions stacking up behind one probe;
        // the lazy selector must collapse concurrent first calls onto a single probe.
        await using var primary = new GatedCodeSandbox();
        await using var fallback = new MockCodeSandbox();
        await using var wrapper = new LazyProbingCodeSandbox(
            primary, fallback,
            Options.Create(new SandboxOptions()),
            NullLogger<LazyProbingCodeSandbox>.Instance);
        var request = new SandboxExecutionRequest { Code = "Console.WriteLine(1);" };

        var executions = Enumerable.Range(0, 8)
            .Select(_ => wrapper.ExecuteAsync(request, TestContext.Current.CancellationToken))
            .ToArray();
        primary.ReleaseProbe(available: true);
        await Task.WhenAll(executions);

        Assert.Equal(1, primary.IsAvailableCallCount);
        Assert.Equal(8, primary.ExecuteCallCount);
        Assert.Equal(0, fallback.ExecuteCallCount);
    }

    // --- Routing + R2.2 fail-closed gate (behavior unchanged, just relocated) ---

    [Fact]
    public async Task ShouldDelegateToPrimary_WhenPrimaryAvailable()
    {
        var result = await _fixture
            .WithPrimaryAvailable()
            .WithPrimaryOutput("from-primary")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("from-primary", result.Output);
        Assert.Equal(1, _fixture.GetPrimary().ExecuteCallCount);
        Assert.Equal(0, _fixture.GetFallback().ExecuteCallCount);
    }

    [Fact]
    public async Task ShouldRefuseExecution_WhenPrimaryUnavailableWithoutHostOptIn()
    {
        var result = await _fixture
            .WithPrimaryAvailable(false)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("no OS-isolating sandbox", result.Error);
        Assert.Equal(0, _fixture.GetPrimary().ExecuteCallCount);
        Assert.Equal(0, _fixture.GetFallback().ExecuteCallCount);
    }

    [Fact]
    public async Task ShouldUseExactToolRefusalMessage_WhenRefusingExecution()
    {
        // Pin the wrapper's refusal to the very message SecureCodeInterpreterTool
        // emits at its own gate (shared via SandboxIsolationGate).
        var result = await _fixture
            .WithPrimaryAvailable(false)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SandboxIsolationGate.BuildRefusalMessage("process"), result.Error);
    }

    [Fact]
    public async Task ShouldExecuteOnFallback_WhenPrimaryUnavailableAndHostOptedIn()
    {
        var result = await _fixture
            .WithPrimaryAvailable(false)
            .WithAllowHostExecution()
            .WithFallbackOutput("from-fallback")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("from-fallback", result.Output);
        Assert.Equal(0, _fixture.GetPrimary().ExecuteCallCount);
        Assert.Equal(1, _fixture.GetFallback().ExecuteCallCount);
    }

    [Fact]
    public async Task ShouldExecuteOnFallback_WhenFallbackIsOsIsolated()
    {
        // The gate only blocks NON-isolating fallbacks; an isolating one needs no opt-in.
        var result = await _fixture
            .WithPrimaryAvailable(false)
            .WithFallbackIsolation(true)
            .WithFallbackOutput("isolated-fallback")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, _fixture.GetFallback().ExecuteCallCount);
    }

    // --- Capabilities surface ---

    [Fact]
    public void ShouldReportPrimaryCapabilities_BeforeProbeRuns()
    {
        // Optimistic before the probe: safe because the fail-closed gate is enforced
        // inside ExecuteAsync itself. Reading Capabilities must not trigger the probe.
        var capabilities = _fixture.GetWrapper().Capabilities;

        Assert.Equal("docker", capabilities.SandboxType);
        Assert.Equal(0, _fixture.GetPrimary().IsAvailableCallCount);
    }

    [Fact]
    public async Task ShouldReportFallbackCapabilities_WhenProbeFoundPrimaryUnavailable()
    {
        _fixture.WithPrimaryAvailable(false);
        await _fixture.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal("process", _fixture.GetWrapper().Capabilities.SandboxType);
    }

    // --- IsAvailableAsync surface ---

    [Fact]
    public async Task ShouldReportAvailableWithSingleProbe_WhenIsAvailableCalledTwice()
    {
        var wrapper = _fixture.WithPrimaryAvailable().GetWrapper();

        Assert.True(await wrapper.IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.True(await wrapper.IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, _fixture.GetPrimary().IsAvailableCallCount);
        Assert.Equal(0, _fixture.GetFallback().IsAvailableCallCount);
    }

    [Fact]
    public async Task ShouldConsultFallback_WhenPrimaryUnavailableOnIsAvailable()
    {
        var wrapper = _fixture.WithPrimaryAvailable(false).GetWrapper();

        Assert.True(await wrapper.IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, _fixture.GetFallback().IsAvailableCallCount);
    }

    // --- Cancellation + ownership ---

    [Fact]
    public async Task ShouldReturnCancelledResult_WhenTokenCancelledWhileProbePending()
    {
        // A cancelled caller stops waiting on the memoized probe without poisoning it
        // and gets the same cancellation result shape as the inner sandboxes.
        await using var primary = new GatedCodeSandbox(); // probe never released during the call
        await using var fallback = new MockCodeSandbox();
        await using var wrapper = new LazyProbingCodeSandbox(
            primary, fallback,
            Options.Create(new SandboxOptions()),
            NullLogger<LazyProbingCodeSandbox>.Instance);
        var alreadyCancelled = new CancellationToken(canceled: true);

        var result = await wrapper.ExecuteAsync(
            new SandboxExecutionRequest { Code = "Console.WriteLine(1);" }, alreadyCancelled);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Equal("Execution was cancelled", result.Error);
        Assert.Equal(0, primary.ExecuteCallCount);
        Assert.Equal(0, fallback.ExecuteCallCount);
    }

    [Fact]
    public async Task ShouldNotDisposeInnerSandboxes_WhenWrapperDisposed()
    {
        // The concrete sandboxes are container-owned singletons; the DI container
        // disposes them itself, so the selector must not double-dispose them.
        await _fixture.GetWrapper().DisposeAsync();

        Assert.Equal(0, _fixture.GetPrimary().DisposeCallCount);
        Assert.Equal(0, _fixture.GetFallback().DisposeCallCount);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region SandboxOptions Tests

public class SandboxOptionsTests
{
    private readonly SandboxOptionsTestsFixture _fixture = new();

    [Fact]
    public void ShouldHaveCorrectDefaults_WhenSandboxOptionsCreated()
    {
        var options = SandboxOptionsTestsFixture.CreateSandboxOptions();

        Assert.Equal("process", options.PreferredSandbox);
        Assert.Equal(30, options.TimeoutSeconds);
        Assert.Equal(256 * 1024 * 1024, options.MaxMemoryBytes);
        Assert.Equal(50_000, options.MaxOutputBytes);
        Assert.True(options.RequireHumanApproval);
    }

    [Fact]
    public void ShouldHaveCorrectDefaults_WhenDockerSandboxOptionsCreated()
    {
        var options = SandboxOptionsTestsFixture.CreateDockerSandboxOptions();

        Assert.Contains("dotnet/sdk", options.ImageName);
        Assert.False(options.PullImageOnStartup);
    }
}

#endregion

#region DI Registration Tests

public sealed class SandboxDependencyInjectionTests : IDisposable
{
    private readonly SandboxDependencyInjectionTestsFixture _fixture = new();

    [Fact]
    public void ShouldRegisterAllServices_WhenAddOrkeonCodeSandboxCalled()
    {
        var provider = _fixture.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ICodeSecurityAnalyzer>());
        Assert.IsType<RoslynCodeSecurityAnalyzer>(provider.GetService<ICodeSecurityAnalyzer>());
        var sandbox = provider.GetService<ICodeSandbox>();
        Assert.NotNull(sandbox);
        // R10.3 (ORG-012): ICodeSandbox resolves to the lazy selector — the Docker
        // availability probe no longer runs at resolution time. The R2.2 fail-closed
        // routing (prefer the OS-isolating DockerSandbox; refuse the non-isolating
        // HostProcessRunner unless AllowHostExecution is opted in) is unchanged in
        // substance and asserted behaviorally in LazyProbingCodeSandboxTests.
        Assert.IsType<LazyProbingCodeSandbox>(sandbox);
        Assert.NotNull(provider.GetService<SecureCodeInterpreterTool>());
    }

    [Fact]
    public void ShouldResolveSecureCodeInterpreterTool_WhenAddOrkeonCodeSandboxCalled()
    {
        var provider = _fixture.BuildServiceProvider();
        var tool = provider.GetRequiredService<SecureCodeInterpreterTool>();

        Assert.Equal("code_interpreter", tool.Name);
    }

    [Fact]
    public void ShouldKeepFactoryPureWiring_WhenResolvingICodeSandbox()
    {
        // Anti-regression for ORG-012: the old factory blocked up to 10 s inside the
        // container's singleton-resolution lock waiting for a `docker version` child
        // process. The factory now only constructs the lazy selector: no probe runs
        // here (LazyProbingCodeSandboxTests proves construction never probes), and
        // DiRegistrationSyncOverAsyncPolicyTests forbids reintroducing the blocking call.
        var provider = _fixture.BuildServiceProvider();

        var first = provider.GetRequiredService<ICodeSandbox>();
        var second = provider.GetRequiredService<ICodeSandbox>();

        var wrapper = Assert.IsType<LazyProbingCodeSandbox>(first);
        // Singleton: the memoized probe outcome stays cached per container, as before.
        Assert.Same(first, second);
        // Pre-probe capabilities are the (optimistic) primary's — reading them must not probe.
        Assert.Equal("docker", wrapper.Capabilities.SandboxType);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region SecurityAnalysisOptions Tests

public class SecurityAnalysisOptionsTests
{
    private readonly SecurityAnalysisOptionsTestsFixture _fixture = new();

    [Fact]
    public void ShouldContainCommonSafeNamespaces_WhenDefaultOptionsCreated()
    {
        var options = SecurityAnalysisOptionsTestsFixture.CreateOptions();

        Assert.Contains("System", options.AllowedNamespaces);
        Assert.Contains("System.Collections.Generic", options.AllowedNamespaces);
        Assert.Contains("System.Linq", options.AllowedNamespaces);
        Assert.Contains("System.Text", options.AllowedNamespaces);
        Assert.Contains("System.Text.Json", options.AllowedNamespaces);
    }

    [Fact]
    public void ShouldContainDangerousTypes_WhenDefaultOptionsCreated()
    {
        var options = SecurityAnalysisOptionsTestsFixture.CreateOptions();

        Assert.Contains("System.Diagnostics.Process", options.BlockedTypes);
        Assert.Contains("System.Reflection.Assembly", options.BlockedTypes);
        Assert.Contains("System.Net.Sockets.Socket", options.BlockedTypes);
        Assert.Contains("System.AppDomain", options.BlockedTypes);
    }

    [Fact]
    public void ShouldBeRestrictive_WhenDefaultSecurityOptionsCreated()
    {
        var options = SecurityAnalysisOptionsTestsFixture.CreateOptions();

        Assert.False(options.AllowFileIO);
        Assert.False(options.AllowNetworking);
        Assert.False(options.AllowReflection);
        Assert.False(options.AllowProcessExec);
        Assert.False(options.AllowUnsafeCode);
    }
}

#endregion

#region SandboxPermissions Tests

public class SandboxPermissionsTests
{
    private readonly SandboxPermissionsTestsFixture _fixture = new();

    [Fact]
    public void ShouldBeRestrictive_WhenDefaultPermissionsCreated()
    {
        var permissions = SandboxPermissionsTestsFixture.CreatePermissions();

        Assert.False(permissions.AllowFileRead);
        Assert.False(permissions.AllowFileWrite);
        Assert.False(permissions.AllowNetworkAccess);
        Assert.False(permissions.AllowProcessExec);
        Assert.False(permissions.AllowReflection);
        Assert.Null(permissions.WorkingDirectory);
        Assert.Null(permissions.AllowedPaths);
    }
}

#endregion

#region SandboxExecutionRequest Tests

public class SandboxExecutionRequestTests
{
    private readonly SandboxExecutionRequestTestsFixture _fixture = new();

    [Fact]
    public void ShouldHaveCorrectDefaults_WhenExecutionRequestCreated()
    {
        var request = SandboxExecutionRequestTestsFixture.CreateRequest();

        Assert.Empty(request.Code);
        Assert.Equal("csharp", request.Language);
        Assert.Equal(TimeoutQuick, request.Timeout);
        Assert.Equal(256 * 1024 * 1024, request.MaxMemoryBytes);
        Assert.Equal(50, request.MaxCpuPercent);
        Assert.Equal(50_000, request.MaxOutputBytes);
    }
}

#endregion
