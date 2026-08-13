using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// The argument list handed to the <c>orkeon</c> process, checked string by string against
/// what a user would type, and the refusal of options that belong to the other dialect.
/// </summary>
public sealed class RunArgumentsBuilderTests
{
    private static readonly string[] BareYamlArguments = ["run", "/crews/crew.yaml"];

    private static readonly string[] ScriptDirectoryArguments = ["run", "/crews/ts/crew.ork.ts"];

    private static readonly string[] MultiFileArguments = ["run", "/crews/my_crew"];

    private static readonly string[] FullYamlArguments =
    [
        "run", "/crews/crew.yaml",
        "--settings", "/etc/orkeon/appsettings.json",
        "-V", "TOPIC=quantum computing",
        "-V", "DEPTH=3",
        "--initial-context", "Focus on 2026 papers",
        "--mount", "/srv/data:/workspace:ro",
        "--mount", "/srv/out:/output:rw",
        "--allow-external-mounts",
        "--verbose", "2",
        "--llm-log",
        "--llm-log-path", "/var/log/orkeon",
        "--validate",
    ];

    private static readonly string[] FullScriptArguments =
    [
        "run", "/crews/crew.ork.ts",
        "--settings", "/etc/orkeon/appsettings.json",
        "--inputs", "{\"topic\":\"rag\"}",
        "--verbose", "1",
    ];

    private static readonly string[] InputsFileArguments =
        ["run", "/crews/crew.ork.ts", "--inputs-file", "/crews/inputs.json"];

    private static readonly string[] EmptyVariableArguments =
        ["run", "/crews/crew.yaml", "-V", "TOPIC="];

    private static readonly string[] BothInputChannelsArguments =
    [
        "run", "/crews/crew.ork.ts",
        "--inputs", "{}",
        "--inputs-file", "/crews/inputs.json",
    ];

    private static readonly string[] LlmLogPathArguments =
        ["run", "/crews/crew.yaml", "--llm-log-path", "/var/log/orkeon"];

    private static RunTarget YamlTarget(string path = "/crews/crew.yaml") => new()
    {
        Kind = RunTargetKind.YamlFile,
        SelectedPath = path,
        RunPath = path,
    };

    private static RunTarget ScriptTarget(string path = "/crews/crew.ork.ts") => new()
    {
        Kind = RunTargetKind.ScriptFile,
        SelectedPath = path,
        RunPath = path,
    };

    private static RunTarget MultiFileTarget(string path = "/crews/my_crew") => new()
    {
        Kind = RunTargetKind.MultiFileCrewDirectory,
        SelectedPath = path,
        RunPath = path,
        Markers = [path + "/agents"],
    };

    [Fact]
    public void A_bare_launch_is_just_the_verb_and_the_path()
    {
        Assert.Equal(BareYamlArguments, RunArgumentsBuilder.Build(YamlTarget()));
    }

    [Fact]
    public void A_script_directory_target_runs_the_resolved_file_not_the_directory()
    {
        var target = new RunTarget
        {
            Kind = RunTargetKind.ScriptDirectory,
            SelectedPath = "/crews/ts",
            RunPath = "/crews/ts/crew.ork.ts",
        };

        Assert.Equal(ScriptDirectoryArguments, RunArgumentsBuilder.Build(target));
    }

    [Fact]
    public void A_multi_file_directory_target_runs_the_directory_itself()
    {
        Assert.Equal(MultiFileArguments, RunArgumentsBuilder.Build(MultiFileTarget()));
    }

    [Fact]
    public void A_full_yaml_launch_maps_every_option_in_typing_order()
    {
        var options = new RunLaunchOptions
        {
            SettingsPath = "/etc/orkeon/appsettings.json",
            Variables = [new RunVariable("TOPIC", "quantum computing"), new RunVariable("DEPTH", "3")],
            InitialContext = "Focus on 2026 papers",
            Mounts = ["/srv/data:/workspace:ro", "/srv/out:/output:rw"],
            AllowExternalMounts = true,
            Verbosity = 2,
            LlmLogEnabled = true,
            LlmLogPath = "/var/log/orkeon",
            Validate = true,
        };

        Assert.Equal(FullYamlArguments, RunArgumentsBuilder.Build(YamlTarget(), options));
    }

    [Fact]
    public void A_full_script_launch_maps_the_script_only_options()
    {
        var options = new RunLaunchOptions
        {
            SettingsPath = "/etc/orkeon/appsettings.json",
            InputsJson = "{\"topic\":\"rag\"}",
            Verbosity = 1,
        };

        Assert.Equal(FullScriptArguments, RunArgumentsBuilder.Build(ScriptTarget(), options));
    }

    [Fact]
    public void An_inputs_file_is_passed_as_its_own_option()
    {
        var arguments = RunArgumentsBuilder.Build(
            ScriptTarget(),
            new RunLaunchOptions { InputsFilePath = "/crews/inputs.json" });

        Assert.Equal(InputsFileArguments, arguments);
    }

    [Fact]
    public void The_default_verbosity_emits_no_argument()
    {
        var arguments = RunArgumentsBuilder.Build(YamlTarget(), new RunLaunchOptions { Verbosity = 0 });

        Assert.DoesNotContain("--verbose", arguments);
    }

    [Fact]
    public void An_empty_settings_path_means_auto_and_emits_no_argument()
    {
        var arguments = RunArgumentsBuilder.Build(YamlTarget(), new RunLaunchOptions { SettingsPath = "   " });

        Assert.DoesNotContain("--settings", arguments);
    }

    [Fact]
    public void Yaml_options_are_refused_on_a_script_target()
    {
        var options = new RunLaunchOptions
        {
            Variables = [new RunVariable("TOPIC", "rag")],
            InitialContext = "context",
        };

        var messages = RunArgumentsBuilder.Validate(ScriptTarget(), options);

        Assert.Equal(2, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.Equal(LaunchCodes.OptionNotApplicable, message.Code);
            Assert.Equal(ValidationSeverity.Error, message.Severity);
        });
        Assert.Contains(messages, message => message.Path == "-V");
        Assert.Contains(messages, message => message.Path == "--initial-context");

        var failure = Assert.Throws<InvalidOperationException>(() => RunArgumentsBuilder.Build(ScriptTarget(), options));
        Assert.Contains("-V", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_options_are_refused_on_a_yaml_target()
    {
        var options = new RunLaunchOptions
        {
            InputsJson = "{}",
            InputsFilePath = "/crews/inputs.json",
        };

        var messages = RunArgumentsBuilder.Validate(YamlTarget(), options);

        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, message => message.Path == "--inputs");
        Assert.Contains(messages, message => message.Path == "--inputs-file");

        Assert.Throws<InvalidOperationException>(() => RunArgumentsBuilder.Build(YamlTarget(), options));
    }

    [Fact]
    public void Script_options_are_refused_on_a_multi_file_directory_which_is_a_yaml_target()
    {
        var messages = RunArgumentsBuilder.Validate(
            MultiFileTarget(),
            new RunLaunchOptions { InputsJson = "{}" });

        Assert.Contains(messages, message => message.Code == LaunchCodes.OptionNotApplicable);
    }

    [Fact]
    public void A_multi_file_directory_states_the_cli_version_directory_dispatch_needs()
    {
        var notice = Assert.Single(RunArgumentsBuilder.Validate(MultiFileTarget()));

        Assert.Equal(LaunchCodes.DirectoryRunNotice, notice.Code);
        Assert.Equal(ValidationSeverity.Information, notice.Severity);
        Assert.Equal(RunTargetRequirements.DirectoryRunNotice, notice.Text);
        Assert.Contains(RunTargetRequirements.MinimumCliVersion, notice.Text, StringComparison.Ordinal);

        // The capability shipped, so nothing about a directory target blocks the launch.
        Assert.Equal(MultiFileArguments, RunArgumentsBuilder.Build(MultiFileTarget()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_verbosity_outside_the_cli_range_is_refused(int verbosity)
    {
        var options = new RunLaunchOptions { Verbosity = verbosity };

        Assert.Equal(
            LaunchCodes.InvalidVerbosity,
            Assert.Single(RunArgumentsBuilder.Validate(YamlTarget(), options)).Code);
        Assert.Throws<InvalidOperationException>(() => RunArgumentsBuilder.Build(YamlTarget(), options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOPIC=EXTRA")]
    public void A_variable_without_a_usable_name_is_refused(string key)
    {
        var options = new RunLaunchOptions { Variables = [new RunVariable(key, "value")] };

        Assert.Equal(
            LaunchCodes.InvalidVariable,
            Assert.Single(RunArgumentsBuilder.Validate(YamlTarget(), options)).Code);
    }

    [Fact]
    public void A_variable_with_an_empty_value_is_accepted()
    {
        var options = new RunLaunchOptions { Variables = [new RunVariable("TOPIC", string.Empty)] };

        Assert.Empty(RunArgumentsBuilder.Validate(YamlTarget(), options));
        Assert.Equal(EmptyVariableArguments, RunArgumentsBuilder.Build(YamlTarget(), options));
    }

    [Fact]
    public void An_empty_mount_entry_is_refused_and_points_at_its_configuration_key()
    {
        var options = new RunLaunchOptions { Mounts = ["/srv/data:/workspace:ro", "  "] };

        var message = Assert.Single(RunArgumentsBuilder.Validate(YamlTarget(), options));

        Assert.Equal(LaunchCodes.EmptyMount, message.Code);
        // The second --mount, but the THIRD configuration key: the runner writes its own
        // auto-injected mount at index 0 before appending the user's.
        Assert.Equal("Orkeon:FileSystem:Mounts:2", message.Path);
    }

    [Fact]
    public void Setting_both_input_channels_is_a_warning_not_a_refusal()
    {
        var options = new RunLaunchOptions { InputsJson = "{}", InputsFilePath = "/crews/inputs.json" };

        var message = Assert.Single(RunArgumentsBuilder.Validate(ScriptTarget(), options));

        Assert.Equal(LaunchCodes.ConflictingInputs, message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
        Assert.Equal(BothInputChannelsArguments, RunArgumentsBuilder.Build(ScriptTarget(), options));
    }

    [Fact]
    public void A_custom_log_directory_alone_implies_logging_and_emits_only_its_option()
    {
        var arguments = RunArgumentsBuilder.Build(
            YamlTarget(),
            new RunLaunchOptions { LlmLogPath = "/var/log/orkeon" });

        Assert.Equal(LlmLogPathArguments, arguments);
    }

    [Fact]
    public void The_display_command_line_quotes_what_a_shell_would_need_quoted()
    {
        var options = new RunLaunchOptions
        {
            Variables = [new RunVariable("TOPIC", "quantum computing")],
            Verbosity = 1,
        };

        var commandLine = RunArgumentsBuilder.ToDisplayCommandLine(
            YamlTarget(),
            options,
            style: CommandLineQuotingStyle.Posix);

        Assert.Equal(
            "orkeon run /crews/crew.yaml -V 'TOPIC=quantum computing' --verbose 1",
            commandLine);
    }

    [Fact]
    public void The_builder_rejects_a_null_target()
    {
        Assert.Throws<ArgumentNullException>(() => RunArgumentsBuilder.Build(null!));
        Assert.Throws<ArgumentNullException>(() => RunArgumentsBuilder.Validate(null!));
    }
}
