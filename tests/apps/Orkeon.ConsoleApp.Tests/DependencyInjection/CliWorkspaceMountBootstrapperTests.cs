using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// <c>--mount</c> specs are resolved to absolute and appended to
/// <see cref="FileSystemOptions.Mounts"/>. The interesting part is where the physical segment
/// ends: taking everything before the first <c>':'</c> — what this bootstrapper used to do —
/// reads <c>C:\src:/workspace:ro</c> as the path <c>"C"</c>, resolves it against the working
/// directory and emits a mount string the runtime then refuses. The split is the domain type's
/// job now, and every spec it produces must survive <see cref="FileSystemMount.Parse"/>.
/// </summary>
public sealed class CliWorkspaceMountBootstrapperTests
{
    private static IReadOnlyList<string> Resolve(params string[] specs)
    {
        var services = new ServiceCollection();
        services.AddOptions<FileSystemOptions>();
        services.AddCliWorkspaceMounts(specs);
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<FileSystemOptions>>().Value.Mounts];
    }

    [Fact]
    public void Empty_list_is_a_no_op() => Assert.Empty(Resolve());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_external_mount_is_readable_under_the_flag_only(bool allowExternalMounts)
    {
        // GAP-49, decision 3: a --mount outside the working directory, as for a runner — readable
        // under --allow-external-mounts, mounted and refused at access without it.
        var external = Path.Combine(Path.GetTempPath(), "orkeon-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(external);
        try
        {
            File.WriteAllText(Path.Combine(external, "a.txt"), "a");
            var services = new ServiceCollection();
            services.AddOptions<FileSystemOptions>();
            services.AddOptions<PathSecurityOptions>();
            services.AddCliWorkspaceMounts([$"{external}:/workspace:rw"], allowExternalMounts);
            using var provider = services.BuildServiceProvider();
            var security = provider.GetRequiredService<IOptions<PathSecurityOptions>>();

            Assert.Single(provider.GetRequiredService<IOptions<FileSystemOptions>>().Value.Mounts);
            Assert.Equal(allowExternalMounts, security.Value.AdditionalAllowedDirectories.Contains(Path.GetFullPath(external)));
            var validation = new PathValidator(security, NullLogger<PathValidator>.Instance)
                .ValidatePath(Path.Combine(external, "a.txt"));
            Assert.Equal(allowExternalMounts, validation.IsAllowed);
        }
        finally
        {
            try { Directory.Delete(external, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void A_windows_spec_keeps_its_drive_letter()
    {
        var mount = FileSystemMount.Parse(Assert.Single(Resolve(@"C:\src:/workspace:ro")));

        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.EndsWith(@"src", mount.BasePath, StringComparison.Ordinal);
        // The drive letter is a path, not a segment: it must never be resolved on its own.
        Assert.NotEqual("C", mount.BasePath);
        Assert.DoesNotContain(":/workspace:ro", mount.BasePath, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_physical_path_becomes_absolute()
    {
        var mount = FileSystemMount.Parse(Assert.Single(Resolve("./data:/workspace:rw")));

        Assert.Equal(Path.GetFullPath("./data"), mount.BasePath);
        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mount.DefaultRights);
    }

    [Fact]
    public void A_quoted_path_survives_the_resolution()
    {
        var mount = FileSystemMount.Parse(Assert.Single(Resolve(@"""./odd:name"":/data:ro")));

        Assert.Equal(Path.GetFullPath("./odd:name"), mount.BasePath);
        Assert.Equal("/data", mount.VirtualPath);
    }

    [Fact]
    public void Sub_path_overrides_are_preserved()
    {
        var mount = FileSystemMount.Parse(Assert.Single(Resolve("./data:/workspace:rw;cache:ro")));

        var over = Assert.Single(mount.Overrides);
        Assert.Equal("cache", over.RelativePath);
        Assert.Equal(FileAccessRights.ReadOnly, over.Rights);
    }

    [Fact]
    public void A_spec_the_grammar_cannot_read_is_passed_through_for_the_parser_to_reject()
    {
        var spec = Assert.Single(Resolve("no-separator-at-all"));

        Assert.Equal("no-separator-at-all", spec);
        Assert.Throws<FormatException>(() => FileSystemMount.Parse(spec));
    }

    [Fact]
    public void The_same_spec_twice_is_appended_once()
    {
        Assert.Single(Resolve("./data:/workspace:rw", "./data:/workspace:rw"));
    }
}
