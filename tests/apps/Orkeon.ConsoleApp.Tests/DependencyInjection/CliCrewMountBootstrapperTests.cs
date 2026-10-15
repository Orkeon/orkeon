using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// <c>--crews-dir</c> folders are entries of the REPL, like the script of <c>orkeon run</c>: their
/// files are readable wherever the folder is (GAP-49, decision 3).
/// </summary>
public sealed class CliCrewMountBootstrapperTests : IDisposable
{
    private readonly string _crews;

    public CliCrewMountBootstrapperTests()
    {
        // Under the temp directory: outside the tests' working directory.
        _crews = Path.Combine(Path.GetTempPath(), "orkeon-crews-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_crews, "main-loop"));
        File.WriteAllText(Path.Combine(_crews, "main-loop", "crew.ork.ts"), "// crew");
    }

    public void Dispose()
    {
        try { Directory.Delete(_crews, true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_crews_folder_outside_the_working_directory_is_readable()
    {
        var services = new ServiceCollection();
        services.AddOptions<FileSystemOptions>();
        services.AddOptions<PathSecurityOptions>();
        services.AddScriptHostCrewMounts([_crews]);
        using var provider = services.BuildServiceProvider();
        var security = provider.GetRequiredService<IOptions<PathSecurityOptions>>();

        Assert.Contains(Path.GetFullPath(_crews), security.Value.AdditionalAllowedDirectories);
        var validation = new PathValidator(security, NullLogger<PathValidator>.Instance)
            .ValidatePath(Path.Combine(_crews, "main-loop", "crew.ork.ts"));
        Assert.True(validation.IsAllowed, validation.DenialReason);
    }
}
