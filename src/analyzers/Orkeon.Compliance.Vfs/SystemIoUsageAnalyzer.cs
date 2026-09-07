using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orkeon.Compliance.Vfs;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemIoUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            DiagnosticDescriptors.DirectFileUsage,
            DiagnosticDescriptors.DirectDirectoryUsage,
            DiagnosticDescriptors.DirectFileSystemTypeInstantiation,
            DiagnosticDescriptors.SuspiciousPathGetFullPath,
            DiagnosticDescriptors.DirectFileSystemWatcher,
            DiagnosticDescriptors.DirectStreamReaderWriterPath,
            DiagnosticDescriptors.NullableFileSystemService);

    private const string SuppressAttributeMetadataName =
        "Orkeon.Compliance.Vfs.SuppressVfsComplianceAttribute";

    public override void Initialize(AnalysisContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeObjectCreation,
            SyntaxKind.ObjectCreationExpression,
            // `FileStream fs = new(path, ...)` is the same instantiation as
            // `new FileStream(path, ...)`; only the syntax node differs.
            SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeNullableFileSystemService, SyntaxKind.FieldDeclaration, SyntaxKind.Parameter);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (IsExemptByPath(context.Node.SyntaxTree.FilePath))
            return;

        var invocation = (InvocationExpressionSyntax)context.Node;

        // The call is judged on its RESOLVED SYMBOL, not on its syntax. Requiring a
        // MemberAccessExpressionSyntax meant `using static System.IO.File; ReadAllText(p)`
        // -- a bare identifier invocation -- reached the same System.IO.File.ReadAllText
        // without the analyzer ever looking at it. The location reported is the name being
        // invoked, whichever shape carried it.
        var nameLocation = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.GetLocation(),
            _ => invocation.Expression.GetLocation(),
        };

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        var containingType = method.ContainingType;
        if (containingType is null)
            return;

        var ns = containingType.ContainingNamespace?.ToDisplayString();
        if (ns != "System.IO")
            return;

        if (IsSuppressed(context.ContainingSymbol))
            return;

        var typeName = containingType.Name;
        var methodName = method.Name;

        switch (typeName)
        {
            case "File":
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DirectFileUsage,
                    nameLocation,
                    methodName));
                break;
            case "Directory":
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DirectDirectoryUsage,
                    nameLocation,
                    methodName));
                break;
            case "Path" when methodName == "GetFullPath":
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.SuspiciousPathGetFullPath,
                    nameLocation));
                break;
        }
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (IsExemptByPath(context.Node.SyntaxTree.FilePath))
            return;

        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken);
        var type = typeInfo.Type;
        if (type is null)
            return;

        // An implicit `new(...)` has no type syntax to point at; the whole expression is
        // the smallest thing that names the type to a reader.
        var typeLocation = creation is ObjectCreationExpressionSyntax explicitCreation
            ? explicitCreation.Type.GetLocation()
            : creation.GetLocation();

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns != "System.IO")
            return;

        if (IsSuppressed(context.ContainingSymbol))
            return;

        var name = type.Name;
        switch (name)
        {
            case "FileStream":
            case "FileInfo":
            case "DirectoryInfo":
                if (TakesStringPath(creation, context.SemanticModel, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DirectFileSystemTypeInstantiation,
                        typeLocation,
                        name));
                }
                break;
            case "FileSystemWatcher":
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DirectFileSystemWatcher,
                    typeLocation));
                break;
            case "StreamReader":
            case "StreamWriter":
                // Only the path-based overloads bypass the VFS; wrapping a Stream is allowed.
                if (TakesStringPath(creation, context.SemanticModel, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DirectStreamReaderWriterPath,
                        typeLocation,
                        name));
                }
                break;
        }
    }

    private static void AnalyzeNullableFileSystemService(SyntaxNodeAnalysisContext context)
    {
        if (IsExemptByPath(context.Node.SyntaxTree.FilePath))
            return;

        TypeSyntax? typeSyntax = context.Node switch
        {
            FieldDeclarationSyntax field => field.Declaration.Type,
            ParameterSyntax { Type: { } pt } => pt,
            _ => null,
        };

        if (typeSyntax is not NullableTypeSyntax nullable)
            return;

        if (!IsFileSystemServiceType(nullable.ElementType))
            return;

        if (IsSuppressed(context.ContainingSymbol))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.NullableFileSystemService,
            typeSyntax.GetLocation()));
    }

    private static bool IsFileSystemServiceType(TypeSyntax elementType)
    {
        // Match by the trailing identifier so both `IFileSystemService` and
        // `Orkeon.Domain.FileSystem.IFileSystemService` are covered without
        // requiring semantic resolution.
        var name = elementType switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            _ => null,
        };
        return name == "IFileSystemService";
    }

    private static bool TakesStringPath(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel model,
        System.Threading.CancellationToken ct)
    {
        var args = creation.ArgumentList?.Arguments;
        if (args is null || args.Value.Count == 0)
            return false;

        var first = args.Value[0].Expression;
        var argType = model.GetTypeInfo(first, ct).Type;
        return argType?.SpecialType == SpecialType.System_String;
    }

    private static bool IsSuppressed(ISymbol? symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (HasSuppressAttribute(current))
                return true;

            if (current is IMethodSymbol { AssociatedSymbol: { } associated }
                && HasSuppressAttribute(associated))
                return true;
        }

        if (symbol?.ContainingAssembly is { } assembly && HasSuppressAttribute(assembly))
            return true;

        return false;
    }

    private static bool HasSuppressAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == SuppressAttributeMetadataName);
    }

    /// <summary>
    /// Folder-level exemptions, restricted to the directories that <em>are</em> the
    /// abstraction itself or that the CLAUDE.md taxonomy allows wholesale:
    /// the VFS implementation (<c>FileSystem/</c>), tests, and examples.
    /// </summary>
    /// <remarks>
    /// Sandbox, <c>PathValidator</c> and other security primitives are intentionally
    /// <em>not</em> covered here: a folder-wide blank-check let any new file silently
    /// escape the VFS audit. Those legitimately-raw files are now listed explicitly in
    /// <see cref="ExemptFileSuffixes"/>, and any new I/O must instead carry an explicit
    /// <c>[SuppressVfsCompliance]</c> marker at the use site (see CLAUDE.md exemption
    /// taxonomy: <c>EXCEPTION-BOOTSTRAP</c> / <c>OUT-OF-SCOPE</c> / VFS implementation).
    /// </remarks>
    private static readonly string[] s_exemptFolderSegments =
    {
        // VFS implementation — where the abstraction itself lives.
        "/core/Orkeon.Domain/FileSystem/",
        "/core/Orkeon.Infrastructure/FileSystem/",
        // Tests may use the real disk (DiskBackedFileSystemService fixtures).
        "/tests/",
        // Examples are out of the framework compliance scope.
        "/examples/",
    };

    /// <summary>
    /// Explicit per-file allowlist replacing the former whole-folder blank-checks for
    /// security primitives. Each entry is a legitimately-raw I/O file (bootstrap, host
    /// probing or path-validation primitive) carrying inline <c>// EXCEPTION-…</c> /
    /// <c>// OUT-OF-SCOPE</c> markers. New files under the same folders are NOT covered
    /// and must use <c>[SuppressVfsCompliance]</c> at the use site.
    /// </summary>
    private static readonly string[] s_exemptFileSuffixes =
    {
        // Sandbox bootstrap + host-binary/SDK probing (carry EXCEPTION-BOOTSTRAP / OUT-OF-SCOPE).
        "/core/Orkeon.Infrastructure/Sandbox/SandboxSession.cs",
        "/core/Orkeon.Infrastructure/Sandbox/ProcessIsolationSandbox.cs",
        "/core/Orkeon.Infrastructure/Sandbox/DockerSandbox.cs",
        // Path-validation primitive (symlink/realpath resolution is its core job).
        "/core/Orkeon.Infrastructure/Security/PathValidator.cs",
    };

    private static bool IsExemptByPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path!.Replace('\\', '/');

        if (s_exemptFolderSegments.Any(segment => ContainsSegment(normalized, segment)))
            return true;

        if (s_exemptFileSuffixes.Any(suffix => normalized.EndsWith(suffix, System.StringComparison.Ordinal)))
            return true;

        return false;
    }

    private static bool ContainsSegment(string path, string segment) =>
        path.IndexOf(segment, System.StringComparison.Ordinal) >= 0;
}
