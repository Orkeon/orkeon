using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// CH-10 - a mount the run would refuse at access is refused when the host is built: outside the
/// path validator's whitelist without the external-mounts opt-in, or malformed. A mount a flag, the
/// variable, an allowed directory or a settings entry in force covers is accepted. Joins
/// <see cref="ConsoleSerialCollection"/> because one case sets a process-wide variable and the
/// host writes to the process-global console streams.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class MountsReachableAtStartTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-mount-reach-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private string Settings(string mounts = "", string allowed = "")
    {
        var path = Path.Combine(_root, "appsettings.json");
        var pathSecurity = allowed.Length == 0
            ? ""
            : ", \"PathSecurity\": { \"AdditionalAllowedDirectories\": [ " + System.Text.Json.JsonSerializer.Serialize(allowed) + " ] }";
        var fileSystem = mounts.Length == 0
            ? ""
            : ", \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ " + System.Text.Json.JsonSerializer.Serialize(mounts) + " ] } }";
        File.WriteAllText(path, "{ \"RaggableTree\": { \"Enabled\": false }" + pathSecurity + fileSystem + " }");
        return path;
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void ShouldRefuseAtStart_WhenAMountIsOutsideTheWhitelistWithoutTheFlag()
    {
        var external = Folder("external");
        var plan = new RunnerMountPlan { CliMounts = [$"{external}:/data:ro"] };

        var refusal = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(Settings(), plan));

        Assert.Contains("/data", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--allow-external-mounts", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(RunnerEnvironment.AllowExternalMountsVariable, refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-10")]
    [InlineData("flag")]
    [InlineData("variable")]
    [InlineData("allowed-directory")]
    public void ShouldAcceptAtStart_WhenTheFlagTheVariableOrAnAllowedDirectoryCoversTheMount(string cover)
    {
        var external = Folder("external");
        var settings = Settings(allowed: cover == "allowed-directory" ? external : "");
        var previous = Environment.GetEnvironmentVariable(RunnerEnvironment.AllowExternalMountsVariable);
        if (cover == "variable")
            Environment.SetEnvironmentVariable(RunnerEnvironment.AllowExternalMountsVariable, "1");
        try
        {
            var plan = new RunnerMountPlan
            {
                CliMounts = [$"{external}:/data:ro"],
                AllowExternalMounts = cover == "flag" || RunnerEnvironment.AllowExternalMounts,
            };

            using var host = RunnerHost.Build(settings, plan);

            Assert.NotNull(host);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerEnvironment.AllowExternalMountsVariable, previous);
        }
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void ShouldRefuseAtStartWithTheParserMessage_WhenAMountIsMalformed()
    {
        var plan = new RunnerMountPlan { CliMounts = ["\"/a\"b\":/x:ro"], AllowExternalMounts = true };

        var refusal = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(Settings(), plan));

        Assert.Contains("\"/a\"b\"", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void ShouldAcceptAtStart_WhenASettingsMountChosenByIdIsOutsideTheWorkingDirectory()
    {
        var external = Folder("declared");
        var id = MountId.Create();
        var plan = new RunnerMountPlan { SelectedMountIds = [id] };

        using var host = RunnerHost.Build(Settings(mounts: $"{id}|{external}:/data:ro"), plan);

        Assert.NotNull(host);
    }

    private sealed class ScriptRunOptions : RunnerOptionsBase;

    [Fact]
    [Trait("BR", "CH-11")]
    public async Task ShouldReadItsFolder_WhenADeclarativeScriptRunsFromElsewhere()
    {
        var scriptDir = Folder("script");
        var script = Path.Combine(scriptDir, "crew.ork.ts");
        await File.WriteAllTextAsync(script, "/// <reference orkeon-script=\"1.0\" />\nconst a = agentBuilder().name(\"A\").role(\"A\").goal(\"g\").body((input, ctx) => \"ok\").build();\nconst t = taskBuilder().name(\"t\").description(\"d\").expectedOutput(\"o\").agent(a).build();\nglobalThis.crew = crewBuilder().name(\"outside\").withAgent(a).withTask(t).build();", TestContext.Current.CancellationToken);
        var opts = new ScriptRunOptions { ConfigPath = script, SettingsPath = Settings(), Validate = true };
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        Console.SetError(captured);
        int exitCode;
        try
        {
            exitCode = await RunnerExecution.RunOneShotAsync(opts, "test", externalCt: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var output = captured.ToString();
        Assert.True(output.Contains("VALIDATION OK", StringComparison.Ordinal), output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    [Trait("BR", "CH-11")]
    public void ShouldReadTheFirstLevel_WhenAWhitelistedFolderIsALink()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "symbolic links are created on Linux only");
        var target = Folder("target");
        File.WriteAllText(Path.Combine(target, "a.txt"), "x");
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, target);
        var plan = new RunnerMountPlan { CliMounts = [$"{link}:/data:ro"], AllowExternalMounts = true };

        using var host = RunnerHost.Build(Settings(), plan);

        var validator = host.Services.GetRequiredService<IPathValidator>();
        Assert.True(validator.ValidatePath(Path.Combine(link, "a.txt")).IsAllowed);
    }
}
