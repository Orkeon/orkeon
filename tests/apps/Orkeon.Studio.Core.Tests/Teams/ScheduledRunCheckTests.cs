using Orkeon.Domain.Common;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// STUDIO-52, decisions 2 and 4: what a team's scheduled run reads against what Studio shows. The
/// launchers name the host profile of the team's setting and the declarations of its folders, and
/// the run looks both up in the settings file as saved: a profile or a folder id the file does not
/// have refuses the run, an entry changed since runs as it was saved — the key judged by the one
/// rule of STUDIO-54, never by comparing placeholders —, and a setting offered to no crew is not
/// named at all. One line per card: a refusal first, then the default, then an older version.
/// </summary>
public sealed class ScheduledRunCheckTests : IDisposable
{
    private static readonly MountId Docs = MountId.Create();

    private static readonly ModelProfile Glm = new()
    {
        Name = "GLM", Provider = "zai", BaseUrl = "https://api.z.ai/api/paas/v4",
        Model = "glm-5.2", KeyEnvName = "ZAI_API_KEY", TimeoutSeconds = 600,
    };

    private static readonly ModelProfile Docker = new()
    {
        Name = "Docker", Provider = "docker-model-runner", BaseUrl = "http://localhost:12434/engines/v1", Model = "ai/smollm2",
    };

    /// <summary>The card that names no provider: offered to no crew.</summary>
    private static readonly ModelProfile NoModel = new() { Name = "Aucun", Provider = "none" };

    /// <summary>A name without a Latin letter or digit: offered to no crew.</summary>
    private static readonly ModelProfile Unlettered = Glm with { Name = "模型" };

    private static readonly ModelProfileSet Settings =
        ModelProfileSet.Empty.Upsert(Glm).Upsert(Docker).Upsert(NoModel).Upsert(Unlettered);

    /// <summary>The folders the settings screen shows: /docs, declared under an id given at load.</summary>
    private static readonly IReadOnlyList<string> Shown = [$"{Docs}|/data/docs:/docs:ro"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-scheduled-run-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>A team on <paramref name="setting"/>, its folders read against what the screen shows.</summary>
    private TeamSummary Team(string? setting, params string[] mounts)
    {
        var team = Path.Combine(_root, "teams", "veille");
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Profile = setting,
            Schedule = "daily@07:30",
            Mounts = mounts.Length > 0 ? mounts : null,
        });
        return TeamCatalog.Describe(team, Shown);
    }

    /// <summary>The file Studio saved: each setting's entry as the mirror writes it, and the folders as given.</summary>
    private static AppSettingsDocument Saved(IReadOnlyList<string>? mounts, params ModelProfile[] settings)
    {
        var document = AppSettingsDocument.CreateEmpty();
        foreach (var setting in settings)
            document.Llm.Profiles.Set(setting.ToHostEntry(setting.HostProfileId!));
        if (mounts is { Count: > 0 })
            document.Mounts.SetRaw(mounts);
        return document;
    }

    private static AppSettingsDocument Parse(string json)
    {
        Assert.True(AppSettingsDocument.TryParse(json, out var document, out var error), error);
        return document!;
    }

    private static string Line(ScheduledRunNotice notice) => ScheduledRunCheck.Describe(notice, EnglishStudioStrings.Instance);

    [Fact]
    public void A_file_that_does_not_define_the_profile_refuses_the_run()
    {
        var team = Team("GLM");

        foreach (var saved in new AppSettingsDocument?[] { null, Saved(null, Docker) })
        {
            var notice = Assert.Single(ScheduledRunCheck.Of(team, Settings, saved));
            Assert.Equal(new ScheduledRunNotice(ScheduledRunIssue.SettingRefused, "GLM"), notice);
            Assert.Equal(
                "The next scheduled run will be refused: the saved settings file does not define “GLM” yet. Save the settings.",
                Line(notice));
        }
    }

    /// <summary>The entry as the mirror writes it, under another case of the id: the binder's comparison.</summary>
    [Fact]
    public void An_entry_equal_to_the_setting_says_nothing_whatever_the_case_of_its_id()
    {
        var saved = AppSettingsDocument.CreateEmpty();
        saved.Llm.Profiles.Set(Glm.ToHostEntry("GLM"));

        Assert.Equal("GLM", Assert.Single(saved.Llm.Profiles.Ids));
        Assert.Empty(ScheduledRunCheck.Of(Team("GLM"), Settings, saved));
        Assert.Null(ScheduledRunCheck.First(Team("GLM"), Settings, saved));
    }

    /// <summary>Another model, or an entry written before it named its key's variable: the run takes what was saved.</summary>
    [Fact]
    public void Another_model_or_no_key_variable_is_an_older_version()
    {
        var team = Team("GLM");
        var otherModel = Saved(null, Glm with { Model = "glm-4.6" });
        var noVariable = Saved(null, Glm with { KeyEnvName = null });

        foreach (var saved in new[] { otherModel, noVariable })
        {
            var notice = Assert.Single(ScheduledRunCheck.Of(team, Settings, saved));
            Assert.Equal(new ScheduledRunNotice(ScheduledRunIssue.Outdated, "GLM"), notice);
            Assert.Equal(
                "The scheduled run takes “GLM” as it was saved, not as Studio shows it. Save the settings.",
                Line(notice));
        }
    }

    /// <summary>
    /// STUDIO-54's rule, never an equality of placeholders: an entry read from the file carries none,
    /// so a Docker Model Runner entry holding its key agrees, and one without a key does not.
    /// </summary>
    [Fact]
    public void A_docker_model_runner_entry_agrees_by_its_key_alone()
    {
        var team = Team("Docker");
        var withKey = Parse("""
            {"Llm":{"Profiles":{"docker":{"BaseUrl":"http://localhost:12434/engines/v1","Model":"ai/smollm2","ApiKey":"not-needed"}}}}
            """);
        var withoutKey = Parse("""
            {"Llm":{"Profiles":{"docker":{"BaseUrl":"http://localhost:12434/engines/v1","Model":"ai/smollm2"}}}}
            """);

        Assert.Empty(ScheduledRunCheck.Of(team, Settings, withKey));
        Assert.Equal(
            new ScheduledRunNotice(ScheduledRunIssue.Outdated, "Docker"),
            Assert.Single(ScheduledRunCheck.Of(team, Settings, withoutKey)));
    }

    /// <summary>
    /// A declaration the screen gave an id at load, saved without it: the runner finds no entry
    /// carrying the id the launchers name. The line names the folder the agents see, never the disk's.
    /// </summary>
    [Fact]
    public void A_folder_id_the_file_does_not_declare_refuses_the_run_by_its_virtual_path()
    {
        var team = Team("GLM", "./output:/output:rw", $"{Docs}|/data/docs:/docs:ro");
        var unsaved = Saved(["/data/docs:/docs:ro"], Glm);
        var saved = Saved([$"{Docs}|/data/docs:/docs:ro"], Glm);

        var notice = Assert.Single(ScheduledRunCheck.Of(team, Settings, unsaved));
        Assert.Equal(new ScheduledRunNotice(ScheduledRunIssue.FolderRefused, "/docs"), notice);
        Assert.Equal(
            "The next scheduled run will be refused: the saved settings file does not declare the folder /docs yet. Save the settings.",
            Line(notice));
        Assert.DoesNotContain("/data", Line(notice), StringComparison.Ordinal);
        Assert.Empty(ScheduledRunCheck.Of(team, Settings, saved));
    }

    [Fact]
    public void A_setting_offered_to_no_crew_runs_on_the_default_and_the_line_says_why()
    {
        var unlettered = Assert.Single(ScheduledRunCheck.Of(Team("模型"), Settings, Saved(null, Glm)));
        var noModel = Assert.Single(ScheduledRunCheck.Of(Team("Aucun"), Settings, Saved(null, Glm)));

        Assert.Equal(ScheduledRunIssue.OnDefault, unlettered.Issue);
        Assert.Equal("模型", unlettered.Subject);
        Assert.Equal(HostProfileStatus.NoId, unlettered.Check!.Status);
        Assert.Equal(
            "Scheduled, this team runs on the default setting, not on “模型”. No crew can name this setting: its name keeps no Latin letter or digit.",
            Line(unlettered));
        Assert.Equal(HostProfileStatus.NoProvider, noModel.Check!.Status);
        Assert.Equal(
            "Scheduled, this team runs on the default setting, not on “Aucun”. “Aucun” has no model: from Studio, the team answers as an echo.",
            Line(noModel));
    }

    /// <summary>A setting absent from this machine: the card's label says it, the run takes the default — no notice of its own.</summary>
    [Fact]
    public void A_missing_setting_or_none_has_nothing_to_say_here()
    {
        Assert.Empty(ScheduledRunCheck.Of(Team("Claude"), Settings, null));
        Assert.Empty(ScheduledRunCheck.Of(Team(null), Settings, null));
    }

    [Fact]
    public void A_refusal_comes_first_then_the_default_then_an_older_version()
    {
        string[] mounts = [$"{Docs}|/data/docs:/docs:ro"];
        var folder = new ScheduledRunNotice(ScheduledRunIssue.FolderRefused, "/docs");

        Assert.Equal(
            [new ScheduledRunNotice(ScheduledRunIssue.SettingRefused, "GLM"), folder],
            ScheduledRunCheck.Of(Team("GLM", mounts), Settings, Saved(null)));
        Assert.Equal(
            [folder, new ScheduledRunNotice(ScheduledRunIssue.Outdated, "GLM")],
            ScheduledRunCheck.Of(Team("GLM", mounts), Settings, Saved(null, Glm with { Model = "glm-4.6" })));

        var onDefault = ScheduledRunCheck.Of(Team("模型", mounts), Settings, Saved(null));
        Assert.Equal(2, onDefault.Count);
        Assert.Equal(folder, onDefault[0]);
        Assert.Equal(ScheduledRunIssue.OnDefault, onDefault[1].Issue);
        Assert.Equal(folder, ScheduledRunCheck.First(Team("模型", mounts), Settings, Saved(null)));
    }
}
