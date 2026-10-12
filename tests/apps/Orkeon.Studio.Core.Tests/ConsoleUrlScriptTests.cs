using System.ComponentModel;
using System.Diagnostics;
using SysProcess = System.Diagnostics.Process;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Tests.Shared.Produced;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Studio.Core.Tests;

/// <summary>The manual script that lists the console links Studio opens.</summary>
public sealed class ConsoleUrlScriptTests
{
    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "UC-05")]
    public async System.Threading.Tasks.Task ShouldGiveAnHttpsConsoleForEveryProviderOfTheCatalogue()
    {
        var root = ProducedFile.RepositoryRoot();
        var expected = LlmPresets.ProviderCatalogFor(EnglishStudioStrings.Instance)
            .Select(c => c.KeyConsoleUri)
            .OfType<Uri>()
            .ToList();
        Assert.All(expected, uri => Assert.Equal(Uri.UriSchemeHttps, uri.Scheme));

        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(root, "scripts", "check-console-urls.py"));
        start.ArgumentList.Add("--list");

        SysProcess process;
        try
        {
            process = SysProcess.Start(start)!;
        }
        catch (Win32Exception)
        {
            Assert.Skip("python3 is not on PATH");
            return;
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(Polling.DefaultTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("check-console-urls.py --list did not end within the hang guard");
            }

            var stdout = await output;
            var stderr = await error;
            Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}: {stderr}");

            var printed = stdout.Split('\n')
                .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(t => t.StartsWith("https://", StringComparison.Ordinal)))
                .Where(token => token is not null)
                .Select(token => token!)
                .Order(StringComparer.Ordinal)
                .Distinct()
                .ToList();

            Assert.Equal(
                expected.Select(u => u.AbsoluteUri).Order(StringComparer.Ordinal).Distinct().ToList(),
                printed);
        }
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public void ShouldNotBeCalledByAnyWorkflow()
    {
        var folder = Path.Combine(ProducedFile.RepositoryRoot(), ".github", "workflows");
        var files = Directory.EnumerateFiles(folder, "*.yml")
            .Concat(Directory.EnumerateFiles(folder, "*.yaml"))
            .ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
            Assert.DoesNotContain("check-console-urls", File.ReadAllText(file), StringComparison.Ordinal);
    }
}
