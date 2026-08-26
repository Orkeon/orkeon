using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;

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
