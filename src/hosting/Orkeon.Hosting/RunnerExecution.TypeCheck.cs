using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Hosting;

/// <summary>
/// The type check of a <c>.ork.ts</c> script (FWK-12): <c>tsc</c> against the typings this build
/// ships, run by <c>--validate</c> before the crew loads, and before a run under
/// <c>TypeCheck=always</c>. The class-level VFS-compliance suppression lives on the primary partial.
/// </summary>
public static partial class RunnerExecution
{
    /// <summary>
    /// Type-checks the script of <paramref name="bootstrap"/> when the configured
    /// <see cref="TypeCheckMode"/> asks for it. Prints its own diagnostics.
    /// </summary>
    /// <param name="host">The built host (its <c>Orkeon:Scripting:Toolchain</c> options).</param>
    /// <param name="bootstrap">The resolved target.</param>
    /// <param name="console">Where the diagnostics go: stderr.</param>
    /// <param name="validating">Whether this is <c>--validate</c> (else a run, which only <c>always</c> type-checks).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns><see langword="true"/> to go on, <see langword="false"/> when the script must not load or run.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fault barrier: a type checker that cannot run is reported as such, never an unhandled crash of --validate.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    internal static async Task<bool> TypeCheckScriptAsync(
        IHost host, HostBootstrap bootstrap, IRunnerConsole console, bool validating, CancellationToken ct)
    {
        if (bootstrap.IsCrewDirectory || !IsScriptedCrewDefinition(bootstrap.ConfigPath))
            return true;

        var options = host.Services.GetRequiredService<IOptions<ScriptingToolchainOptions>>();
        var mode = options.Value.TypeCheck;
        if (mode == TypeCheckMode.Off || (!validating && mode != TypeCheckMode.Always))
            return true;

        // A .ork.js fixture is already JavaScript: there is nothing for tsc to check.
        if (!bootstrap.ConfigPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            return true;

        var failure = validating ? "VALIDATION FAILED" : "ERROR";
        var checker = new TscTypeChecker(options);
        try
        {
            var diagnostics = await checker.CheckAsync(
                Path.GetFullPath(bootstrap.ConfigPath), bootstrap.VirtualConfigPath, ct).ConfigureAwait(false);
            if (diagnostics.Count == 0)
                return true;

            await console.ErrorOutput.WriteLineAsync($"{failure}: type errors").ConfigureAwait(false);
            foreach (var diagnostic in diagnostics)
                await console.ErrorOutput.WriteLineAsync(diagnostic.ToString()).ConfigureAwait(false);
            return false;
        }
        catch (TscNotFoundException ex)
        {
            if (mode == TypeCheckMode.Always)
            {
                await console.ErrorOutput.WriteLineAsync($"{failure}: type check unavailable").ConfigureAwait(false);
                await console.ErrorOutput.WriteLineAsync(ex.Message).ConfigureAwait(false);
                return false;
            }

            await console.ErrorOutput.WriteLineAsync($"WARNING: type check skipped: {ex.Message}").ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The checker words its failures ("type check failed (exit 3)") and redacts them.
            await console.ErrorOutput.WriteLineAsync($"{failure}: {ex.Message}").ConfigureAwait(false);
            return false;
        }
    }
}
