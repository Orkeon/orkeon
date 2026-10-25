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
    [Trait("BR", "GH-188")]
    public void ShouldGrantModifyOnTheGitDirsOnly_UnderAReadOnlyCommonDir()
    {
        // A linked worktree's binds: the common dir read-only, what a commit writes read-write over it,
        // then overrides narrowed to read-only on hooks (an admin's widening) and on refs/heads.
        ConfinedPath[] paths =
        [
            new(@"C:\repo\.git", "/workspace/.git", ConfinedAccess.ReadOnly),
            new(@"C:\repo\.git\objects", "/workspace/.git/objects", ConfinedAccess.ReadWrite),
            new(@"C:\repo\.git\refs", "/workspace/.git/refs", ConfinedAccess.ReadWrite),
            new(@"C:\repo\.git\worktrees\p1", "/workspace/.git/worktrees/p1", ConfinedAccess.ReadWrite),
            new(@"C:\repo\.git\hooks", "/workspace/.git/hooks", ConfinedAccess.ReadOnly),
            new(@"C:\repo\.git\refs\heads", "/workspace/.git/refs/heads", ConfinedAccess.ReadOnly),
        ];

        var plan = AclPlan.Build(paths, []);
        var isolations = AclPlan.Isolations(paths, []);

        Assert.Equal(
        [
            new AclEntry(@"C:\repo\.git", AclRights.ReadAndExecute, Deny: false),
            new AclEntry(@"C:\repo\.git\hooks", AclRights.ReadAndExecute, Deny: false),
            new AclEntry(@"C:\repo\.git\objects", AclRights.Modify, Deny: false),
            new AclEntry(@"C:\repo\.git\refs", AclRights.Modify, Deny: false),
            new AclEntry(@"C:\repo\.git\refs\heads", AclRights.ReadAndExecute, Deny: false),
            new AclEntry(@"C:\repo\.git\refs\heads", AclRights.Write, Deny: true),
            new AclEntry(@"C:\repo\.git\worktrees\p1", AclRights.Modify, Deny: false),
        ], plan);
        // Nothing grants Modify above the common dir: only the ref narrowed under refs is cut off.
        Assert.Equal([@"C:\repo\.git\refs\heads"], isolations);
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
    public void ShouldIsolateAHiddenPathBelowAGrantedOne_AndAReadOnlySubPathOfAWritableOne_ParentsFirst()
    {
        var isolations = AclPlan.Isolations(
        [
            new ConfinedPath(@"C:\work\repo\secrets", "/workspace/secrets", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(@"C:\work\repo\.env", "/workspace/.env", ConfinedAccess.HiddenFile),
            new ConfinedPath(@"C:\work\repo\vendor", "/workspace/vendor", ConfinedAccess.ReadOnly),
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"C:\data\docs\private", "/docs/private", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(@"C:\data\docs", "/docs", ConfinedAccess.ReadOnly),
        ], []);

        Assert.Equal([@"C:\data\docs\private", @"C:\work\repo\.env", @"C:\work\repo\secrets", @"C:\work\repo\vendor"], isolations);
    }

    [Fact]
    public void ShouldIsolateAHiddenPathBelowAnExtraReadOnlyPath()
    {
        var isolations = AclPlan.Isolations([new ConfinedPath(@"C:\tools\node\secrets", "/x", ConfinedAccess.HiddenDirectory)], [@"C:\tools\node"]);

        Assert.Equal([@"C:\tools\node\secrets"], isolations);
    }

    [Fact]
    public void ShouldNotIsolate_WhatNothingGrantedHolds_NorAReadOnlyMountOutsideAnyWritableOne()
    {
        var isolations = AclPlan.Isolations(
        [
            new ConfinedPath(@"C:\work\repo", "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(@"C:\work\repository-secrets", "/s", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(@"C:\data\docs", "/docs", ConfinedAccess.ReadOnly),
        ], []);

        Assert.Empty(isolations);
    }
}
