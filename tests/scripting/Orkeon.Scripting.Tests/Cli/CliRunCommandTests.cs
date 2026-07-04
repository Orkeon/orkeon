using System.Diagnostics;

namespace Orkeon.Scripting.Tests.Cli;

public sealed class CliRunCommandTests
{
    private static string CliProjectPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
        "src", "scripting", "Orkeon.Scripting.Cli", "Orkeon.Scripting.Cli.csproj"));

    // The test assembly runs from bin/<Config>/<TFM>/, so run the CLI with that SAME
    // configuration. `dotnet run --no-build` otherwise defaults to Debug, which made CI
    // fail once it built Release only (the Debug binary simply did not exist).
    private static string Configuration =>
        new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static (int exitCode, string stdout, string stderr) Run(params string[] cliArgs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(CliProjectPath);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(Configuration);
        psi.ArgumentList.Add("--no-build");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("run");
        foreach (var a in cliArgs) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return (proc.ExitCode, stdout, stderr);
    }

    [Fact]
    public void Cli_run_simple_script_returns_exit_0_and_emits_result_JSON()
    {
        var script = FixturePath("cli-hello.ork.ts");
        if (!File.Exists(script))
            return;

        var (exit, stdout, stderr) = Run(script);

        Assert.True(exit == 0, $"exit={exit}; stderr=\n{stderr}\nstdout=\n{stdout}");
        Assert.Contains("hello-from-cli", stdout);
    }

    [Fact]
    public void Cli_run_invalid_file_returns_exit_1()
    {
        var (exit, _, stderr) = Run("/no/such/file.ork.ts");

        Assert.Equal(1, exit);
        Assert.Contains("script not found", stderr);
    }
}
