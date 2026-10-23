using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class DocumentsOfTool : ToolBase<DocumentsOfRequest, DocumentsOfResponse>
{
    private readonly IRaggableStore _store;

    public DocumentsOfTool(IRaggableStore store, ILogger<DocumentsOfTool>? logger = null) : base(logger)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public override string Name => "documents_of";
    public override string Description => "Symbols documented by the sections of a Markdown file.";
    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<DocumentsOfResponse> ExecuteTypedAsync(DocumentsOfRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = _store.GetIndexedRoots().Select(r => r.VirtualRoot).FirstOrDefault() ?? "/";
        var prefix = IndexPaths.RootPrefix(_store);
        var result = await DocumentsQuery.DocumentsOfAsync(_store, request.Path, root, cancellationToken).ConfigureAwait(false);
        return new DocumentsOfResponse
        {
            Symbols =
            [
                .. result.Symbols.Select(s => new DocumentsOfSymbol { Fqn = s.Fqn, Kind = s.Kind, File = IndexPaths.Relative(s.File, prefix), HeadingPath = s.HeadingPath }),
            ],
            Unresolved = result.Unresolved,
        };
    }
}

public sealed record DocumentsOfRequest
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

public sealed record DocumentsOfSymbol
{
    [JsonPropertyName("fqn")]
    public string Fqn { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;

    [JsonPropertyName("heading_path")]
    public string HeadingPath { get; init; } = string.Empty;
}

public sealed record DocumentsOfResponse
{
    [JsonPropertyName("symbols")]
    public ImmutableList<DocumentsOfSymbol> Symbols { get; init; } = [];

    [JsonPropertyName("unresolved")]
    public ImmutableList<string> Unresolved { get; init; } = [];
}
