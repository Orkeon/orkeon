using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using PathValidatorSut = Orkeon.Infrastructure.Security.PathValidator;

namespace Orkeon.Infrastructure.Tests.Security;

/// <summary>
/// Issue #215: reading a script is not executing it. A built-in script extension refuses
/// every operation but a plain read; binaries, configured extensions and every other check
/// (system paths, traversal, workspace) keep refusing reads.
/// </summary>
public class PathValidatorBlockedExtensionReadTests
{
    private static PathValidatorSut CreateValidator(bool blockReads = false, params string[] additional)
    {
        var options = new PathSecurityOptions
        {
            DefaultWorkspaceRoot = "/workspace",
            BlockReadsOfBlockedExtensions = blockReads,
        };
        foreach (var extension in additional)
            options.AdditionalBlockedExtensions.Add(extension);
        return new PathValidatorSut(options, NullLogger<PathValidatorSut>.Instance);
    }

    [Theory]
    [InlineData("/workspace/src/run.sh")]
    [InlineData("/workspace/src/run.bash")]
    [InlineData("/workspace/src/build.ps1")]
    [InlineData("/workspace/src/build.bat")]
    [InlineData("/workspace/src/build.cmd")]
    [InlineData("/workspace/src/tool.vbs")]
    [InlineData("/workspace/src/tool.wsf")]
    public void Read_of_a_script_extension_is_allowed(string path)
    {
        var result = CreateValidator().ValidatePath(path, FileAccessRights.Read);

        Assert.True(result.IsAllowed, result.DenialReason);
        Assert.NotNull(result.ResolvedPath);
    }

    [Theory]
    [InlineData(FileAccessRights.Write)]
    [InlineData(FileAccessRights.Create)]
    [InlineData(FileAccessRights.Delete)]
    [InlineData(FileAccessRights.ReadWrite)]
    [InlineData(FileAccessRights.ReadWriteNoDelete)]
    [InlineData(FileAccessRights.None)]
    public void Any_other_operation_on_a_script_extension_is_refused(FileAccessRights rights)
    {
        var result = CreateValidator().ValidatePath("/workspace/src/run.sh", rights);

        Assert.False(result.IsAllowed);
        Assert.Contains(".sh", result.DenialReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_operation_less_overload_still_refuses_a_script_extension()
    {
        var result = CreateValidator().ValidatePath("/workspace/src/run.sh");

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void The_option_blocks_reads_of_a_script_extension_again()
    {
        var result = CreateValidator(blockReads: true).ValidatePath("/workspace/src/run.sh", FileAccessRights.Read);

        Assert.False(result.IsAllowed);
        Assert.Contains(".sh", result.DenialReason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/workspace/bin/app.exe")]
    [InlineData("/workspace/bin/plugin.dll")]
    [InlineData("/workspace/bin/setup.msi")]
    [InlineData("/workspace/bin/app.com")]
    [InlineData("/workspace/bin/saver.scr")]
    [InlineData("/workspace/bin/link.pif")]
    public void Read_of_a_binary_extension_stays_refused(string path)
    {
        var result = CreateValidator().ValidatePath(path, FileAccessRights.Read);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Read_of_a_configured_extension_stays_refused()
    {
        var result = CreateValidator(false, ".pem").ValidatePath("/workspace/keys/server.pem", FileAccessRights.Read);

        Assert.False(result.IsAllowed);
        Assert.Contains(".pem", result.DenialReason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/workspace/../etc/run.sh")]
    [InlineData("/outside/run.sh")]
    public void Read_does_not_loosen_system_paths_traversal_or_the_workspace(string path)
    {
        var result = CreateValidator().ValidatePath(path, FileAccessRights.Read);

        Assert.False(result.IsAllowed);
    }
}
