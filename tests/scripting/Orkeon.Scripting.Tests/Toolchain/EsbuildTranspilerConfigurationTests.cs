using Microsoft.Extensions.Configuration;
using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

/// <summary>
/// GAP-13: the <c>Orkeon:Scripting:Toolchain</c> section reaches the transpiler. Nothing bound
/// it before: <c>EsbuildPath</c> was the first place the resolution chain looked, and the error
/// message cited the key, but every host built the transpiler without options.
/// </summary>
public sealed class EsbuildTranspilerConfigurationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orkeon-esbuild-cfg-" + Guid.NewGuid().ToString("N"));

    public EsbuildTranspilerConfigurationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [Fact]
    public void Create_resolves_the_EsbuildPath_set_in_configuration()
    {
        // ResolveBinary only checks that the file exists; it never runs it.
        var fake = Path.Combine(_dir, OperatingSystem.IsWindows() ? "esbuild.exe" : "esbuild");
        File.WriteAllText(fake, "not a real binary");
        var configuration = Configure(("Orkeon:Scripting:Toolchain:EsbuildPath", fake));

        using var transpiler = EsbuildTranspiler.Create(configuration);

        Assert.Equal(fake, transpiler.ResolveBinary());
    }

    [Fact]
    public void FromConfiguration_binds_EsbuildTimeout()
    {
        var configuration = Configure(("Orkeon:Scripting:Toolchain:EsbuildTimeout", "00:02:30"));

        var options = ScriptingToolchainOptions.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromSeconds(150), options.EsbuildTimeout);
        Assert.Null(options.EsbuildPath);
    }

    [Fact]
    public void FromConfiguration_without_the_section_keeps_the_defaults()
    {
        Assert.Equal(new ScriptingToolchainOptions(), ScriptingToolchainOptions.FromConfiguration(Configure()));
        Assert.Equal(new ScriptingToolchainOptions(), ScriptingToolchainOptions.FromConfiguration(null));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Off, Always")]
    [InlineData("sk-secret-example")]
    public void FromConfiguration_refuses_a_TypeCheck_that_is_not_a_name(string value)
    {
        // #464: the configuration binder read "1" as Validate and a comma list as a combination.
        var configuration = Configure(("Orkeon:Scripting:Toolchain:TypeCheck", value));

        var refused = Assert.Throws<InvalidOperationException>(() => ScriptingToolchainOptions.FromConfiguration(configuration));

        Assert.Equal("Orkeon:Scripting:Toolchain:TypeCheck is not a type-check mode: write one of Off, Validate, Always.", refused.Message);
        Assert.DoesNotContain(value, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_reads_TypeCheck_by_name_in_any_case()
    {
        var configuration = Configure(("Orkeon:Scripting:Toolchain:TypeCheck", " always "));

        Assert.Equal(TypeCheckMode.Always, ScriptingToolchainOptions.FromConfiguration(configuration).TypeCheck);
    }

    private static IConfiguration Configure(params (string Key, string Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();
}
