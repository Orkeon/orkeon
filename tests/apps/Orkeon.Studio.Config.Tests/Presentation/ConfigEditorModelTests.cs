using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Tests.Presentation;

public sealed class ConfigEditorModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("studio-config-editor").FullName;
    private bool _disposed;

    private string PathIn(string fileName) => Path.Combine(_directory, fileName);

    private static ConfigEditorModel CreateModel(
        FakeDirectoryProbe? directories = null,
        FakeProcessLauncher? launcher = null,
        FakeExecutableProbe? executables = null)
    {
        var probe = executables ?? new FakeExecutableProbe().WithFile(Path.Combine("/", "opt", "orkeon", "orkeon"));
        var runner = new OrkeonProcessRunner(
            launcher ?? new FakeProcessLauncher(),
            new OrkeonBinaryLocator(probe, ["orkeon"]));

        return new ConfigEditorModel(
            directories ?? new FakeDirectoryProbe("/data/in", "/data/out"),
            runner,
            new FakeLlmEndpointProbe());
    }

    [Fact]
    public void An_empty_mount_list_is_advice_while_editing_and_an_error_when_saving()
    {
        // An empty folder list is a warning while editing and when saving alike (STUDIO-57):
        // the runner mounts nothing and runs the crew all the same, and a team's own folders
        // travel with the team. Writing the file used to be blocked here.
        var model = CreateModel();

        var editing = model.Preflight().Messages;
        var saving = model.Preflight(ValidationScope.Saving).Messages;

        Assert.Contains(
            editing,
            message => message.Code == ValidationCodes.MountsEmpty
                && message.Severity == ValidationSeverity.Warning);
        Assert.Contains(
            saving,
            message => message.Code == ValidationCodes.MountsEmpty
                && message.Severity == ValidationSeverity.Warning);
        Assert.False(model.Preflight().HasBlockingErrors);
        Assert.False(model.Preflight(ValidationScope.Saving).HasBlockingErrors);
    }

    [Fact]
    public async Task Keys_studio_does_not_model_survive_an_edit_and_a_save()
    {
        var path = PathIn("appsettings.json");
        await File.WriteAllTextAsync(path, """
            {
              "Llm": { "Model": "old-model" },
              "Serilog": { "MinimumLevel": "Debug", "WriteTo": [ { "Name": "Console" } ] },
              "_comment": "kept by hand"
            }
            """, TestContext.Current.CancellationToken);

        var model = CreateModel();
        Assert.Null(await model.OpenAsync(path, TestContext.Current.CancellationToken));

        model.Llm.Model = "new-model";
        Assert.Empty(model.ApplyForms());
        Assert.Null(await model.SaveAsync(path, TestContext.Current.CancellationToken));

        var reopened = CreateModel();
        Assert.Null(await reopened.OpenAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal("new-model", reopened.Document.Llm.Model);
        Assert.Equal("Debug", reopened.Document.GetString("Serilog:MinimumLevel"));
        Assert.Equal("kept by hand", reopened.Document.GetString("_comment"));
    }

    [Fact]
    public async Task An_openai_preset_saved_to_a_file_reloads_as_a_valid_document()
    {
        // Spec §12.1: create an `openai` preset, save it, and have it accepted afterwards.
        var model = CreateModel();
        Assert.True(LlmPresets.TryCreatePlan(LlmPresets.OpenAI, overrides: null, out var plan, out _));
        Assert.NotNull(plan);

        var guidance = model.ApplyPreset(plan);
        Assert.Contains(guidance, line => line.Contains(LlmPresets.DefaultApiKeyEnv));

        model.Mounts.Add(new MountDefinition
        {
            PhysicalPath = "/data/in",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadOnly,
        });

        var preflight = model.Preflight();
        Assert.False(preflight.HasBlockingErrors);

        var path = PathIn("appsettings.json");
        Assert.Null(await model.SaveAsync(path, TestContext.Current.CancellationToken));

        var reopened = CreateModel();
        Assert.Null(await reopened.OpenAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal(LlmPresets.OpenAI, reopened.Llm.DetectedProvider);
        Assert.Equal("", reopened.Llm.ApiKey);
        Assert.DoesNotContain(reopened.Preflight().Messages, message => message.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task A_file_holding_integers_is_saved_and_reopens_with_them()
    {
        // STUDIO-53: each preflight writes the forms' integers again through SetInt32, and the
        // validator read those nodes as « not a number » — a file holding a token budget, a
        // timeout or a rate limit could not be saved at all.
        var path = PathIn("appsettings.json");
        await File.WriteAllTextAsync(path, """
            {
              "Llm": { "Model": "qwen3", "BaseUrl": "http://localhost:11434", "MaxTokens": 4096, "TimeoutSeconds": 600 },
              "RateLimiting": { "MaxConcurrentRequests": 1, "QueueLimit": 32 },
              "Orkeon": { "FileSystem": { "Mounts": [ "/data/in:/workspace:ro" ] } }
            }
            """, TestContext.Current.CancellationToken);
        var model = CreateModel();
        Assert.Null(await model.OpenAsync(path, TestContext.Current.CancellationToken));

        var preflight = model.Preflight(ValidationScope.Saving);

        Assert.DoesNotContain(preflight.Messages, message => message.Code == ValidationCodes.InvalidFieldType);
        Assert.False(preflight.HasBlockingErrors);
        Assert.Null(await model.SaveAsync(path, TestContext.Current.CancellationToken));
        var reopened = CreateModel();
        Assert.Null(await reopened.OpenAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(600, reopened.Document.Llm.TimeoutSeconds);
        Assert.Equal(32, reopened.Document.RateLimiting.QueueLimit);
    }

    [Fact]
    public async Task A_ro_and_a_rw_mount_survive_the_round_trip_and_parse_at_boot()
    {
        // Spec §12.3: what the mount editor writes is what FileSystemMount.Parse reads.
        var model = CreateModel();
        model.Mounts.Add(new MountDefinition { PhysicalPath = "/data/in", VirtualPath = "/workspace", Rights = MountRights.ReadOnly });
        model.Mounts.Add(new MountDefinition { PhysicalPath = "/data/out", VirtualPath = "/output", Rights = MountRights.ReadWrite });

        var path = PathIn("appsettings.json");
        Assert.Empty(model.ApplyForms());
        Assert.Null(await model.SaveAsync(path, TestContext.Current.CancellationToken));

        var reopened = CreateModel();
        Assert.Null(await reopened.OpenAsync(path, TestContext.Current.CancellationToken));

        var mounts = reopened.Document.Mounts.RawEntries.Select(FileSystemMount.Parse).ToList();
        Assert.Equal(2, mounts.Count);
        Assert.Equal(FileAccessRights.ReadOnly, mounts[0].DefaultRights);
        Assert.Equal(FileAccessRights.ReadWrite, mounts[1].DefaultRights);
    }

    [Fact]
    public void An_empty_document_warns_about_the_missing_llm_section()
    {
        var preflight = CreateModel().Preflight();

        Assert.Contains(preflight.Messages, message => message.Code == ValidationCodes.LlmSectionMissing);
        Assert.True(preflight.HasWarnings);
        Assert.False(preflight.HasBlockingErrors);
        Assert.Contains(preflight.Lines, line => line.Contains("WIN-01"));
    }

    [Fact]
    public void A_field_that_cannot_be_read_back_blocks_the_save()
    {
        var model = CreateModel();
        model.Llm.MaxTokens = "plenty";

        var preflight = model.Preflight();

        Assert.True(preflight.HasBlockingErrors);
        Assert.Contains(preflight.Lines, line => line.Contains("Llm:MaxTokens"));
    }

    [Fact]
    public void A_mount_whose_folder_is_missing_blocks_the_save()
    {
        var model = CreateModel(new FakeDirectoryProbe());
        model.Mounts.Add(new MountDefinition { PhysicalPath = "/data/missing", VirtualPath = "/workspace" });

        var preflight = model.Preflight();

        Assert.True(preflight.HasBlockingErrors);
        Assert.Contains(preflight.Messages, message => message.Code == ValidationCodes.MountPathMissing);
    }

    [Fact]
    public void Applying_a_preset_touches_only_the_llm_section()
    {
        var model = CreateModel();
        model.Mounts.Add(new MountDefinition { PhysicalPath = "/data/in", VirtualPath = "/workspace" });
        model.RateLimiting.QueueLimit = "42";

        Assert.True(LlmPresets.TryCreatePlan(LlmPresets.Ollama, overrides: null, out var plan, out _));
        Assert.NotNull(plan);
        model.ApplyPreset(plan);

        Assert.Equal("ollama", model.Llm.DetectedProvider);
        Assert.Equal("42", model.RateLimiting.QueueLimit);
        Assert.Single(model.Mounts.RawEntries);
    }

    [Fact]
    public async Task The_no_model_preset_clears_the_default_and_keeps_the_named_profiles()
    {
        // STUDIO-48: « None » removed the whole Llm section — the named profiles with it, those
        // written by hand included. It clears the default provider only.
        var path = PathIn("appsettings.json");
        await File.WriteAllTextAsync(path, """
            { "Llm": { "Model": "qwen3", "Profiles": { "claude": { "Model": "claude-sonnet-5" } } } }
            """, TestContext.Current.CancellationToken);
        var model = CreateModel();
        Assert.Null(await model.OpenAsync(path, TestContext.Current.CancellationToken));
        Assert.True(LlmPresets.TryCreatePlan(LlmPresets.None, overrides: null, out var plan, out _));

        model.ApplyPreset(plan!);

        Assert.Equal("", model.Llm.Model);
        Assert.Equal(["claude"], model.Document.Llm.Profiles.Ids);
        Assert.Equal(["profile: claude — claude-sonnet-5"], model.Llm.Profiles);
        // The default is gone, so the WIN-01 warning says so — the profiles do not make one.
        Assert.Contains(model.Preflight().Messages, message => message.Code == ValidationCodes.LlmSectionMissing);
    }

    [Fact]
    public async Task Opening_a_file_that_does_not_exist_is_reported_as_a_message()
    {
        var model = CreateModel();

        var error = await model.OpenAsync(PathIn("nope.json"), TestContext.Current.CancellationToken);

        Assert.NotNull(error);
        Assert.Null(model.CurrentPath);
    }

    [Fact]
    public async Task Opening_a_malformed_file_is_reported_as_a_message()
    {
        var path = PathIn("broken.json");
        await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);

        var error = await CreateModel().OpenAsync(path, TestContext.Current.CancellationToken);

        Assert.NotNull(error);
    }

    [Fact]
    public void A_new_document_forgets_the_previous_file()
    {
        var model = CreateModel();
        model.Llm.Model = "m";
        model.ApplyForms();

        model.NewDocument();

        Assert.Null(model.CurrentPath);
        Assert.Equal("", model.Llm.Model);
        Assert.Equal("{}" + Environment.NewLine, model.RawJson);
    }

    [Fact]
    public void The_raw_view_shows_the_unknown_keys_too()
    {
        var model = CreateModel();
        model.Document.SetString("Serilog:MinimumLevel", "Debug");

        Assert.Contains("Serilog", model.RawJson);
    }

    [Fact]
    public async Task The_diagnostic_reads_the_checks_out_of_the_cli_output()
    {
        var launcher = new FakeProcessLauncher()
            .WithStandardOutput("""[{"check":"llm","status":"ok","detail":"reachable"}]""");

        var report = await CreateModel(launcher: launcher).RunDoctorAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(["doctor", "--json"], request.Arguments);

        var check = Assert.Single(report.Checks);
        Assert.Equal("llm", check.Check);
        Assert.Equal(DoctorStatus.Ok, check.Status);
        Assert.False(report.HasFailures);
    }

    [Fact]
    public async Task A_missing_cli_is_reported_with_an_actionable_message_rather_than_an_exception()
    {
        var model = CreateModel(executables: new FakeExecutableProbe());

        var location = model.LocateBinary();
        Assert.False(location.Found);
        Assert.NotNull(location.Error);
        Assert.NotEmpty(location.ProbedPaths);

        var report = await model.RunDoctorAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RunOutcome.NotStarted, report.Run.Outcome);
        Assert.Empty(report.Checks);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }
}
