using System.Reflection;
using Orkeon.Domain.Constants.Platform;

namespace Orkeon.Domain.Tests.Constants.Platform;

/// <summary>
/// OrkeonRelease is internal, yet Orkeon.Scripting and Orkeon.Tools.Abstractions - packages shipped on their own -
/// compile against it through InternalsVisibleTo: its full name and its members are a contract between packages that a
/// rename would break at run time only. These tests pin them.
/// </summary>
public sealed class OrkeonReleaseContractTests
{
    [Fact]
    public void ShouldKeepItsFullName_ForThePackagesThatReadIt()
    {
        var type = typeof(OrkeonRelease);

        Assert.Equal("Orkeon.Domain.Constants.Platform.OrkeonRelease", type.FullName);
        Assert.Equal("Orkeon.Domain", type.Assembly.GetName().Name);
        Assert.True(type.IsAbstract && type.IsSealed, "OrkeonRelease stays a static class");
    }

    [Fact]
    public void ShouldKeepItsMembers_ForThePackagesThatReadThem()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var type = typeof(OrkeonRelease);

        var properties = type.GetProperties(flags).Where(p => p.GetMethod?.IsPublic == true).ToList();
        var property = Assert.Single(properties);
        Assert.Equal("Version", property.Name);
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Null(property.SetMethod);

        var publicMethods = type.GetMethods(flags).Where(m => m.IsPublic).Select(m => m.Name).ToList();
        Assert.Equal(["get_Version"], publicMethods);
        Assert.False(string.IsNullOrWhiteSpace(OrkeonRelease.Version));
    }
}
