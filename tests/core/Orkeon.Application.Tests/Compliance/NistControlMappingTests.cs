using Orkeon.Application.Interfaces.Compliance;

namespace Orkeon.Application.Tests.Compliance;

public class NistControlMappingTests
{
    private readonly NistControlMappingTestsFixture _fixture = new();

    [Fact]
    public void GetAllControls_ReturnsNonEmptyList()
    {
        var controls = NistControlMapping.GetAllControls();

        Assert.NotNull(controls);
        Assert.NotEmpty(controls);
    }

    [Fact]
    public void GetAllControls_ReturnsExpectedCount()
    {
        var controls = NistControlMapping.GetAllControls();

        // 4 AC + 4 AU + 3 CM + 3 IR + 2 RA + 4 SI + 3 SC + 2 PM = 25
        Assert.Equal(25, controls.Count);
    }

    [Theory]
    [MemberData(nameof(FamilyData))]
    public void GetControlsByFamily_ReturnsOnlyMatchingFamily(string family)
    {
        var controls = NistControlMapping.GetControlsByFamily(family);

        Assert.NotEmpty(controls);
        Assert.All(controls, c => Assert.Equal(family, c.Family));
    }

    [Fact]
    public void GetControlsByFamily_UnknownFamily_ReturnsEmptyList()
    {
        var controls = NistControlMapping.GetControlsByFamily("XX");

        Assert.NotNull(controls);
        Assert.Empty(controls);
    }

    [Fact]
    public void GetControl_KnownId_ReturnsControl()
    {
        var control = NistControlMapping.GetControl("AC-1");

        Assert.NotNull(control);
        Assert.Equal("AC-1", control.ControlId);
        Assert.Equal("AC", control.Family);
        Assert.Equal("Access Control Policy", control.Title);
    }

    [Fact]
    public void GetControl_UnknownId_ReturnsNull()
    {
        var control = NistControlMapping.GetControl("UNKNOWN-99");

        Assert.Null(control);
    }

    [Fact]
    public void AllControls_HaveValidControlId()
    {
        foreach (var control in _fixture.AllControls)
        {
            Assert.False(string.IsNullOrWhiteSpace(control.ControlId),
                "ControlId should not be null or whitespace");
            Assert.Contains("-", control.ControlId);
        }
    }

    [Fact]
    public void AllControls_HaveValidFamily()
    {
        foreach (var control in _fixture.AllControls)
        {
            Assert.False(string.IsNullOrWhiteSpace(control.Family),
                $"Family should not be null or whitespace for {control.ControlId}");
            Assert.Equal(2, control.Family.Length);
        }
    }

    [Fact]
    public void AllControls_HaveValidTitle()
    {
        foreach (var control in _fixture.AllControls)
        {
            Assert.False(string.IsNullOrWhiteSpace(control.Title),
                $"Title should not be null or whitespace for {control.ControlId}");
        }
    }

    [Fact]
    public void AllControls_HaveValidDescription()
    {
        foreach (var control in _fixture.AllControls)
        {
            Assert.False(string.IsNullOrWhiteSpace(control.Description),
                $"Description should not be null or whitespace for {control.ControlId}");
        }
    }

    [Fact]
    public void AllControls_DefaultToNotImplemented()
    {
        foreach (var control in _fixture.AllControls)
        {
            Assert.Equal(ControlStatus.NotImplemented, control.Status);
        }
    }

    [Fact]
    public void AllExpectedFamilies_HaveAtLeastOneControl()
    {
        foreach (var family in NistControlMappingTestsFixture.AllExpectedFamilies)
        {
            var controls = NistControlMapping.GetControlsByFamily(family);
            Assert.True(controls.Count > 0, $"Family '{family}' should have at least one control");
        }
    }

    [Fact]
    public void AllControls_ControlIdStartsWithFamily()
    {
        foreach (var control in _fixture.AllControls)
        {
            Assert.StartsWith(control.Family, control.ControlId);
        }
    }

    [Fact]
    public void GetAllControls_ReturnsUniqueControlIds()
    {
        var controls = NistControlMapping.GetAllControls();
        var ids = controls.Select(c => c.ControlId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    public static TheoryData<string> FamilyData()
    {
        var data = new TheoryData<string>();
        foreach (var family in NistControlMappingTestsFixture.AllExpectedFamilies)
        {
            data.Add(family);
        }
        return data;
    }
}
