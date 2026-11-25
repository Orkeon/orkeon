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
}
