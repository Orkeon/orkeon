using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

public sealed class PhysicalOverlapTests
{
    [Theory]
    [InlineData("/srv/files", "/srv/files/", false, true)]
    [InlineData("/srv/files", "/srv/files/in", false, true)]
    [InlineData("/srv/files/in", "/srv/files", false, true)]
    [InlineData("/", "/srv/files", false, true)]
    [InlineData("/srv/files", "/srv/files-old", false, false)]
    [InlineData("/srv/Files", "/srv/files/in", false, false)]
    [InlineData("/srv/Files", "/srv/files/in", true, true)]
    public void ShouldFindAnOverlap_WhenOneFolderHoldsTheOther(string first, string second, bool ignoreCase, bool overlaps) =>
        Assert.Equal(overlaps, PhysicalOverlap.Between(first, second, ignoreCase));

    [Theory]
    [InlineData("/srv/Files/in", "/srv/files", true, true)]
    [InlineData("/srv/Files/in", "/srv/files", false, false)]
    [InlineData("/srv/files", "/SRV/Files/", true, true)]
    [InlineData("/srv/files", "/srv/files/in", true, false)]
    [InlineData("/srv/Files-old", "/srv/files", true, false)]
    public void ShouldFindAFolderUnderAnother_OneWayOnly(string path, string directory, bool ignoreCase, bool under) =>
        Assert.Equal(under, PhysicalOverlap.IsUnder(path, directory, ignoreCase));
}
