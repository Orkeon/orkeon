using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Infrastructure.Sandbox;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Sandbox.DockerSandbox;

/// <summary>
/// The Docker sandbox is what SECURITY.md points at for "any code-execution scenario with
/// untrusted input", so its container flags are the boundary itself, not a detail. These
/// pin the confinement the run line must carry; the arguments are built without launching
/// anything.
/// </summary>
public class DockerSandboxHardeningTests
{
    private static async Task<string> BuildArgsAsync(bool allowNetwork = false, bool allowWrite = false)
    {
        await using var sandbox = new Orkeon.Infrastructure.Sandbox.DockerSandbox(
            new MockLogger<Orkeon.Infrastructure.Sandbox.DockerSandbox>(),
            Options.Create(new SandboxOptions()),
            Options.Create(new DockerSandboxOptions()),
            new MockFileSystemService("/tmp"));

        var request = new SandboxExecutionRequest
        {
            Code = "// nothing is executed here",
            Permissions = new SandboxPermissions
            {
                AllowNetworkAccess = allowNetwork,
                AllowFileWrite = allowWrite
            }
        };

        return sandbox.BuildDockerArgsForTest(request, maxMemory: 268435456, timeout: TimeSpan.FromSeconds(30));
    }

    [Theory]
    // Root inside the container was the default: no capability was dropped, and nothing
    // stopped a setuid binary from re-granting privileges.
    [InlineData("--cap-drop=ALL")]
    [InlineData("--security-opt=no-new-privileges")]
    [InlineData("--pids-limit=")]
    public async Task ShouldConfineTheContainer_WhenBuildingTheRunLine(string expected)
    {
        Assert.Contains(expected, await BuildArgsAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepTheExistingLimits_WhenHardeningIsAdded()
    {
        var args = await BuildArgsAsync();

        Assert.Contains("--rm", args, StringComparison.Ordinal);
        Assert.Contains("--memory=268435456", args, StringComparison.Ordinal);
        Assert.Contains("--network=none", args, StringComparison.Ordinal);
        Assert.Contains("--read-only", args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldStillOpenTheNetwork_WhenThePermissionAllowsIt()
    {
        var args = await BuildArgsAsync(allowNetwork: true);

        Assert.DoesNotContain("--network=none", args, StringComparison.Ordinal);
        // Hardening is not conditional on the permissions.
        Assert.Contains("--cap-drop=ALL", args, StringComparison.Ordinal);
    }
}
