namespace Orkeon.Hosting.Tests;

/// <summary>
/// Contract tests for the pure helpers of <see cref="RunnerExecution"/> that the
/// runners and the `orkeon` tool both rely on (moved from examples/runners/_shared
/// to src/hosting by R1.5).
/// </summary>
public class RunnerExecutionContractTests
{
    // --- DetectOutputMountPath -------------------------------------------------

    [Fact]
    public void DetectOutputMountPath_returns_virtual_path_of_writable_output_mount()
    {
        var mounts = new[] { "/data/in:/input:ro", "/data/out:/output:rw" };
        Assert.Equal("/output", RunnerExecution.DetectOutputMountPath(mounts));
    }

    [Fact]
    public void DetectOutputMountPath_trims_trailing_slash()
    {
        var mounts = new[] { "/data/out:/output/:rw" };
        Assert.Equal("/output", RunnerExecution.DetectOutputMountPath(mounts));
    }

    [Fact]
    public void DetectOutputMountPath_ignores_readonly_output_mount()
    {
        var mounts = new[] { "/data/out:/output:ro" };
        Assert.Null(RunnerExecution.DetectOutputMountPath(mounts));
    }

    [Fact]
    public void DetectOutputMountPath_ignores_writable_non_output_mount()
    {
        var mounts = new[] { "/data/tmp:/scratch:rw" };
        Assert.Null(RunnerExecution.DetectOutputMountPath(mounts));
    }

    [Fact]
    public void DetectOutputMountPath_skips_malformed_mount_strings()
    {
        var mounts = new[] { "not-a-mount", "/data/out:/output:rw" };
        Assert.Equal("/output", RunnerExecution.DetectOutputMountPath(mounts));
    }

    [Fact]
    public void DetectOutputMountPath_returns_null_when_no_mounts()
    {
        Assert.Null(RunnerExecution.DetectOutputMountPath([]));
    }

    // --- IsScriptedCrewDefinition ----------------------------------------------

    [Theory]
    [InlineData("crew.ork.ts")]
    [InlineData("crew.ORK.TS")]
    [InlineData("/abs/path/crew.ork.js")]
    public void IsScriptedCrewDefinition_true_for_ork_scripts(string path)
    {
        Assert.True(RunnerExecution.IsScriptedCrewDefinition(path));
    }

    [Theory]
    [InlineData("crew.yaml")]
    [InlineData("crew.yml")]
    [InlineData("crew.ts")]
    [InlineData("crew.ork")]
    public void IsScriptedCrewDefinition_false_for_non_ork_paths(string path)
    {
        Assert.False(RunnerExecution.IsScriptedCrewDefinition(path));
    }

    [Fact]
    public void IsScriptedCrewDefinition_throws_on_empty_path()
    {
        Assert.ThrowsAny<ArgumentException>(() => RunnerExecution.IsScriptedCrewDefinition(" "));
    }
}
