using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record NodeInsertRequest
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("selector")] public string Selector { get; init; } = "";
    [JsonPropertyName("position")] public string Position { get; init; } = "";
    [JsonPropertyName("text")] public string Text { get; init; } = "";
}

public sealed record NodeInsertResponse
{
    [JsonPropertyName("range")] public required NodeReplaceRange Range { get; init; }
    [JsonPropertyName("new_hash")] public required string NewHash { get; init; }
}

public sealed class NodeInsertTool : FileToolBase<NodeInsertRequest, NodeInsertResponse>
{
    private readonly StructuralEditEngine _engine;
    private readonly IIndexInvalidation? _indexInvalidation;

    public NodeInsertTool(IFileSystemService fs, TreeSitterParserPool pool, IIndexInvalidation? indexInvalidation = null)
        : base(fs)
    {
        _engine = new StructuralEditEngine(fs, pool);
        _indexInvalidation = indexInvalidation;
    }

    public override string Name => "node_insert";
    public override string Description =>
        "Inserts text relative to an anchor node: position 'inside' appends to a type or namespace body (an enum member gets "
        + "its separating comma) or to the end of a Markdown section, 'before' puts it above the anchor's leading comments and "
        + "attributes, 'after' below the anchor. There is no expected_hash: no anchor hash is required for before/after, the "
        + "anchor is only a location. The text is re-indented line by line to the siblings' indentation (tabs or spaces as the file "
        + "does); the insert is refused when it introduces a parse error that was not there before, the file is then untouched.";
    public override ToolAccess Access => ToolAccess.Edit;

    protected override async Task<NodeInsertResponse> ExecuteTypedAsync(NodeInsertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pathValidation = ResolveVirtualPath(request.Path, FileAccessRights.Write);
        if (!pathValidation.IsAllowed)
            throw new InvalidOperationException(pathValidation.DenialReason ?? "Invalid or unsafe file path");

        var position = request.Position.ToUpperInvariant() switch
        {
            "INSIDE" => InsertPosition.Inside,
            "BEFORE" => InsertPosition.Before,
            "AFTER" => InsertPosition.After,
            _ => throw new ArgumentException("position must be 'inside', 'before' or 'after'.", nameof(request)),
        };
        var operation = new NodeInsertOperation(NodeSelector.Parse(request.Selector), position, request.Text);
        var virtualPath = _fileSystemService.ToVirtualPath(pathValidation.ResolvedPath!) ?? request.Path;
        var outcome = await _engine.ApplyAsync(virtualPath, [operation], cancellationToken).ConfigureAwait(false);
        _indexInvalidation?.MarkDirty(virtualPath);
        return new NodeInsertResponse
        {
            Range = new NodeReplaceRange
            {
                StartByte = outcome.Range.StartByte,
                EndByte = outcome.Range.EndByte,
                StartLine = outcome.Range.StartLine,
                EndLine = outcome.Range.EndLine,
            },
            NewHash = outcome.NewHash,
        };
    }
}
