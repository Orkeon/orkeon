using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

public sealed class PrivateMountGuardTests
{
    private static IFileSystemService FileSystem(bool isPrivate) => new FakeFileSystemService()
        .AddMount("/inbox", FileAccessRights.Read, isPrivate)
        .AddMount("/workspace", FileAccessRights.ReadWrite);

    [Theory]
    [InlineData("/inbox")]
    [InlineData("/inbox/")]
    [InlineData("/inbox/a/b.txt")]
    [InlineData("/")]
    [InlineData("//inbox//a")]
    [InlineData("/workspace/../inbox/a")]
    [InlineData("\\inbox\\a")]
    [InlineData("/INBOX/a")]
    public void ShouldRefuse_WhenThePathReachesAPrivateMount(string path)
    {
        Assert.Throws<FileAccessDeniedException>(() => PrivateMountGuard.ThrowIfReached(FileSystem(isPrivate: true), path));
        PrivateMountGuard.ThrowIfReached(FileSystem(isPrivate: false), path);
    }

    [Theory]
    [InlineData("/workspace")]
    [InlineData("/inboxes/a")]
    public void ShouldAccept_WhenThePathReachesNone(string path) =>
        Assert.Null(PrivateMountGuard.Reached(FileSystem(isPrivate: true), path));
}
