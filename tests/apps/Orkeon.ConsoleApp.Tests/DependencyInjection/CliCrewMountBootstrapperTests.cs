using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// <c>--crews-dir</c>: each folder is mounted read-only (<c>/crews</c>, then <c>/crews-1</c>, …) and
/// registered with the script-host, which resolves <c>runCrew("&lt;name&gt;")</c> to
/// <c>&lt;dir&gt;/&lt;name&gt;/crew.ork.ts</c> — the contract a product's commands rely on. The folders
/// are entries of the REPL, like the script of <c>orkeon run</c>: their files are readable wherever
/// the folder is (GAP-49, decision 3).
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

    private static ServiceProvider Build(params string[] crewDirs)
    {
        var services = new ServiceCollection();
        services.AddOptions<FileSystemOptions>();
        services.AddOptions<ScriptHostFacadeOptions>();
        services.AddOptions<PathSecurityOptions>();
        services.AddScriptHostCrewMounts(crewDirs);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Empty_list_is_a_no_op()
    {
        using var provider = Build();

        Assert.Empty(provider.GetRequiredService<IOptions<FileSystemOptions>>().Value.Mounts);
        Assert.True(provider.GetRequiredService<IOptions<ScriptHostFacadeOptions>>().Value.CrewDirectories.IsDefaultOrEmpty);
    }

    [Fact]
    public void Each_folder_is_mounted_read_only_and_registered_with_the_script_host()
    {
        var second = Path.Combine(Path.GetTempPath(), "orkeon-crews-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(second);
        try
        {
            using var provider = Build(_crews, second);

            var mounts = provider.GetRequiredService<IOptions<FileSystemOptions>>().Value.Mounts;
            Assert.Equal(
                $"{Path.GetFullPath(_crews)}:/crews:ro|{Path.GetFullPath(second)}:/crews-1:ro",
                string.Join("|", mounts));
            Assert.Equal(
                "/crews|/crews-1",
                string.Join("|", provider.GetRequiredService<IOptions<ScriptHostFacadeOptions>>().Value.CrewDirectories));
        }
        finally
        {
            try { Directory.Delete(second, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void A_folder_already_registered_is_not_registered_twice()
    {
        var services = new ServiceCollection();
        services.AddOptions<FileSystemOptions>();
        services.AddOptions<ScriptHostFacadeOptions>();
        services.AddScriptHostCrewMounts([_crews]);
        services.AddScriptHostCrewMounts([_crews]);
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetRequiredService<IOptions<FileSystemOptions>>().Value.Mounts);
        Assert.Single(provider.GetRequiredService<IOptions<ScriptHostFacadeOptions>>().Value.CrewDirectories);
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
