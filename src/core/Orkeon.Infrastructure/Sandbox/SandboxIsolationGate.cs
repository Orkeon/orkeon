using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// Single source of truth for the R2.2 fail-closed isolation gate: LLM-generated code must
/// not run on a sandbox that provides no OS-level isolation unless the operator explicitly
/// opted in via <see cref="SandboxOptions.AllowHostExecution"/>.
/// Shared by <see cref="SecureCodeInterpreterTool"/> (tool-level gate) and
/// <see cref="LazyProbingCodeSandbox"/> (execution-level gate, R10.3) so both refuse with
/// the exact same message.
/// </summary>
internal static class SandboxIsolationGate
{
    /// <summary>
    /// Whether the sandbox provides OS-level isolation (network and filesystem).
    /// </summary>
    internal static bool IsOsIsolated(SandboxCapabilities capabilities) =>
        capabilities.SupportsNetworkIsolation && capabilities.SupportsFileSystemIsolation;

    /// <summary>
    /// Builds the fail-closed refusal message for a non-isolating sandbox.
    /// </summary>
    internal static string BuildRefusalMessage(string sandboxType) =>
        "Code execution refused: no OS-isolating sandbox is available " +
        $"(sandbox '{sandboxType}' provides no network/filesystem isolation). " +
        "Install/enable Docker so DockerSandbox can run, or explicitly opt in to " +
        "untrusted host execution via SandboxOptions.AllowHostExecution=true " +
        "(RCE-equivalent on the host — trusted code only).";
}
