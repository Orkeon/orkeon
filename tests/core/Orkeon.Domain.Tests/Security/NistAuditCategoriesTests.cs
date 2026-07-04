using Orkeon.Domain.Security;

namespace Orkeon.Domain.Tests.Security;

public class NistAuditCategoriesTests
{
    [Theory]
    [MemberData(nameof(PrimaryFamilyData))]
    public void GetNistFamily_ReturnsCorrectFamily_ForEachCategory(AuditCategory category, string expectedFamily)
    {
        var result = NistAuditCategories.GetNistFamily(category);

        Assert.Equal(expectedFamily, result);
    }

    [Theory]
    [MemberData(nameof(AllCategoryData))]
    public void GetAllNistFamilies_ReturnsNonEmptyList_ForEachCategory(AuditCategory category)
    {
        var families = NistAuditCategories.GetAllNistFamilies(category);

        Assert.NotNull(families);
        Assert.NotEmpty(families);
    }

    [Theory]
    [MemberData(nameof(AllCategoryData))]
    public void GetAllNistFamilies_ContainsPrimaryFamily(AuditCategory category)
    {
        var primary = NistAuditCategories.GetNistFamily(category);
        var all = NistAuditCategories.GetAllNistFamilies(category);

        Assert.Contains(primary, all);
    }

    [Fact]
    public void GetAllNistFamilies_LlmCall_ReturnsMultipleFamilies()
    {
        var families = NistAuditCategories.GetAllNistFamilies(AuditCategory.LlmCall);

        Assert.True(families.Count >= 2, "LlmCall should map to multiple NIST families");
        Assert.Contains(NistAuditCategories.SystemIntegrity, families);
        Assert.Contains(NistAuditCategories.AuditAccountability, families);
        Assert.Contains(NistAuditCategories.RiskAssessment, families);
    }

    [Fact]
    public void GetAllNistFamilies_SecurityEvent_ReturnsMultipleFamilies()
    {
        var families = NistAuditCategories.GetAllNistFamilies(AuditCategory.SecurityEvent);

        Assert.True(families.Count >= 2, "SecurityEvent should map to multiple NIST families");
        Assert.Contains(NistAuditCategories.IncidentResponse, families);
        Assert.Contains(NistAuditCategories.AuditAccountability, families);
    }

    [Fact]
    public void AllConstants_AreNonNullAndNonEmpty()
    {
        foreach (var constant in NistAuditCategoriesTestsFixture.AllFamilyConstants)
        {
            Assert.False(string.IsNullOrEmpty(constant), $"NIST family constant should not be null or empty");
        }
    }

    [Fact]
    public void AllConstants_AreTwoCharacterCodes()
    {
        foreach (var constant in NistAuditCategoriesTestsFixture.AllFamilyConstants)
        {
            Assert.Equal(2, constant.Length);
            Assert.True(constant.All(c => !char.IsLetter(c) || char.IsUpper(c)), $"NIST family code '{constant}' should be uppercase");
        }
    }

    [Fact]
    public void AllAuditCategories_AreCoveredByPrimaryFamilyMapping()
    {
        var expectedFamilies = NistAuditCategoriesTestsFixture.ExpectedPrimaryFamilies;

        foreach (var category in NistAuditCategoriesTestsFixture.AllCategories)
        {
            Assert.True(
                expectedFamilies.ContainsKey(category),
                $"AuditCategory.{category} is not covered in expected primary families");

            var actual = NistAuditCategories.GetNistFamily(category);
            Assert.Equal(expectedFamilies[category], actual);
        }
    }

    [Fact]
    public void GetNistFamily_UnknownCategory_ReturnsAuditAccountability()
    {
        // Cast an invalid value to test the default case
        var unknown = (AuditCategory)999;
        var result = NistAuditCategories.GetNistFamily(unknown);

        Assert.Equal(NistAuditCategories.AuditAccountability, result);
    }

    public static TheoryData<AuditCategory, string> PrimaryFamilyData()
    {
        var data = new TheoryData<AuditCategory, string>();
        foreach (var (category, family) in NistAuditCategoriesTestsFixture.ExpectedPrimaryFamilies)
        {
            data.Add(category, family);
        }
        return data;
    }

    public static TheoryData<AuditCategory> AllCategoryData()
    {
        var data = new TheoryData<AuditCategory>();
        foreach (var category in NistAuditCategoriesTestsFixture.AllCategories)
        {
            data.Add(category);
        }
        return data;
    }
}
