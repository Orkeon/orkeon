using System.Reflection;
using Orkeon.Constants.FileSystem;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// <c>orkeon typings [--out &lt;dir&gt;]</c> — writes the editor typings this build of the tool
/// carries: <c>orkeon.d.ts</c> (the <c>.ork.ts</c> DSL) and <c>orkeon-cli.d.ts</c> (the
/// <c>*.cmd.ts</c> scripted commands), into <c>./.orkeon/</c> by default (GAP-13).
/// </summary>
/// <remarks>
/// A developer who installed the tool with <c>dotnet tool install</c> had no typings at all:
/// they were built inside the repository and shipped nowhere. Both files are embedded in the
/// tool and always overwritten, so running the verb again after an update refreshes them to
/// the runtime that will execute the scripts.
/// </remarks>
internal static class TypingsCommand
{
    private const string DslFileName = "orkeon.d.ts";
    private const string CommandsFileName = "orkeon-cli.d.ts";

    /// <summary>The DSL typings: the roll-up of <c>Orkeon.Scripting/Typings/*.d.ts</c>, embedded by its build.</summary>
    private const string DslResource = "Orkeon.Scripting.Typings.orkeon.d.ts";

    /// <summary>The command typings, embedded in this tool from <c>Orkeon.Cli.Commands.Scripting/Typings/</c>.</summary>
    private const string CommandsResource = "typings/orkeon-cli.d.ts";

    /// <summary>Dispatches <c>orkeon typings …</c> (arguments already stripped of the verb).</summary>
    public static Task<int> DispatchAsync(string[] args) => DispatchAsync(args, Directory.GetCurrentDirectory());

    /// <summary>Test seam: the same dispatch, anchored at <paramref name="workingDirectory"/>.</summary>
    internal static async Task<int> DispatchAsync(string[] args, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? outDirectory = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    await Console.Out.WriteAsync(Usage).ConfigureAwait(false);
                    return Program.ExitOk;
                case "--out" or "-o" when i + 1 < args.Length:
                    outDirectory = args[++i];
                    break;
                case "--out" or "-o":
                    await Console.Error.WriteLineAsync("orkeon typings: --out needs a directory.").ConfigureAwait(false);
                    return Program.ExitScriptError;
                default:
                    await Console.Error.WriteLineAsync(
                        $"orkeon typings: unknown argument '{args[i]}'; run `orkeon typings --help`.").ConfigureAwait(false);
                    return Program.ExitScriptError;
            }
        }

        var target = Path.GetFullPath(outDirectory ?? ConventionalNames.StateDirectory, workingDirectory);
        Directory.CreateDirectory(target);

        await WriteAsync(typeof(ScriptHost).Assembly, DslResource, Path.Combine(target, DslFileName)).ConfigureAwait(false);
        await WriteAsync(typeof(TypingsCommand).Assembly, CommandsResource, Path.Combine(target, CommandsFileName)).ConfigureAwait(false);

        var relative = Path.GetRelativePath(workingDirectory, target).Replace('\\', '/');
        await Console.Out.WriteLineAsync($"Wrote {DslFileName} and {CommandsFileName} to {target}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Reference them at the top of a script:").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  /// <reference path=\"./{relative}/{DslFileName}\" />       (a .ork.ts crew)").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  /// <reference path=\"./{relative}/{CommandsFileName}\" />   (a .cmd.ts command)").ConfigureAwait(false);
        return Program.ExitOk;
    }

    private const string Usage =
        """
        Usage: orkeon typings [--out <dir>]

        Writes the TypeScript typings of this build for your editor:
          orkeon.d.ts       the .ork.ts scripting DSL
          orkeon-cli.d.ts   the .cmd.ts scripted commands

          -o, --out <dir>   where to write them (default: ./.orkeon)

        """;

    private static async Task WriteAsync(Assembly assembly, string resource, string path)
    {
        using var source = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The typings resource '{resource}' is missing from {assembly.GetName().Name}.");
        using var destination = File.Create(path);
        await source.CopyToAsync(destination).ConfigureAwait(false);
    }
}
