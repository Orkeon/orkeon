using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Tests.Sessions.Windows;

/// <summary>
/// <see cref="AclPlan"/>: the ACEs a view becomes for the session's AppContainer SID, parents first.
/// </summary>
public sealed class AclPlanTests
{
    [Fact]
    public void ShouldGrantReadAndExecute_OnAReadOnlyMount()
    {
        var plan = AclPlan.Build([new ConfinedPath(@"C:\data\docs", "/docs", ConfinedAccess.ReadOnly)], []);

        Assert.Equal([new AclEntry(@"C:\data\docs", AclRights.ReadAndExecute, Deny: false)], plan);
    }

    [Fact]
    public void ShouldGrantModify_OnAReadWriteMount()
    {
        var plan = AclPlan.Build([new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite)], []);

        Assert.Equal([new AclEntry(@"C:\work\repo", AclRights.Modify, Deny: false)], plan);
    }

    [Fact]
    public void ShouldDenyEverything_OnAHiddenSubPath()
    {
        var plan = AclPlan.Build(
        [
            new ConfinedPath(@"C:\work\repo\secrets", "/workspace/secrets", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(@"C:\work\repo\.env", "/workspace/.env", ConfinedAccess.HiddenFile),
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
        ], []);

        Assert.Equal(
        [
            new AclEntry(@"C:\work\repo", AclRights.Modify, Deny: false),
            new AclEntry(@"C:\work\repo\.env", AclRights.All, Deny: true),
            new AclEntry(@"C:\work\repo\secrets", AclRights.All, Deny: true),
        ], plan);
    }

    [Fact]
    public void ShouldDenyWriting_OnAReadOnlySubPathOfAReadWriteMount()
    {
        var plan = AclPlan.Build(
        [
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"C:\work\repo\vendor", "/workspace/vendor", ConfinedAccess.ReadOnly),
        ], []);

        Assert.Equal(
        [
            new AclEntry(@"C:\work\repo", AclRights.Modify, Deny: false),
            new AclEntry(@"C:\work\repo\vendor", AclRights.ReadAndExecute, Deny: false),
            new AclEntry(@"C:\work\repo\vendor", AclRights.Write, Deny: true),
        ], plan);
    }

    [Fact]
    public void ShouldNotDenyWriting_OnAReadOnlyMountBesideAReadWriteOneWithAPrefixInCommon()
    {
        var plan = AclPlan.Build(
        [
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"C:\work\repo-docs", "/docs", ConfinedAccess.ReadOnly),
        ], []);

        Assert.DoesNotContain(plan, e => e.Deny);
    }

    [Fact]
    public void ShouldCompareParentsWithoutCase_AsNtfsDoes()
    {
        var plan = AclPlan.Build(
        [
            new ConfinedPath(@"C:\Work\Repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"c:\work\repo\Vendor", "/workspace/vendor", ConfinedAccess.ReadOnly),
        ], []);

        Assert.Contains(new AclEntry(@"c:\work\repo\Vendor", AclRights.Write, Deny: true), plan);
    }

    [Fact]
    public void ShouldGrantReadAndExecute_OnEachExtraReadOnlyPathOnce_BeforeTheMounts()
    {
        var plan = AclPlan.Build(
            [new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite)],
            [@"C:\Users\me\.dotnet\", @"c:\users\me\.dotnet", @"C:\tools"]);

        Assert.Equal(
        [
            new AclEntry(@"C:\Users\me\.dotnet", AclRights.ReadAndExecute, Deny: false),
            new AclEntry(@"C:\tools", AclRights.ReadAndExecute, Deny: false),
            new AclEntry(@"C:\work\repo", AclRights.Modify, Deny: false),
        ], plan);
    }

    [Fact]
    public void ShouldKeepADriveRootWhole()
    {
        var plan = AclPlan.Build([new ConfinedPath(@"D:\", "/d", ConfinedAccess.ReadOnly)], []);

        Assert.Equal(@"D:\", Assert.Single(plan).Path);
    }

    [Fact]
    public void ShouldListEachPathOnce_ForTheLedger()
    {
        var plan = AclPlan.Build(
        [
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"C:\work\repo\vendor", "/workspace/vendor", ConfinedAccess.ReadOnly),
        ], [@"C:\tools"]);

        Assert.Equal([@"C:\tools", @"C:\work\repo", @"C:\work\repo\vendor"], AclPlan.PathsOf(plan));
    }

    [Fact]
    public void ShouldBeEmpty_ForAnEmptyView()
        => Assert.Empty(AclPlan.Build([], []));

    [Fact]
    public void ShouldLabelAHiddenPathBelowAGrantedOne_NoAccessUp_AndAReadOnlySubPathOfAWritableOne_NoWriteUp()
    {
        var labels = AclPlan.Labels(
        [
            new ConfinedPath(@"C:\work\repo\secrets", "/workspace/secrets", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(@"C:\work\repo\.env", "/workspace/.env", ConfinedAccess.HiddenFile),
            new ConfinedPath(@"C:\work\repo\vendor", "/workspace/vendor", ConfinedAccess.ReadOnly),
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"C:\data\docs\private", "/docs/private", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(@"C:\data\docs", "/docs", ConfinedAccess.ReadOnly),
        ], []);

        Assert.Equal(
        [
            new LabelEntry(@"C:\data\docs\private", LabelPolicy.NoAccessUp),
            new LabelEntry(@"C:\work\repo\.env", LabelPolicy.NoAccessUp),
            new LabelEntry(@"C:\work\repo\secrets", LabelPolicy.NoAccessUp),
            new LabelEntry(@"C:\work\repo\vendor", LabelPolicy.NoWriteUp),
        ], labels);
    }

    [Fact]
    public void ShouldLabelAHiddenPathBelowAnExtraReadOnlyPath()
    {
        var labels = AclPlan.Labels([new ConfinedPath(@"C:	ools
ode\secrets", "/x", ConfinedAccess.HiddenDirectory)], [@"C:	ools
ode"]);

        Assert.Equal([new LabelEntry(@"C:	ools
ode\secrets", LabelPolicy.NoAccessUp)], labels);
    }

    [Fact]
    public void ShouldNotLabel_WhatNothingGrantedHolds_NorAReadOnlyMountOutsideAnyWritableOne()
    {
        var labels = AclPlan.Labels(
        [
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"C:\work\repository-secrets", "/s", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(@"C:\data\docs", "/docs", ConfinedAccess.ReadOnly),
        ], []);

        Assert.Empty(labels);
    }

    [Theory]
    [InlineData("NoWriteUp", true, "S:(ML;OICI;NW;;;ME)")]
    [InlineData("NoAccessUp", true, "S:(ML;OICI;NRNWNX;;;ME)")]
    [InlineData("NoAccessUp", false, "S:(ML;;NRNWNX;;;ME)")]
    public void ShouldWriteAMediumLabel_InheritedByADirectorysTree(string policy, bool directory, string expected)
        => Assert.Equal(expected, LabelSddl.Of(Enum.Parse<LabelPolicy>(policy), directory));

    [Theory]
    [InlineData("", "")]
    [InlineData("S:", "")]
    [InlineData("S:(ML;OICIID;NW;;;LW)", "")]
    [InlineData("S:(ML;OICI;NW;;;LW)", "S:(ML;OICI;NW;;;LW)")]
    [InlineData("S:AI(ML;;NRNWNX;;;ME)(ML;IOID;NW;;;LW)", "S:(ML;;NRNWNX;;;ME)")]
    public void ShouldKeepOnlyTheExplicitLabel_OfALabelSddl(string sddl, string expected)
        => Assert.Equal(expected, LabelSddl.ExplicitOf(sddl));
}
