using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Run.Launcher;
using Orkeon.Studio.Run.Tests.Doubles;

namespace Orkeon.Studio.Run.Tests.Launcher;

/// <summary>
/// The options form: which fields a target leaves usable, what the form drops when the
/// target changes dialect, and the mount list a launch adds on top of its appsettings.
/// </summary>
public class LaunchOptionsModelTests
{
    private static RunTarget YamlTarget() => new()
    {
        Kind = RunTargetKind.YamlFile,
        SelectedPath = "/crews/demo/crew.yaml",
        RunPath = "/crews/demo/crew.yaml",
    };

    private static RunTarget ScriptTarget() => new()
    {
        Kind = RunTargetKind.ScriptFile,
        SelectedPath = "/crews/demo/crew.ork.ts",
        RunPath = "/crews/demo/crew.ork.ts",
    };

    [Fact]
    public void Yaml_target_enables_the_yaml_options_and_disables_the_script_ones()
    {
        var target = YamlTarget();

        Assert.True(LaunchOptionsModel.IsAvailable(target, RunOption.Variables));
        Assert.True(LaunchOptionsModel.IsAvailable(target, RunOption.InitialContext));
        Assert.False(LaunchOptionsModel.IsAvailable(target, RunOption.Inputs));
        Assert.False(LaunchOptionsModel.IsAvailable(target, RunOption.InputsFile));
    }

    [Fact]
    public void Script_target_enables_the_script_options_and_disables_the_yaml_ones()
    {
        var target = ScriptTarget();

        Assert.True(LaunchOptionsModel.IsAvailable(target, RunOption.Inputs));
        Assert.True(LaunchOptionsModel.IsAvailable(target, RunOption.InputsFile));
        Assert.False(LaunchOptionsModel.IsAvailable(target, RunOption.Variables));
        Assert.False(LaunchOptionsModel.IsAvailable(target, RunOption.InitialContext));
    }

    [Fact]
    public void Shared_options_are_available_to_both_dialects()
    {
        foreach (var option in new[] { RunOption.Settings, RunOption.Mounts, RunOption.Verbose, RunOption.Validate })
        {
            Assert.True(LaunchOptionsModel.IsAvailable(YamlTarget(), option));
            Assert.True(LaunchOptionsModel.IsAvailable(ScriptTarget(), option));
        }
    }

    [Fact]
    public void No_target_leaves_no_option_available()
    {
        Assert.Empty(LaunchOptionsModel.AvailableOptions(null));
        Assert.False(LaunchOptionsModel.IsAvailable(null, RunOption.Verbose));
    }

    [Fact]
    public void Options_of_the_other_dialect_are_dropped_rather_than_emitted()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()));
        model.AddVariable("TOPIC", "quantum computing");
        model.InitialContext = "start here";
        model.InputsJson = """{"topic":"x"}""";

        var forScript = model.ToLaunchOptions(ScriptTarget());

        Assert.Empty(forScript.Variables);
        Assert.Null(forScript.InitialContext);
        Assert.Equal("""{"topic":"x"}""", forScript.InputsJson);

        // …and the form keeps what was typed, so switching back restores it.
        var forYaml = model.ToLaunchOptions(YamlTarget());
        Assert.Single(forYaml.Variables);
        Assert.Equal("start here", forYaml.InitialContext);
        Assert.Null(forYaml.InputsJson);
    }

    [Fact]
    public void Automatic_settings_emit_no_path()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()))
        {
            UseAutomaticSettings = true,
            ExplicitSettingsPath = "/etc/orkeon/appsettings.json",
        };

        Assert.Null(model.EffectiveSettingsPath);
        Assert.Null(model.ToLaunchOptions(YamlTarget()).SettingsPath);
    }

    [Fact]
    public void Pinned_settings_emit_the_path()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()))
        {
            UseAutomaticSettings = false,
            ExplicitSettingsPath = "/etc/orkeon/appsettings.json",
        };

        Assert.Equal("/etc/orkeon/appsettings.json", model.ToLaunchOptions(YamlTarget()).SettingsPath);
    }

    [Fact]
    public void Resolution_chain_is_the_four_steps_the_runtime_walks()
    {
        var chain = LaunchOptionsModel.DescribeResolutionChain();

        Assert.Equal(4, chain.Count);
        Assert.StartsWith("1.", chain[0], StringComparison.Ordinal);
        Assert.Contains("--settings", chain[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Variables_are_read_one_per_line()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()));

        var messages = model.SetVariablesFromText("TOPIC=quantum computing\nDEPTH=3\n");

        Assert.Empty(messages);
        Assert.Equal(2, model.Variables.Count);
        Assert.Equal("quantum computing", model.Variables[0].Value);
        Assert.Equal("DEPTH=3", model.Variables[1].ToToken());
    }

    [Fact]
    public void A_line_without_a_separator_is_reported_and_skipped()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()));

        var messages = model.SetVariablesFromText("TOPIC=ok\nnonsense\n");

        var message = Assert.Single(messages);
        Assert.Equal(LaunchCodes.InvalidVariable, message.Code);
        Assert.Single(model.Variables);
    }

    [Fact]
    public void Variables_round_trip_through_the_form_text()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()));
        model.SetVariablesFromText("A=1\nB=2");

        Assert.Equal($"A=1{Environment.NewLine}B=2", model.VariablesAsText());
    }

    [Fact]
    public void Launch_mounts_become_mount_strings()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe("/data")));
        model.AddMount(new MountDefinition
        {
            PhysicalPath = "/data",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadWrite,
        });

        Assert.Equal(["/data:/workspace:rw"], model.MountStrings);
        Assert.Empty(model.ValidateMounts());
    }

    [Fact]
    public void A_launch_mount_on_a_missing_folder_is_an_error()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()));
        model.AddMount(new MountDefinition { PhysicalPath = "/absent", VirtualPath = "/workspace" });

        Assert.Contains(model.ValidateMounts(), message => message.Code == "STUDIO-MOUNT-PATH");
    }

    [Fact]
    public void An_empty_launch_mount_list_is_not_an_error()
    {
        // Unlike the appsettings editor: the launcher adds mounts on top of a file that
        // already declares its own, so declaring none is the ordinary case.
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe()));

        Assert.Empty(model.ValidateMounts());
    }

    [Fact]
    public void Launch_mounts_replace_the_settings_mounts_of_the_same_index()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe("/data")));
        model.AddMount(new MountDefinition { PhysicalPath = "/data", VirtualPath = "/workspace" });

        var effective = model.ComputeEffectiveMounts(["/old:/workspace:ro", "/out:/output:rw"]);

        Assert.Equal(2, effective.Count);
        Assert.Equal(MountOrigin.CommandLine, effective[0].Origin);
        Assert.True(effective[0].OverridesSettings);
        Assert.Equal("/old:/workspace:ro", effective[0].ReplacedSettingsMount);
        Assert.Equal(MountOrigin.Settings, effective[1].Origin);
    }

    [Fact]
    public void The_override_rule_is_stated_for_the_ui()
    {
        Assert.Contains("replaces", LaunchOptionsModel.MountOverrideExplanation, StringComparison.Ordinal);
        Assert.Contains("not merged", LaunchOptionsModel.MountOverrideExplanation, StringComparison.Ordinal);
        Assert.Contains("--allow-external-mounts", LaunchOptionsModel.ExternalMountsExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Mount_editing_replaces_and_removes_by_index()
    {
        var model = new LaunchOptionsModel(new MountValidator(new FakeDirectoryProbe("/a", "/b")));
        model.AddMount(new MountDefinition { PhysicalPath = "/a", VirtualPath = "/workspace" });
        model.AddMount(new MountDefinition { PhysicalPath = "/b", VirtualPath = "/output" });

        model.ReplaceMountAt(0, new MountDefinition
        {
            PhysicalPath = "/b",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadWriteNoDelete,
        });
        model.RemoveMountAt(1);

        Assert.Equal(["/b:/workspace:rwnd"], model.MountStrings);
    }
}
