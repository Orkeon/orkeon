using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis;

public sealed class SymbolSourceTool : ToolBase<SymbolSourceRequest, SymbolSourceResponse>
{
    private readonly IRaggableStore _store;

    public SymbolSourceTool(IRaggableStore store, ILogger<SymbolSourceTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "symbol_source";
    public override string Description => "Deterministic source citation for a symbol. Returns file path, lines, code, SHA-256, and stable flag.";

    protected override Task<SymbolSourceResponse> ExecuteTypedAsync(SymbolSourceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SymbolSourceResponse> ExecuteTypedCoreAsync()
        {
            var effectiveMode = !string.IsNullOrEmpty(request.StatementId) ? SourceMode.StatementSpan : request.Mode;
            var slice = await _store.GetSourceAsync(request.Fqn, effectiveMode, cancellationToken).ConfigureAwait(false);
            if (slice is null) throw await FqnSuggestions.BuildAsync(_store, request.Fqn, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(request.StatementId))
            {
                var node = await _store.GetAsync(request.Fqn, cancellationToken).ConfigureAwait(false);
                if (node is not null)
                {
                    var stmts = await _store.GetStatementsAsync(node.Id, cancellationToken).ConfigureAwait(false);
                    var stmt = stmts.FirstOrDefault(s => s.Id == request.StatementId);
                    if (stmt is not null)
                    {
                        slice = slice with
                        {
                            StartLine = stmt.StartLine,
                            EndLine = stmt.EndLine ?? stmt.StartLine,
                        };
                    }
                }
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
