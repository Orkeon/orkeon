using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Tools.Protocol;
using Base_ToolBase = Orkeon.Tools.Abstractions.Base.ToolBase;
using ValidationResult = Orkeon.Tools.Abstractions.Base.ValidationResult;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// A tool that securely executes C# code in an isolated sandbox.
/// Performs static security analysis before execution.
/// </summary>
public class SecureCodeInterpreterTool : Base_ToolBase
{
    private readonly ICodeSandbox _sandbox;
    private readonly ICodeSecurityAnalyzer _analyzer;
    private readonly SandboxOptions _options;

    /// <inheritdoc />
    public override string Name => "code_interpreter";

    /// <inheritdoc />
    public override string Description =>
        "Execute C# code safely in an isolated sandbox. " +
        "Provide C# code as the 'code' parameter. " +
        "The code is statically analyzed for security before execution.";

    /// <inheritdoc />
    public override string Category => "Code Execution";

    /// <inheritdoc />
    public override ToolSchema Schema => new(
        Name: Name,
        Description: Description,
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["code"] = new ParameterSchema(
                Type: "string",
                Description: "The C# code to execute",
                Required: true
            ),
            ["timeout_seconds"] = new ParameterSchema(
                Type: "integer",
                Description: "Maximum execution time in seconds (default: 30)",
                Required: false,
                Default: 30
            )
        }
    );

    /// <summary>Initializes a new instance of <see cref="SecureCodeInterpreterTool"/>.</summary>
    /// <param name="sandbox">The code sandbox for isolated execution.</param>
    /// <param name="analyzer">The static security analyzer for pre-execution checks.</param>
    /// <param name="options">Sandbox configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public SecureCodeInterpreterTool(
        ICodeSandbox sandbox,
        ICodeSecurityAnalyzer analyzer,
        IOptions<SandboxOptions> options,
        ILogger<SecureCodeInterpreterTool>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        _sandbox = sandbox;
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzer = analyzer;
        _options = options?.Value ?? new SandboxOptions();
    }

    /// <inheritdoc />
    protected override Task<ProtocolToolCallResponse> ExecuteCoreAsync(
        ProtocolToolCallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreInnerAsync();

        async Task<ProtocolToolCallResponse> ExecuteCoreInnerAsync()
        {
            // 1. Extract code from request
            if (!request.Parameters.TryGetValue("code", out var codeObj) ||
                codeObj is null)
            {
                return new ProtocolToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: "Missing required parameter: 'code'"
                );
            }

            var code = codeObj.ToString()!;
            if (string.IsNullOrWhiteSpace(code))
            {
                return new ProtocolToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: "Code cannot be empty"
                );
            }

            // 2. Run security analysis
            var securityReport = _analyzer.Analyze(code, _options.SecurityOptions);

            if (!securityReport.IsAllowed)
            {
                return BuildSecurityBlockedResponse(securityReport);
            }

            // 2b. Fail-closed isolation gate: refuse to execute LLM-generated code on a sandbox that
            // provides no OS-level isolation, unless the operator explicitly opted in. The Roslyn
            // analyzer above is defense-in-depth only (bypassable) — the real boundary is the sandbox.
            // The condition and message are shared with LazyProbingCodeSandbox via
            // SandboxIsolationGate (R10.3) so both gates stay strictly identical.
            var caps = _sandbox.Capabilities;
            var isIsolated = SandboxIsolationGate.IsOsIsolated(caps);
            if (!isIsolated && !_options.AllowHostExecution)
            {
                return new ProtocolToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: SandboxIsolationGate.BuildRefusalMessage(caps.SandboxType),
                    Metadata: new Dictionary<string, object?>
                    {
                        ["isolation_unavailable"] = true,
                        ["sandbox_type"] = caps.SandboxType
                    }
                );
            }

            // 3. Parse optional timeout (clamped to reasonable bounds)
            var timeoutSeconds = ResolveTimeoutSeconds(request);

            // 4. Execute in sandbox
            var executionRequest = new SandboxExecutionRequest
            {
                Code = code,
                Language = "csharp",
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                MaxMemoryBytes = _options.MaxMemoryBytes,
                MaxOutputBytes = _options.MaxOutputBytes,
                Permissions = _options.DefaultPermissions
            };

            var result = await _sandbox.ExecuteAsync(executionRequest, cancellationToken).ConfigureAwait(false);

            // 5. Handle result (failures first, then success)
            return BuildExecutionResponse(result, securityReport, timeoutSeconds);
        }
    }

    /// <summary>Builds the response returned when static security analysis blocks the code.</summary>
    private static ProtocolToolCallResponse BuildSecurityBlockedResponse(
        CodeSecurityReport securityReport)
    {
        var violationMessages = securityReport.Violations
            .Select(v => $"[{v.Risk}] {v.Rule}: {v.Description} (line {v.LineNumber})")
            .ToList();

        return new ProtocolToolCallResponse(
            Success: false,
            Result: null,
            Error: $"Code blocked by security analysis (risk: {securityReport.OverallRisk}):\n" +
                   string.Join("\n", violationMessages),
            Metadata: new Dictionary<string, object?>
            {
                ["security_blocked"] = true,
                ["risk_level"] = securityReport.OverallRisk.ToString(),
                ["violation_count"] = securityReport.Violations.Count
            }
        );
    }

    /// <summary>Resolves the optional <c>timeout_seconds</c> parameter, clamped to [1, 300].</summary>
    private int ResolveTimeoutSeconds(ProtocolToolCallRequest request)
    {
        var timeoutSeconds = _options.TimeoutSeconds;
        if (request.Parameters.TryGetValue("timeout_seconds", out var timeoutObj))
        {
            if (timeoutObj is JsonElement je && je.TryGetInt32(out var ts))
                timeoutSeconds = ts;
            else if (timeoutObj is int ti)
                timeoutSeconds = ti;
            else if (timeoutObj is long tl)
                timeoutSeconds = (int)tl;
            else if (int.TryParse(timeoutObj?.ToString(), out var tp))
                timeoutSeconds = tp;
        }

        return Math.Clamp(timeoutSeconds, 1, 300);
    }

    /// <summary>Maps a completed sandbox execution to the tool response (timeout / memory / error / success).</summary>
    private ProtocolToolCallResponse BuildExecutionResponse(
        SandboxExecutionResult result,
        CodeSecurityReport securityReport,
        int timeoutSeconds)
    {
        if (result.TimedOut)
        {
            return new ProtocolToolCallResponse(
                Success: false,
                Result: null,
                Error: $"Code execution timed out after {timeoutSeconds} seconds",
                Metadata: new Dictionary<string, object?>
                {
                    ["timed_out"] = true,
                    ["duration_ms"] = result.Duration.TotalMilliseconds
                }
            );
        }

        if (result.MemoryExceeded)
        {
            return new ProtocolToolCallResponse(
                Success: false,
                Result: null,
                Error: $"Code exceeded memory limit ({_options.MaxMemoryBytes / (1024 * 1024)} MB)",
                Metadata: new Dictionary<string, object?>
                {
                    ["memory_exceeded"] = true,
                    ["memory_used_bytes"] = result.MemoryUsedBytes
                }
            );
        }

        if (!result.Success)
        {
            return new ProtocolToolCallResponse(
                Success: false,
                Result: null,
                Error: result.Error ?? "Code execution failed",
                Metadata: new Dictionary<string, object?>
                {
                    ["exit_code"] = result.ExitCode,
                    ["duration_ms"] = result.Duration.TotalMilliseconds
                }
            );
        }

        // Success
        return new ProtocolToolCallResponse(
            Success: true,
            Result: new Dictionary<string, object?>
            {
                ["output"] = result.Output,
                ["exit_code"] = result.ExitCode,
                ["duration_ms"] = result.Duration.TotalMilliseconds,
                ["memory_used_bytes"] = result.MemoryUsedBytes
            },
            Error: null,
            Metadata: new Dictionary<string, object?>
            {
                ["sandbox_type"] = _sandbox.Capabilities.SandboxType,
                ["security_violations"] = securityReport.Violations.Count
            }
        );
    }

    /// <inheritdoc />
    protected override ValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue("code", out var codeValue))
            return ValidationResult.Failed("Required parameter 'code' is missing");

        var code = codeValue?.ToString();
        if (string.IsNullOrWhiteSpace(code))
            return ValidationResult.Failed("Code cannot be empty");

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    protected override ValidationResult ValidateInputInternal(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Failed("Input cannot be empty");

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(input, _jsonOptions);
            if (parsed == null || !parsed.ContainsKey("code"))
                return ValidationResult.Failed("Input must contain a 'code' property");

            return ValidationResult.Success();
        }
        catch (JsonException)
        {
            // Treat plain text input as code
            return ValidationResult.Success();
        }
    }
}
