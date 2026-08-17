namespace Orkeon.Cli.Commands.Scripting.Tests.Typings;

/// <summary>
/// Verifies that <c>Typings/orkeon-cli.d.ts</c> is shipped as an embedded resource of
/// <c>Orkeon.Cli.Commands.Scripting.dll</c> — the Phase 2 deliverable that backs Phase 5's
/// "publish to disk at first start" feature.
/// </summary>
public sealed class EmbeddedTypingsTests
{
    [Fact]
    public void Typings_dts_is_embedded_in_assembly()
    {
        var asm = typeof(Orkeon.Cli.Commands.Scripting.Loading.ScriptCommandLoader).Assembly;
        var names = asm.GetManifestResourceNames();
        var resource = names.SingleOrDefault(n => n.EndsWith("orkeon-cli.d.ts", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(resource), $"orkeon-cli.d.ts not found. Resources: {string.Join(", ", names)}");

        using var stream = asm.GetManifestResourceStream(resource!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        Assert.Contains("function defineCommand", content);
        Assert.Contains("interface CommandRuntimeContext", content);
    }
}
