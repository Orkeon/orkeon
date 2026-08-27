using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// The shipped <c>.d.ts</c> files are handed to esbuild, the same TypeScript front end that
/// reads every user script.
/// <para>
/// The suite next door asserts that certain substrings appear in the rolled-up bundle. That
/// cannot fail when the typings stop being TypeScript — and one of them had not been for a
/// while: <c>tools.d.ts</c> declared an index signature as <c>const [name: string]: …</c>,
/// which a namespace cannot carry and which is not a declaration at all. The file did not
/// parse, so the <c>tools</c> namespace this package advertises was unavailable to every
/// editor that loaded it, with a green test suite and every substring present.
/// </para>
/// </summary>
public sealed class TypingsParseTests
{
    public static TheoryData<string> TypingFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));

        return data;
    }

    [Theory]
    [MemberData(nameof(TypingFiles))]
    public async Task Every_shipped_typing_is_valid_typescript(string fileName)
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(TypingsDirectory(), fileName), TestContext.Current.CancellationToken);

        using var transpiler = new EsbuildTranspiler();

        try
        {
            // The output is discarded: a .d.ts strips to nothing. Parsing is the assertion.
            await transpiler.TranspileAsync(source, TestContext.Current.CancellationToken);
        }
        catch (EsbuildNotFoundException)
        {
            Assert.Skip("esbuild is not available in this environment.");
        }
        catch (EsbuildTranspileException ex)
        {
            Assert.Fail($"{fileName} is not valid TypeScript:\n{ex.Message}");
        }
    }

    [Fact]
    public void The_typings_directory_is_not_empty()
    {
        // Guards the guard: an empty theory source passes silently.
        Assert.NotEmpty(Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts"));
    }

    private static string TypingsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "scripting", "Orkeon.Scripting", "Typings");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate src/scripting/Orkeon.Scripting/Typings above {AppContext.BaseDirectory}.");
    }
}
