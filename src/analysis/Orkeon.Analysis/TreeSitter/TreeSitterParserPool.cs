using System.Collections.Concurrent;
using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using TsLanguage = TreeSitter.Language;
using TsNode = TreeSitter.Node;
using TsParser = TreeSitter.Parser;

namespace Orkeon.Analysis.TreeSitter;

public sealed class TreeSitterParserPool : ITreeSitterParser
{
    private readonly ConcurrentDictionary<string, TsLanguage> _languages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentBag<TsParser>> _pools = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public TsParser Rent(string language)
    {
        ThrowIfDisposed();
        var key = Normalize(language);
        var bag = _pools.GetOrAdd(key, _ => []);
        if (bag.TryTake(out var parser)) return parser;
        var tsLang = _languages.GetOrAdd(key, LanguageRegistry.Create);
        return new TsParser(tsLang);
    }

    public void Return(TsParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        if (_disposed) { parser.Dispose(); return; }
        var language = parser.Language;
        if (language is null) { parser.Dispose(); return; }
        var key = _languages
            .FirstOrDefault(kv => ReferenceEquals(kv.Value, language) || kv.Value.Name == language.Name).Key
            ?? language.Name;
        var bag = _pools.GetOrAdd(key, _ => []);
        bag.Add(parser);
    }

    public ParsedTree Parse(string sourceCode, string language)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        var parser = Rent(language);
        try
        {
            var tree = parser.Parse(sourceCode)
                ?? throw new InvalidOperationException($"Tree-sitter returned a null tree for language '{language}'.");
            var diagnostics = CollectDiagnostics(tree.RootNode);
            return new ParsedTree(tree, Normalize(language), diagnostics);
        }
        finally
        {
            Return(parser);
        }
    }

    public static ImmutableArray<ParseDiagnostic> CollectDiagnostics(TsNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!root.HasError) return [];
        var builder = ImmutableArray.CreateBuilder<ParseDiagnostic>();
        var stack = new Stack<TsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.IsError || node.IsMissing)
            {
                builder.Add(CreateDiagnostic(node));
                continue;
            }
            foreach (var child in node.Children)
            {
                if (child.HasError) stack.Push(child);
            }
        }
        return builder.ToImmutable();
    }

    private static ParseDiagnostic CreateDiagnostic(TsNode node) => new()
    {
        Severity = DiagnosticSeverity.Error,
        StartLine = node.StartPosition.Row + 1,
        StartColumn = node.StartPosition.Column + 1,
        EndLine = node.EndPosition.Row + 1,
        EndColumn = node.EndPosition.Column + 1,
        Message = node.IsMissing ? $"Missing node '{node.Type}'" : "Unexpected token",
        TreeSitterNodeType = node.IsMissing ? "MISSING" : "ERROR",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var bag in _pools.Values)
        {
            while (bag.TryTake(out var parser)) parser.Dispose();
        }
        _pools.Clear();
        foreach (var lang in _languages.Values) lang.Dispose();
        _languages.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string Normalize(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language must not be empty.", nameof(language));
        if (language.Equals("c-sharp", StringComparison.OrdinalIgnoreCase)) return "csharp";
#pragma warning disable CA1308 // lowercase is the canonical normalized language key returned to callers, not a comparison normalization
        return language.ToLowerInvariant();
#pragma warning restore CA1308
    }
}
