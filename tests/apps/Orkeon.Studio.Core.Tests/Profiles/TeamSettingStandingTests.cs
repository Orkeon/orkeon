using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// STUDIO-52, decision 2: what the setting a team names is to a run outside Studio — the host
/// profile its launchers name; a setting offered to no crew, which Studio launches the team on while
/// a scheduled run takes the default; or a setting this machine does not have, the default running
/// in its place everywhere. One reading for the card, the offer, the Run screen and the launchers.
/// </summary>
public sealed class TeamSettingStandingTests
{
    private static readonly ModelProfile DeepSeek = new()
    {
        Name = "DeepSeek", Provider = "deepseek", BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-v4-flash", KeyEnvName = "DEEPSEEK_API_KEY",
    };

    /// <summary>The card that names no provider: its runs answer as an echo.</summary>
    private static readonly ModelProfile NoModel = new() { Name = "Aucun", Provider = "none" };

    /// <summary>A name that keeps no Latin letter or digit: there is no id for a crew to write.</summary>
    private static readonly ModelProfile Unlettered = new()
    {
        Name = "模型", Provider = "deepseek", BaseUrl = "https://api.deepseek.com", Model = "deepseek-v4-flash",
    };

    private static readonly ModelProfileSet Settings = ModelProfileSet.Empty.Upsert(DeepSeek).Upsert(NoModel).Upsert(Unlettered);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_team_that_names_no_setting_has_none(string? setting)
    {
        var standing = TeamSettingStanding.Of(setting, Settings);

        Assert.Equal(TeamSettingKind.None, standing.Kind);
        Assert.Null(standing.Label(EnglishStudioStrings.Instance));
    }

    [Fact]
    public void An_offered_setting_gives_the_id_its_launchers_name()
    {
        var standing = TeamSettingStanding.Of("DeepSeek", Settings);

        Assert.Equal(TeamSettingKind.Offered, standing.Kind);
        Assert.Equal("deepseek", standing.Id);
        Assert.Equal("DeepSeek", standing.Name);
        Assert.Equal("DeepSeek", standing.Label(EnglishStudioStrings.Instance));
    }

    [Fact]
    public void A_setting_offered_to_no_crew_says_why()
    {
        var noModel = TeamSettingStanding.Of("Aucun", Settings);
        var unlettered = TeamSettingStanding.Of("模型", Settings);

        Assert.Equal(TeamSettingKind.NotOffered, noModel.Kind);
        Assert.Equal(HostProfileStatus.NoProvider, noModel.Check!.Status);
        Assert.Null(noModel.Id);
        Assert.Equal(TeamSettingKind.NotOffered, unlettered.Kind);
        Assert.Equal(HostProfileStatus.NoId, unlettered.Check!.Status);
        // Studio launches the team on it: the name is shown as it is.
        Assert.Equal("模型", unlettered.Label(EnglishStudioStrings.Instance));
    }

    /// <summary>A store edited by hand: two names giving one id, and the reserved name.</summary>
    [Fact]
    public void A_store_edited_by_hand_gives_the_taken_and_the_reserved_names()
    {
        var zai = new ModelProfile { Name = "Zai", Provider = "zai", BaseUrl = "https://api.z.ai/api/paas/v4", Model = "glm-5.2" };
        var handEdited = new ModelProfileSet
        {
            Profiles = [zai, zai with { Name = "ZAI" }, DeepSeek with { Name = "Default" }],
        };

        var first = TeamSettingStanding.Of("Zai", handEdited);
        var second = TeamSettingStanding.Of("ZAI", handEdited);
        var reserved = TeamSettingStanding.Of("Default", handEdited);

        Assert.Equal(TeamSettingKind.Offered, first.Kind);
        Assert.Equal("zai", first.Id);
        Assert.Equal(TeamSettingKind.NotOffered, second.Kind);
        Assert.Equal(HostProfileStatus.TakenBySetting, second.Check!.Status);
        Assert.Equal("Zai", second.Check.TakenBy);
        Assert.Equal(TeamSettingKind.NotOffered, reserved.Kind);
        Assert.Equal(HostProfileStatus.DefaultName, reserved.Check!.Status);
    }

    /// <summary>
    /// A setting removed, renamed by hand, or named by a team from another machine: the default runs
    /// in its place — and the name is matched as every launch matches it, ordinal.
    /// </summary>
    [Theory]
    [InlineData("Claude")]
    [InlineData("deepseek")]
    public void A_setting_this_machine_does_not_have_is_missing(string setting)
    {
        var standing = TeamSettingStanding.Of(setting, Settings);

        Assert.Equal(TeamSettingKind.Missing, standing.Kind);
        Assert.Null(standing.Id);
        Assert.Null(standing.Check);
        Assert.Equal(
            $"{setting} — absent from this machine: the default setting runs in its place",
            standing.Label(EnglishStudioStrings.Instance));
    }
}
