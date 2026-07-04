using Orkeon.Domain.Tools.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using PathValidatorSut = Orkeon.Infrastructure.Security.PathValidator;

namespace Orkeon.Infrastructure.Tests.Security;

public class PathValidatorTests
{
    private readonly ILogger<PathValidatorSut> _logger = NullLogger<PathValidatorSut>.Instance;

    private PathValidatorSut CreateValidator(PathSecurityOptions? options = null)
    {
        return new PathValidatorSut(options ?? new PathSecurityOptions
        {
            DefaultWorkspaceRoot = "/workspace"
        }, _logger);
    }

    // ---- Basic allowed/denied paths ----

    [Fact]
    public void ShouldReturnAllowed_WhenPathIsUnderWorkspace()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/src/file.txt");

        Assert.True(result.IsAllowed);
        Assert.NotNull(result.ResolvedPath);
        Assert.Contains("workspace", result.ResolvedPath);
        Assert.Null(result.DenialReason);
    }

    [Fact]
    public void ShouldReturnDenied_WhenRelativePathTraversalDetected()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/src/../../../etc/passwd");

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
    }

    [Fact]
    public void ShouldReturnDenied_WhenAbsolutePathIsOutsideWorkspace()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/tmp/secret.txt");

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
    }

    [Fact]
    public void ShouldReturnDenied_WhenDoubleDotResolvesOutsideWorkspace()
    {
        var validator = CreateValidator();
        // Even though Path.GetFullPath resolves "..", the resolved path is outside workspace
        var result = validator.ValidatePath("/workspace/../etc/shadow");

        Assert.False(result.IsAllowed);
    }

    // ---- Extension checks ----

    [Fact]
    public void ShouldReturnDenied_WhenExtensionIsExe()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/malware.exe");

        Assert.False(result.IsAllowed);
        Assert.Contains(".exe", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldReturnDenied_WhenExtensionIsDll()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/library.dll");

        Assert.False(result.IsAllowed);
        Assert.Contains(".dll", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldReturnAllowed_WhenExtensionIsTxt()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/readme.txt");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldReturnDenied_WhenExtensionIsSh()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/script.sh");

        Assert.False(result.IsAllowed);
        Assert.Contains(".sh", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldReturnDenied_WhenExtensionIsBat()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/script.bat");

        Assert.False(result.IsAllowed);
        Assert.Contains(".bat", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- System paths ----

    [Fact]
    public void ShouldReturnDenied_WhenPathIsEtcShadow()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/etc/shadow");

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void ShouldReturnDenied_WhenPathIsEtcPasswd()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/etc/passwd");

        Assert.False(result.IsAllowed);
    }

    // ---- Workspace root boundary ----

    [Fact]
    public void ShouldReturnAllowed_WhenPathIsWorkspaceRootExact()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldReturnDenied_WhenPathIsSimilarToWorkspaceButDifferent()
    {
        // CRITICAL: /workspace-evil/ must NOT match /workspace/
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace-evil/file.txt");

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
    }

    // ---- Null/empty ----

    [Fact]
    public void ShouldReturnDenied_WhenPathIsNull()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath(null!);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
    }

    [Fact]
    public void ShouldReturnDenied_WhenPathIsEmpty()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("");

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
    }

    [Fact]
    public void ShouldReturnDenied_WhenPathIsWhitespace()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("   ");

        Assert.False(result.IsAllowed);
    }

    // ---- Special characters ----

    [Fact]
    public void ShouldReturnAllowed_WhenPathContainsSpaces()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/my folder/my file.txt");

        Assert.True(result.IsAllowed);
        Assert.NotNull(result.ResolvedPath);
    }

    [Fact]
    public void ShouldReturnAllowed_WhenPathContainsUnicodeCharacters()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/dossier/fichier-accentue.txt");

        Assert.True(result.IsAllowed);
    }

    // ---- Additional blocked extensions from options ----

    [Fact]
    public void ShouldReturnDenied_WhenExtensionIsInAdditionalBlockedList()
    {
        var options = new PathSecurityOptions
        {
            DefaultWorkspaceRoot = "/workspace",
            AdditionalBlockedExtensions = { ".dangerous" }
        };
        var validator = CreateValidator(options);

        var result = validator.ValidatePath("/workspace/file.dangerous");

        Assert.False(result.IsAllowed);
        Assert.Contains(".dangerous", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Additional allowed directories ----

    [Fact]
    public void ShouldReturnAllowed_WhenPathIsInAdditionalAllowedDirectory()
    {
        var options = new PathSecurityOptions
        {
            DefaultWorkspaceRoot = "/workspace",
            AdditionalAllowedDirectories = { "/tmp/allowed" }
        };
        var validator = CreateValidator(options);

        var result = validator.ValidatePath("/tmp/allowed/data.txt");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldReturnDenied_WhenPathIsNotInAdditionalAllowedDirectory()
    {
        var options = new PathSecurityOptions
        {
            DefaultWorkspaceRoot = "/workspace",
            AdditionalAllowedDirectories = { "/tmp/allowed" }
        };
        var validator = CreateValidator(options);

        var result = validator.ValidatePath("/tmp/not-allowed/data.txt");

        Assert.False(result.IsAllowed);
    }

    // ---- Very long path ----

    [Fact]
    public void ShouldReturnDenied_WhenPathExceedsMaximumLength()
    {
        var validator = CreateValidator();
        var longPath = "/workspace/" + new string('a', 5000) + ".txt";
        var result = validator.ValidatePath(longPath);

        Assert.False(result.IsAllowed);
        Assert.Contains("maximum", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Workspace root override ----

    [Fact]
    public void ShouldUseOverride_WhenWorkspaceRootIsOverridden()
    {
        var validator = CreateValidator();

        // With default workspace /workspace, /tmp/test.txt is denied
        var denied = validator.ValidatePath("/tmp/test.txt");
        Assert.False(denied.IsAllowed);

        // With workspace root override to /tmp, it should be allowed
        var allowed = validator.ValidatePath("/tmp/test.txt", "/tmp");
        Assert.True(allowed.IsAllowed);
    }

    // ---- Multiple blocked extensions ----

    [Theory]
    [InlineData("/workspace/file.ps1")]
    [InlineData("/workspace/file.cmd")]
    [InlineData("/workspace/file.vbs")]
    [InlineData("/workspace/file.wsf")]
    [InlineData("/workspace/file.msi")]
    [InlineData("/workspace/file.com")]
    [InlineData("/workspace/file.scr")]
    [InlineData("/workspace/file.pif")]
    [InlineData("/workspace/file.bash")]
    public void ShouldReturnDenied_WhenExtensionIsBlocked(string path)
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath(path);

        Assert.False(result.IsAllowed);
    }

    // ---- Allowed extensions ----

    [Theory]
    [InlineData("/workspace/file.cs")]
    [InlineData("/workspace/file.json")]
    [InlineData("/workspace/file.xml")]
    [InlineData("/workspace/file.yaml")]
    [InlineData("/workspace/file.md")]
    [InlineData("/workspace/file.csproj")]
    public void ShouldReturnAllowed_WhenExtensionIsSafe(string path)
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath(path);

        Assert.True(result.IsAllowed);
    }

    // ---- Subdirectory traversal ----

    [Fact]
    public void ShouldReturnAllowed_WhenPathIsDeepSubdirectory()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/src/Orkeon.Domain/Agent/Agent.cs");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldReturnDenied_WhenPathIsRootSshDirectory()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/root/.ssh/id_rsa");

        Assert.False(result.IsAllowed);
    }

    // ---- No extension ----

    [Fact]
    public void ShouldReturnAllowed_WhenFileHasNoExtensionUnderWorkspace()
    {
        var validator = CreateValidator();
        var result = validator.ValidatePath("/workspace/Makefile");

        Assert.True(result.IsAllowed);
    }

    // ---- Static factory methods on PathValidationResult ----

    [Fact]
    public void ShouldHaveCorrectProperties_WhenPathValidationResultIsAllowed()
    {
        var result = PathValidationResult.Allowed("/workspace/test.txt");

        Assert.True(result.IsAllowed);
        Assert.Equal("/workspace/test.txt", result.ResolvedPath);
        Assert.Null(result.DenialReason);
    }

    [Fact]
    public void ShouldHaveCorrectProperties_WhenPathValidationResultIsDenied()
    {
        var result = PathValidationResult.Denied("Not allowed");

        Assert.False(result.IsAllowed);
        Assert.Null(result.ResolvedPath);
        Assert.Equal("Not allowed", result.DenialReason);
    }
}
