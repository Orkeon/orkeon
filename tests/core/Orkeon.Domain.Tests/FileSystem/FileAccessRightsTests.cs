using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

public class FileAccessRightsTests
{
    [Fact]
    public void None_ShouldBeZero()
    {
        Assert.Equal(0, (int)FileAccessRights.None);
    }

    [Fact]
    public void Read_ShouldBeOne()
    {
        Assert.Equal(1, (int)FileAccessRights.Read);
    }

    [Fact]
    public void Write_ShouldBeTwo()
    {
        Assert.Equal(2, (int)FileAccessRights.Write);
    }

    [Fact]
    public void Create_ShouldBeFour()
    {
        Assert.Equal(4, (int)FileAccessRights.Create);
    }

    [Fact]
    public void Delete_ShouldBeEight()
    {
        Assert.Equal(8, (int)FileAccessRights.Delete);
    }

    [Fact]
    public void ReadOnly_ShouldEqualRead()
    {
        Assert.Equal(FileAccessRights.Read, FileAccessRights.ReadOnly);
        Assert.Equal(1, (int)FileAccessRights.ReadOnly);
    }

    [Fact]
    public void ReadWrite_ShouldBeReadWriteCreateDelete()
    {
        var expected = FileAccessRights.Read | FileAccessRights.Write |
                       FileAccessRights.Create | FileAccessRights.Delete;
        Assert.Equal(FileAccessRights.ReadWrite, expected);
        Assert.Equal(15, (int)FileAccessRights.ReadWrite);
    }

    [Fact]
    public void ReadWriteNoDelete_ShouldBeReadWriteCreate()
    {
        var expected = FileAccessRights.Read | FileAccessRights.Write |
                       FileAccessRights.Create;
        Assert.Equal(FileAccessRights.ReadWriteNoDelete, expected);
        Assert.Equal(7, (int)FileAccessRights.ReadWriteNoDelete);
    }

    [Theory]
    [InlineData(FileAccessRights.ReadWrite, FileAccessRights.Read, true)]
    [InlineData(FileAccessRights.ReadWrite, FileAccessRights.Write, true)]
    [InlineData(FileAccessRights.ReadWrite, FileAccessRights.Create, true)]
    [InlineData(FileAccessRights.ReadWrite, FileAccessRights.Delete, true)]
    [InlineData(FileAccessRights.ReadOnly, FileAccessRights.Write, false)]
    [InlineData(FileAccessRights.ReadOnly, FileAccessRights.Delete, false)]
    [InlineData(FileAccessRights.ReadWriteNoDelete, FileAccessRights.Read, true)]
    [InlineData(FileAccessRights.ReadWriteNoDelete, FileAccessRights.Write, true)]
    [InlineData(FileAccessRights.ReadWriteNoDelete, FileAccessRights.Create, true)]
    [InlineData(FileAccessRights.ReadWriteNoDelete, FileAccessRights.Delete, false)]
    [InlineData(FileAccessRights.None, FileAccessRights.Read, false)]
    public void HasFlag_Combinations(FileAccessRights rights, FileAccessRights flag, bool expected)
    {
        Assert.Equal(expected, rights.HasFlag(flag));
    }
}
