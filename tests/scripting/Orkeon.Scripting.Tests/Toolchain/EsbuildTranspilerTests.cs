using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

public sealed class EsbuildTranspilerTests
{
    private static EsbuildTranspiler RequireTranspiler()
    {
        var t = EsbuildTestEnvironment.TryCreate();
        Assert.SkipWhen(t is null, "esbuild binary not available in this test environment.");
        return t!;
    }

    [Fact]
    public async Task TranspileAsync_strips_TypeScript_and_returns_valid_JS()
    {
        using var transpiler = RequireTranspiler();

        var ts = "const greet = (name: string): string => `hello ${name}`;\n";

        var js = await transpiler.TranspileAsync(ts, CancellationToken.None);

        Assert.Contains("const greet", js);
        Assert.Contains("hello", js);
        Assert.DoesNotContain(": string", js);
    }

    [Fact]
    public async Task TranspileAsync_throws_EsbuildTranspileException_on_syntax_error()
    {
        using var transpiler = RequireTranspiler();

        var invalid = "const x = (";

        var ex = await Assert.ThrowsAsync<EsbuildTranspileException>(
            () => transpiler.TranspileAsync(invalid, CancellationToken.None));
        Assert.NotEqual(0, ex.ExitCode);
    }

    [Fact]
    public async Task TranspileAsync_strips_interface_declarations_entirely()
    {
        using var transpiler = RequireTranspiler();

        var ts = "interface Foo { x: number }\nconst y = 1;\n";

        var js = await transpiler.TranspileAsync(ts, CancellationToken.None);

        Assert.DoesNotContain("interface", js);
        Assert.DoesNotContain("Foo", js);
        Assert.Contains("const y = 1", js);
    }
}
