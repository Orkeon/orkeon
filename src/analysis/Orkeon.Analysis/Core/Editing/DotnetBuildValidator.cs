using System.Diagnostics;
using System.Text;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Analysis.Core.Editing;

/// <summary>Runs <c>dotnet build --no-restore</c> on the nearest project or solution above the edited files.</summary>
internal sealed class DotnetBuildValidator : IBuildValidator
{
    private const int DetailErrors = 20;
    private readonly TimeSpan _timeout;

    public DotnetBuildValidator(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public async Task<BuildValidation> ValidateAsync(IReadOnlyList<string> editedPhysicalPaths, CancellationToken cancellationToken)
    {
        var targets = editedPhysicalPaths.Select(FindTarget).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (targets.Count == 0)
            return new BuildValidation { Outcome = BuildOutcome.Skipped, Detail = "No .csproj or .sln above the edited files." };

        foreach (var target in targets)
        {
            var result = await BuildAsync(target, cancellationToken).ConfigureAwait(false);
            if (result.Outcome != BuildOutcome.Green) return result;
        }

        return new BuildValidation { Outcome = BuildOutcome.Green };
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probing the host filesystem for the project that owns an edited file; not subject to VFS policy")]
    private static string? FindTarget(string physicalPath)
    {
        for (var dir = Path.GetDirectoryName(physicalPath); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (!Directory.Exists(dir)) continue;
            var found = Directory.EnumerateFiles(dir, "*.csproj").Concat(Directory.EnumerateFiles(dir, "*.sln")).FirstOrDefault();
            if (found is not null) return found;
        }

        return null;
    }

    private async Task<BuildValidation> BuildAsync(string target, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(target)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "build", target, "--no-restore", "-nodeReuse:false", "-p:UseSharedCompilation=false" })
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet process.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested) throw;
            return new BuildValidation { Outcome = BuildOutcome.Red, Detail = $"Build timed out after {_timeout}." };
        }

        return process.ExitCode == 0
            ? new BuildValidation { Outcome = BuildOutcome.Green }
            : new BuildValidation { Outcome = BuildOutcome.Red, Detail = Errors(output) };
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }

    private static string Errors(StringBuilder output)
    {
        lock (output)
        {
            var errors = output.ToString().Split('\n')
                .Where(l => l.Contains(": error ", StringComparison.Ordinal))
                .Select(l => l.Trim())
                .Distinct();
            return string.Join("; ", errors.Take(DetailErrors));
        }
    }
}
