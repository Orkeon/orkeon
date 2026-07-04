using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// Roslyn AST-based static security analyzer for C# code.
/// Parses the code into a syntax tree and walks it to detect dangerous patterns.
/// <para>
/// IMPORTANT — defense-in-depth only: this analyzer is a syntactic blocklist and is
/// inherently bypassable (computed member names, indirect reflection, dynamic dispatch).
/// It must NOT be relied upon as the security boundary for executing LLM-generated code.
/// The boundary is the OS-level sandbox (<c>DockerSandbox</c>: <c>--network=none</c>,
/// <c>--read-only</c>, <c>tmpfs noexec,nosuid</c>, memory/CPU caps). Use this analyzer to
/// reject obvious dangerous code early, not to authorize execution outside an isolated sandbox.
/// </para>
/// </summary>
public sealed class RoslynCodeSecurityAnalyzer : ICodeSecurityAnalyzer
{
    /// <summary>
    /// Bundles the location context for a syntax node being analyzed.
    /// </summary>
    private readonly record struct NodeContext(int LineNumber, string Snippet);
    /// <inheritdoc/>
    public CodeSecurityReport Analyze(string code, SecurityAnalysisOptions? options = null)
    {
        options ??= new SecurityAnalysisOptions();
        var violations = new List<SecurityViolation>();

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();

        CheckUsingDirectives(root, options, violations);
        CheckUnsafeBlocks(root, options, violations);
        CheckInvocations(root, options, violations);
        CheckObjectCreations(root, options, violations);
        CheckMemberAccess(root, options, violations);
        CheckDynamicAndPInvoke(root, options, violations);

        var overallRisk = violations.Count > 0
            ? violations.Max(v => v.Risk)
            : SecurityRiskLevel.None;

        return new CodeSecurityReport
        {
            IsAllowed = overallRisk < SecurityRiskLevel.High,
            Violations = violations,
            OverallRisk = overallRisk
        };
    }

    private static void CheckUsingDirectives(
        CompilationUnitSyntax root,
        SecurityAnalysisOptions options,
        List<SecurityViolation> violations)
    {
        foreach (var usingDirective in root.Usings)
        {
            var ns = usingDirective.Name?.ToString();
            if (ns == null) continue;

            var isAllowed = options.AllowedNamespaces.Any(allowed =>
                ns == allowed || ns.StartsWith(allowed + ".", StringComparison.Ordinal));

            if (!isAllowed)
            {
                var lineNumber = GetLineNumber(usingDirective);
                violations.Add(new SecurityViolation
                {
                    Rule = "BlockedNamespace",
                    Description = $"Namespace '{ns}' is not in the allowed list",
                    Risk = ClassifyNamespaceRisk(ns, options),
                    LineNumber = lineNumber,
                    CodeSnippet = usingDirective.ToString().Trim()
                });
            }
        }
    }

    private static void CheckUnsafeBlocks(
        CompilationUnitSyntax root,
        SecurityAnalysisOptions options,
        List<SecurityViolation> violations)
    {
        if (options.AllowUnsafeCode) return;

        var unsafeStatements = root.DescendantNodes().OfType<UnsafeStatementSyntax>();
        foreach (var unsafeStmt in unsafeStatements)
        {
            violations.Add(new SecurityViolation
            {
                Rule = "UnsafeCode",
                Description = "Unsafe code blocks are not allowed",
                Risk = SecurityRiskLevel.Critical,
                LineNumber = GetLineNumber(unsafeStmt),
                CodeSnippet = "unsafe { ... }"
            });
        }
    }

    private static readonly string[] ProcessExecPatterns =
    [
        "Process.Start", "ProcessStartInfo", "Process.GetProcesses",
        "Process.GetProcessById", "Process.Kill"
    ];

    private static readonly string[] ReflectionPatterns =
    [
        "Assembly.Load", "Assembly.LoadFrom", "Assembly.LoadFile",
        "Type.GetType", "Activator.CreateInstance", ".GetMethod", ".GetProperty",
        ".GetField", ".Invoke",
        // Indirect type/assembly resolution used to evade the namespace/type blocklist
        // (e.g. typeof(int).Assembly.GetType("System.Diagnostics.Process")).
        ".Assembly.GetType", ".Assembly.CreateInstance", ".GetType", ".GetMethods",
        ".GetProperties", ".GetFields", ".GetMembers", ".GetConstructor", ".InvokeMember",
        ".DynamicInvoke", ".MakeGenericType", "RuntimeHelpers.GetUninitializedObject",
        // Expression-tree / delegate based indirection
        "Expression.Call", "Expression.Lambda", "Expression.New", ".Compile",
        // Marshal-based memory/function-pointer escapes
        "Marshal.GetDelegateForFunctionPointer", "Marshal.GetFunctionPointerForDelegate"
    ];

    private static readonly string[] FileIOPatterns =
    [
        "File.Read", "File.Write", "File.Open", "File.Create",
        "File.Delete", "File.Copy", "File.Move", "File.Append", "File.Exists",
        "Directory.Create", "Directory.Delete", "Directory.GetFiles",
        "Directory.GetDirectories", "Directory.Exists",
        "StreamReader", "StreamWriter", "FileStream"
    ];

    private static readonly string[] NetworkingPatterns =
    [
        "HttpClient", "WebClient", "WebRequest",
        "Socket", "TcpClient", "TcpListener", "UdpClient",
        "Dns.GetHost", "HttpRequestMessage"
    ];

    /// <summary>
    /// Describes a security check rule for pattern-based detection.
    /// </summary>
    private readonly record struct SecurityCheckRule(
        bool IsAllowed,
        string Rule,
        string DescriptionPrefix,
        SecurityRiskLevel Risk);

    private static void CheckInvocations(
        CompilationUnitSyntax root,
        SecurityAnalysisOptions options,
        List<SecurityViolation> violations)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations)
        {
            var text = invocation.Expression.ToString();
            var ctx = new NodeContext(GetLineNumber(invocation), invocation.ToString().Trim());

            CheckInvocationCategory(text, ProcessExecPatterns,
                new SecurityCheckRule(options.AllowProcessExec, "ProcessExecution", "Process execution is not allowed", SecurityRiskLevel.Critical),
                ctx, violations);

            CheckInvocationCategory(text, ReflectionPatterns,
                new SecurityCheckRule(options.AllowReflection, "Reflection", "Reflection is not allowed", SecurityRiskLevel.High),
                ctx, violations);

            CheckInvocationCategory(text, FileIOPatterns,
                new SecurityCheckRule(options.AllowFileIO, "FileIO", "File I/O is not allowed", SecurityRiskLevel.High),
                ctx, violations);

            CheckInvocationCategory(text, NetworkingPatterns,
                new SecurityCheckRule(options.AllowNetworking, "Networking", "Network access is not allowed", SecurityRiskLevel.High),
                ctx, violations);
        }
    }

    private static void CheckInvocationCategory(
        string text,
        string[] patterns,
        SecurityCheckRule rule,
        NodeContext ctx,
        List<SecurityViolation> violations)
    {
        if (rule.IsAllowed)
            return;

        if (!ContainsAny(text, patterns))
            return;

        violations.Add(new SecurityViolation
        {
            Rule = rule.Rule,
            Description = $"{rule.DescriptionPrefix}: {text}",
            Risk = rule.Risk,
            LineNumber = ctx.LineNumber,
            CodeSnippet = ctx.Snippet
        });
    }

    private static readonly HashSet<string> ProcessCreationTypes = new(StringComparer.Ordinal)
    {
        "Process", "ProcessStartInfo",
        "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo"
    };

    private static readonly HashSet<string> NetworkCreationTypes = new(StringComparer.Ordinal)
    {
        "HttpClient", "WebClient", "Socket", "TcpClient", "TcpListener", "UdpClient",
        "System.Net.Http.HttpClient", "System.Net.WebClient",
        "System.Net.Sockets.Socket", "System.Net.Sockets.TcpClient",
        "System.Net.Sockets.TcpListener", "System.Net.Sockets.UdpClient"
    };

    private static readonly HashSet<string> FileIOCreationTypes = new(StringComparer.Ordinal)
    {
        "StreamReader", "StreamWriter", "FileStream",
        "System.IO.StreamReader", "System.IO.StreamWriter", "System.IO.FileStream"
    };

    private static void CheckObjectCreations(
        CompilationUnitSyntax root,
        SecurityAnalysisOptions options,
        List<SecurityViolation> violations)
    {
        var objectCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
        foreach (var creation in objectCreations)
        {
            var typeName = creation.Type.ToString();
            var ctx = new NodeContext(GetLineNumber(creation), creation.ToString().Trim());

            CheckBlockedTypes(typeName, options.BlockedTypes, ctx, violations);
            CheckObjectCreationCategory(typeName, ProcessCreationTypes,
                new SecurityCheckRule(options.AllowProcessExec, "ProcessExecution",
                    $"Process creation is not allowed: new {typeName}(...)", SecurityRiskLevel.Critical),
                ctx, violations);
            CheckObjectCreationCategory(typeName, NetworkCreationTypes,
                new SecurityCheckRule(options.AllowNetworking, "Networking",
                    $"Network object creation is not allowed: new {typeName}(...)", SecurityRiskLevel.High),
                ctx, violations);
            CheckObjectCreationCategory(typeName, FileIOCreationTypes,
                new SecurityCheckRule(options.AllowFileIO, "FileIO",
                    $"File I/O object creation is not allowed: new {typeName}(...)", SecurityRiskLevel.High),
                ctx, violations);
        }
    }

    private static void CheckBlockedTypes(
        string typeName,
        IReadOnlyList<string> blockedTypes,
        NodeContext ctx,
        List<SecurityViolation> violations)
    {
        foreach (var blockedType in blockedTypes)
        {
            var shortName = blockedType.Contains('.', StringComparison.Ordinal)
                ? blockedType.Substring(blockedType.LastIndexOf('.') + 1)
                : blockedType;

            if (typeName != blockedType && typeName != shortName)
                continue;

            violations.Add(new SecurityViolation
            {
                Rule = "BlockedType",
                Description = $"Type '{blockedType}' is not allowed",
                Risk = SecurityRiskLevel.Critical,
                LineNumber = ctx.LineNumber,
                CodeSnippet = ctx.Snippet
            });
        }
    }

    private static void CheckObjectCreationCategory(
        string typeName,
        HashSet<string> blockedTypeNames,
        SecurityCheckRule rule,
        NodeContext ctx,
        List<SecurityViolation> violations)
    {
        if (rule.IsAllowed)
            return;

        if (!blockedTypeNames.Contains(typeName))
            return;

        violations.Add(new SecurityViolation
        {
            Rule = rule.Rule,
            Description = rule.DescriptionPrefix,
            Risk = rule.Risk,
            LineNumber = ctx.LineNumber,
            CodeSnippet = ctx.Snippet
        });
    }

    private static void CheckMemberAccess(
        CompilationUnitSyntax root,
        SecurityAnalysisOptions options,
        List<SecurityViolation> violations)
    {
        if (options.AllowReflection) return;

        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
        foreach (var access in memberAccesses)
        {
            var text = access.ToString();

            // typeof(...).GetMethod / typeof(...).GetProperty etc.
            if (access.Expression is TypeOfExpressionSyntax
                && ContainsAny(access.Name.Identifier.Text,
                    "GetMethod", "GetMethods", "GetProperty", "GetProperties",
                    "GetField", "GetFields", "GetMembers", "InvokeMember"))
            {
                violations.Add(new SecurityViolation
                {
                    Rule = "Reflection",
                    Description = $"Reflection via typeof is not allowed: {text}",
                    Risk = SecurityRiskLevel.High,
                    LineNumber = GetLineNumber(access),
                    CodeSnippet = text.Trim()
                });
            }
        }
    }

    /// <summary>
    /// Flags <c>dynamic</c> dispatch and P/Invoke (<c>[DllImport]</c>) declarations.
    /// Both bypass the static blocklist: <c>dynamic</c> resolves members at runtime
    /// (computed member names), and <c>DllImport</c> calls arbitrary native code.
    /// </summary>
    private static void CheckDynamicAndPInvoke(
        CompilationUnitSyntax root,
        SecurityAnalysisOptions options,
        List<SecurityViolation> violations)
    {
        if (!options.AllowReflection)
        {
            foreach (var dynamicType in root.DescendantNodes().OfType<IdentifierNameSyntax>()
                         .Where(id => !id.IsVar && id.Identifier.Text == "dynamic"
                                      && id.Parent is not MemberAccessExpressionSyntax))
            {
                violations.Add(new SecurityViolation
                {
                    Rule = "Reflection",
                    Description = "Dynamic dispatch ('dynamic') is not allowed: it resolves members at runtime and bypasses static analysis",
                    Risk = SecurityRiskLevel.High,
                    LineNumber = GetLineNumber(dynamicType),
                    CodeSnippet = dynamicType.Parent?.ToString().Trim() ?? "dynamic"
                });
            }
        }

        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            var name = attribute.Name.ToString();
            if (name is not ("DllImport" or "DllImportAttribute"
                or "LibraryImport" or "LibraryImportAttribute"))
                continue;

            violations.Add(new SecurityViolation
            {
                Rule = "PInvoke",
                Description = $"P/Invoke declaration is not allowed: [{name}] invokes arbitrary native code",
                Risk = SecurityRiskLevel.Critical,
                LineNumber = GetLineNumber(attribute),
                CodeSnippet = attribute.ToString().Trim()
            });
        }
    }

    private static SecurityRiskLevel ClassifyNamespaceRisk(string ns, SecurityAnalysisOptions options)
    {
        if (ns.StartsWith("System.Diagnostics", StringComparison.Ordinal))
            return SecurityRiskLevel.Critical;
        if (ns.StartsWith("System.Reflection", StringComparison.Ordinal))
            return options.AllowReflection ? SecurityRiskLevel.Low : SecurityRiskLevel.High;
        if (ns.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal))
            return SecurityRiskLevel.Critical;
        if (ns.StartsWith("System.Net", StringComparison.Ordinal))
            return options.AllowNetworking ? SecurityRiskLevel.Low : SecurityRiskLevel.High;
        if (ns.StartsWith("System.IO", StringComparison.Ordinal))
            return options.AllowFileIO ? SecurityRiskLevel.Low : SecurityRiskLevel.High;

        return SecurityRiskLevel.Medium;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(p => text.Contains(p, StringComparison.Ordinal));
    }

    private static int GetLineNumber(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }
}
