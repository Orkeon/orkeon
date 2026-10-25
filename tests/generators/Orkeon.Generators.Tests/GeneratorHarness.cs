using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Orkeon.Generators.Tests;

/// <summary>
/// Compiles a source against the real reference assemblies and the marker attributes of
/// <c>Orkeon.Domain/SharedKernel/CodeGen/TypedDictionaryAttributes.cs</c>, runs
/// <see cref="TypedDictionaryGenerator"/> on it, and returns what it produced together with the
/// compilation that includes the generated files.
/// </summary>
internal static class GeneratorHarness
{
    /// <summary>The marker attributes, as Orkeon.Domain declares them (same metadata names).</summary>
    public const string Attributes = """
        namespace Orkeon.Generators
        {
            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
            internal sealed class TypedDictionaryAttribute : System.Attribute
            {
                public TypedDictionaryAttribute(System.Type valueType) { ValueType = valueType; }
                public System.Type ValueType { get; }
                public bool CacheEmpty { get; set; }
                public bool EmitEmpty { get; set; } = true;
                public bool EmitBuilderFrom { get; set; }
                public bool EmitGenericAdd { get; set; } = true;
            }

            [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
            internal sealed class DictionaryEntryAttribute : System.Attribute
            {
                public DictionaryEntryAttribute() { }
                public DictionaryEntryAttribute(string key) { Key = key; }
                public string? Key { get; }
                public string Factory { get; set; } = "From";
            }
        }
        """;

    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES not available.");

        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0 && File.Exists(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    }

    /// <summary>Runs the generator on <paramref name="source"/> (the attributes are added).</summary>
    public static GeneratorRun Run(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            [
                CSharpSyntaxTree.ParseText(Attributes, parseOptions),
                CSharpSyntaxTree.ParseText(source, parseOptions),
            ],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new TypedDictionaryGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var result = driver.GetRunResult().Results.Single();
        if (result.Exception is not null)
            throw new InvalidOperationException("The generator threw.", result.Exception);

        return new GeneratorRun(
            output,
            [.. result.GeneratedSources.Select(s => new GeneratedSource(s.HintName, s.SourceText.ToString()))],
            generatorDiagnostics,
            [.. output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)]);
    }
}

/// <summary>One file the generator added.</summary>
internal sealed record GeneratedSource(string HintName, string Text);

/// <summary>What one generator run produced.</summary>
internal sealed record GeneratorRun(
    Compilation Output,
    ImmutableArray<GeneratedSource> Sources,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationErrors)
{
    /// <summary>The single generated file; fails when there is not exactly one.</summary>
    public GeneratedSource Single => Assert.Single(Sources);

    /// <summary>Asserts that the code plus the generated files compile without error.</summary>
    public void AssertCompiles() =>
        Assert.True(CompilationErrors.IsEmpty, string.Join("\n", CompilationErrors.Select(d => d.ToString())));

    /// <summary>Emits the compilation, loads it in a collectible context and returns <paramref name="typeName"/>.</summary>
    public Type Load(string typeName)
    {
        using var image = new MemoryStream();
        var emitted = Output.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Select(d => d.ToString())));

        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext(nameof(GeneratorRun), isCollectible: true);
        return context.LoadFromStream(image).GetType(typeName, throwOnError: true)!;
    }
}
