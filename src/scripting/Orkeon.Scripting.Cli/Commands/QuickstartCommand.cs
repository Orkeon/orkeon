using Orkeon.Hosting;
namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// <c>orkeon quickstart [--to &lt;dir&gt;]</c> — writes the two-minute crew of the README
/// (<c>crew.yaml</c> and its <c>appsettings.json</c>, from <c>examples/quickstart/</c>) and an
/// empty <c>out/</c> folder into <c>./orkeon-quickstart</c> by default.
/// </summary>
/// <remarks>
/// The README's first block used to start from a clone of the repository: a reader who only
/// installed the dotnet tool had no crew to run. Both files are embedded in the tool, byte for
/// byte, so the block needs nothing but the tool. The verb never overwrites: a file, or a folder
/// that is not empty, at the target is refused, so a second run cannot clobber an edited crew.
/// </remarks>
internal static class QuickstartCommand
{
    private const string DefaultDirectory = "orkeon-quickstart";
    private const string OutputDirectory = "out";
    private const string ResourcePrefix = "quickstart/";

    /// <summary>The files written, each embedded as <c>quickstart/&lt;name&gt;</c>.</summary>
    private static readonly string[] Files = ["crew.yaml", "appsettings.json"];

    /// <summary>Dispatches <c>orkeon quickstart …</c> (arguments already stripped of the verb).</summary>
    public static Task<int> DispatchAsync(IRunnerConsole console, string[] args) => DispatchAsync(console, args, Directory.GetCurrentDirectory());

    /// <summary>Test seam: the same dispatch, anchored at <paramref name="workingDirectory"/>.</summary>
    internal static async Task<int> DispatchAsync(IRunnerConsole console, string[] args, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(args);

        var arguments = Parse(args);
        if (arguments.Help)
        {
            await console.Output.WriteAsync(Usage).ConfigureAwait(false);
            return Program.ExitOk;
        }

        if (arguments.Error is { } error)
            return await RefuseAsync(console.ErrorOutput, error).ConfigureAwait(false);

        var target = Path.GetFullPath(arguments.Directory ?? DefaultDirectory, workingDirectory);
        if (File.Exists(target))
            return await RefuseAsync(console.ErrorOutput, $"'{target}' is a file; choose another folder with --to.").ConfigureAwait(false);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            return await RefuseAsync(console.ErrorOutput, $"'{target}' is not empty; nothing was written. Choose another folder with --to.").ConfigureAwait(false);

        Directory.CreateDirectory(Path.Combine(target, OutputDirectory));
        foreach (var file in Files)
            await WriteAsync(file, Path.Combine(target, file)).ConfigureAwait(false);

        var relative = Path.GetRelativePath(workingDirectory, target);
        await console.Output.WriteLineAsync($"Wrote {string.Join(", ", Files)} and an empty {OutputDirectory}/ to {target}").ConfigureAwait(false);
        await console.Output.WriteLineAsync($"From {relative}, run the crew with {OutputDirectory}/ writable:").ConfigureAwait(false);
        await console.Output.WriteLineAsync($"  orkeon run crew.yaml --mount ./{OutputDirectory}:/output:rw").ConfigureAwait(false);
        await console.Output.WriteLineAsync("then read-only, and watch the write refused:").ConfigureAwait(false);
        await console.Output.WriteLineAsync($"  orkeon run crew.yaml --mount ./{OutputDirectory}:/output:ro").ConfigureAwait(false);
        return Program.ExitOk;
    }

    /// <summary>The arguments of the verb: <c>--help</c>, else the target directory, else what was refused.</summary>
    private sealed record QuickstartArguments(bool Help = false, string? Directory = null, string? Error = null);

    private static QuickstartArguments Parse(string[] args)
    {
        string? directory = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    return new QuickstartArguments(Help: true);
                case "--to" when i + 1 < args.Length:
                    directory = args[++i];
                    break;
                case "--to":
                    return new QuickstartArguments(Error: "--to needs a directory.");
                case var attached when attached.StartsWith("--to=", StringComparison.Ordinal):
                    if (attached.Length == "--to=".Length)
                        return new QuickstartArguments(Error: "--to needs a directory.");
                    directory = attached["--to=".Length..];
                    break;
                case var option when option.StartsWith('-'):
                    return new QuickstartArguments(Error: $"unknown argument '{option}'; run `orkeon quickstart --help`.");
                default:
                    // The argument rule of every command: a word no option takes is refused as such.
                    return new QuickstartArguments(Error: $"unexpected argument '{args[i]}'; run `orkeon quickstart --help`.");
            }
        }

        return new QuickstartArguments(Directory: directory);
    }

    private const string Usage =
        """
        Usage: orkeon quickstart [--to <dir>]

        Writes the two-minute crew of the README, ready to run on a local model:
          crew.yaml          one agent, one file_write tool, one task
          appsettings.json   Ollama on localhost:11434, qwen2.5:1.5b
          out/               the folder the run mounts as /output

          --to <dir>   where to write them (default: ./orkeon-quickstart);
                       a file or a folder that is not empty is refused

        """;

    private static async Task<int> RefuseAsync(TextWriter errorOutput, string error)
    {
        await errorOutput.WriteLineAsync("orkeon quickstart: " + error).ConfigureAwait(false);
        return Program.ExitScriptError;
    }

    private static async Task WriteAsync(string file, string path)
    {
        // The logical name is written with '/', but a build may hand it back with '\'.
        var assembly = typeof(QuickstartCommand).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => string.Equals(name.Replace('\\', '/'), ResourcePrefix + file, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The quickstart resource '{ResourcePrefix}{file}' is missing from {assembly.GetName().Name}.");

        using var source = assembly.GetManifestResourceStream(resource)!;
        using var destination = File.Create(path);
        await source.CopyToAsync(destination).ConfigureAwait(false);
    }
}
