using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orkeon.Compliance.Vfs.Tests;

/// <summary>
/// Minimal self-contained harness: compile source against the real reference assemblies,
/// run the analyzer, return the diagnostics it reports.
/// </summary>
internal static class AnalyzerHarness<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(trustedPlatformAssemblies))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES not available.");

        return trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
    }

    public static async Task<ImmutableArray<Diagnostic>> RunAsync(string source, string? fileName = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            path: fileName ?? "/workspace/src/core/Orkeon.Domain/Fake.cs");

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilerDiagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (compilerDiagnostics.Length > 0)
        {
            var msg = string.Join("\n", compilerDiagnostics.Select(d => d.ToString()));
            throw new InvalidOperationException("Test source has compiler errors:\n" + msg);
        }

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        var filtered = diagnostics.Where(d => d.Id.StartsWith("ORKVFS", StringComparison.Ordinal))
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();

        if (diagnostics.Length > 0 && filtered.Length == 0)
        {
            // surface any unexpected analyzer errors for debugging
            var msg = string.Join("\n", diagnostics.Select(d => $"[{d.Id} {d.Severity}] {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"));
            throw new InvalidOperationException("Analyzer emitted non-ORK diagnostics:\n" + msg);
        }

        return filtered;
    }
}
