using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orkeon.Compliance.Vfs;

/// <summary>
/// Reports every reference to a member of <c>System.Console</c> in framework code (ORKCON001):
/// output goes through an injected <c>ILogger</c>, interactive reads and writes through an
/// injected abstraction or <c>TextWriter</c>/<c>TextReader</c>. The rule works on the resolved
/// symbol, so a property named <c>Console</c> of another type is not mistaken for it, and
/// <c>using static System.Console</c> does not hide a call.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConsoleUsageAnalyzer : DiagnosticAnalyzer
{
    private const string SuppressAttributeMetadataName =
        "Orkeon.Compliance.Vfs.SuppressConsoleComplianceAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.DirectConsoleUsage);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null) throw new System.ArgumentNullException(nameof(context));
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(
            AnalyzeMemberReference,
            OperationKind.Invocation,
            OperationKind.PropertyReference,
            OperationKind.EventReference,
            OperationKind.MethodReference);
    }

    private static void AnalyzeMemberReference(OperationAnalysisContext context)
    {
        var member = context.Operation switch
        {
            IInvocationOperation invocation => (ISymbol)invocation.TargetMethod,
            IPropertyReferenceOperation property => property.Property,
            IEventReferenceOperation @event => @event.Event,
            IMethodReferenceOperation method => method.Method,
            _ => null,
        };

        if (member?.ContainingType is not { Name: "Console" } type
            || type.ContainingNamespace?.ToDisplayString() != "System")
            return;

        if (IsExemptByPath(context.Operation.Syntax.SyntaxTree.FilePath))
            return;

        if (IsSuppressed(context.ContainingSymbol))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.DirectConsoleUsage,
            context.Operation.Syntax.GetLocation(),
            member.Name));
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

        return symbol?.ContainingAssembly is { } assembly && HasSuppressAttribute(assembly);
    }

    private static bool HasSuppressAttribute(ISymbol symbol) =>
        symbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == SuppressAttributeMetadataName);

    /// <summary>Tests and examples are out of the framework scope.</summary>
    private static bool IsExemptByPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path!.Replace('\\', '/');
        return normalized.IndexOf("/tests/", System.StringComparison.Ordinal) >= 0
            || normalized.IndexOf("/examples/", System.StringComparison.Ordinal) >= 0;
    }
}
