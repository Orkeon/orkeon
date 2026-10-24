using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class DocumentedInTool : ToolBase<DocumentedInRequest, DocumentedInResponse>
{
    private readonly IRaggableStore _store;

    public DocumentedInTool(IRaggableStore store, ILogger<DocumentedInTool>? logger = null) : base(logger)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public override string Name => "documented_in";
    public override string Description => "Markdown sections that document a symbol.";
    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<DocumentedInResponse> ExecuteTypedAsync(DocumentedInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = IndexPaths.RootPrefix(_store);
        var result = await DocumentsQuery.DocumentedInAsync(_store, request.Fqn, cancellationToken).ConfigureAwait(false);
        return new DocumentedInResponse
        {
            Sections =
            [
                .. result.Sections.Select(s => new DocumentedInSection
                {
                    Path = IndexPaths.Relative(s.Path, root),
                    HeadingPath = s.HeadingPath,
                    StartLine = s.StartLine,
                }),
            ],
            Unresolved = result.Unresolved,
        };
    }
}

public sealed record DocumentedInRequest
{
    [JsonPropertyName("fqn")]
    public string Fqn { get; init; } = string.Empty;
}

public sealed record DocumentedInSection
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("heading_path")]
    public string HeadingPath { get; init; } = string.Empty;

    [JsonPropertyName("start_line")]
    public int StartLine { get; init; }
}

public sealed record DocumentedInResponse
{
    [JsonPropertyName("sections")]
    public ImmutableList<DocumentedInSection> Sections { get; init; } = [];

    [JsonPropertyName("unresolved")]
    public ImmutableList<string> Unresolved { get; init; } = [];
}
