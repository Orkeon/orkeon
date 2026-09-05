using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis;

public sealed class SymbolSourceTool : ToolBase<SymbolSourceRequest, SymbolSourceResponse>
{
    private readonly IRaggableStore _store;
    private readonly IndexFreshnessService? _freshness;

    public SymbolSourceTool(
        IRaggableStore store,
        ILogger<SymbolSourceTool>? logger = null,
        IndexFreshnessService? freshness = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _freshness = freshness;
    }

    public override string Name => "symbol_source";
    public override string Description => "Deterministic source citation for a symbol. Returns file path, lines, code, SHA-256, and stable flag.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<SymbolSourceResponse> ExecuteTypedAsync(SymbolSourceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SymbolSourceResponse> ExecuteTypedCoreAsync()
        {
            // Lazy freshness (PLAN B3): serve the symbol as it IS, not as it was indexed.
            if (_freshness is not null)
                await _freshness.EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
            var effectiveMode = !string.IsNullOrEmpty(request.StatementId) ? SourceMode.StatementSpan : request.Mode;
            var slice = await _store.GetSourceAsync(request.Fqn, effectiveMode, cancellationToken).ConfigureAwait(false);
            if (slice is null) throw await FqnSuggestions.BuildAsync(_store, request.Fqn, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(request.StatementId))
            {
                slice = await NarrowToStatementAsync(slice, request.Fqn, request.StatementId, cancellationToken)
                    .ConfigureAwait(false);
            }

            var linesRequested = Math.Max(1, request.MaxLines);
            var actualLines = Math.Max(1, slice.EndLine - slice.StartLine + 1);
            var truncated = actualLines > linesRequested;

            var shortSha = slice.Sha256.Length >= 8 ? slice.Sha256[..8] : slice.Sha256;
            var citationBlock = BuildCitationBlock(
                request.Fqn,
                slice.VirtualFilePath,
                slice.StartLine,
                slice.EndLine,
                shortSha,
                slice.Language,
                slice.Source);

            return new SymbolSourceResponse
            {
                VirtualFilePath = slice.VirtualFilePath,
                StartLine = slice.StartLine,
                EndLine = slice.EndLine,
                Language = slice.Language,
                Source = slice.Source,
                Sha256 = slice.Sha256,
                Stable = slice.Stable,
                Truncated = truncated,
                Fqn = request.Fqn,
                ShortSha = shortSha,
                MarkdownReadyBlock = citationBlock,
            };
        }
    }

    /// <summary>
    /// Narrows a symbol slice to the span of one of its statements. The slice is returned
    /// untouched when the symbol is gone from the index or no longer carries that
    /// statement: a stale statement id degrades to the whole symbol, it never fails.
    /// </summary>
    private async Task<SourceSlice> NarrowToStatementAsync(
        SourceSlice slice,
        string fqn,
        string statementId,
        CancellationToken cancellationToken)
    {
        var node = await _store.GetAsync(fqn, cancellationToken).ConfigureAwait(false);
        if (node is null) return slice;

        var statements = await _store.GetStatementsAsync(node.Id, cancellationToken).ConfigureAwait(false);
        var statement = statements.FirstOrDefault(s => s.Id == statementId);
        if (statement is null) return slice;

        return slice with
        {
            StartLine = statement.StartLine,
            EndLine = statement.EndLine ?? statement.StartLine,
        };
    }

    private static string BuildCitationBlock(
        string fqn,
        string filePath,
        int startLine,
        int endLine,
        string shortSha,
        string language,
        string source)
    {
        var lang = string.IsNullOrEmpty(language) ? "" : language;
        return $"```{lang}\n"
            + $"// FQN  : {fqn}\n"
            + $"// File : {filePath}:{startLine}-{endLine}\n"
            + $"// SHA  : sha256:{shortSha}\n"
            + source
            + (source.EndsWith('\n') ? "```" : "\n```");
    }
}
