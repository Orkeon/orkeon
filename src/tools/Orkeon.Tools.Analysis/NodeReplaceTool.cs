using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record NodeReplaceRequest
{
    [FieldSchema(Description = "Path of the file to edit, absolute under a mounted root or relative to the working root.", Example = "/workspace/src/Calc.cs")]
    [JsonPropertyName("path")] public string Path { get; init; } = "";

    [FieldSchema(Description = "Selector of the node to replace, as node_outline lists it: Ns.Type.Member(paramTypes), Section/Sub for Markdown, or @ts:<node-path>.", Example = "Demo.Calc.Add(int)")]
    [JsonPropertyName("selector")] public string Selector { get; init; } = "";

    [FieldSchema(Description = "SHA-256 of the node's current text, as node_read returned it; the replacement is refused when it is stale.")]
    [JsonPropertyName("expected_hash")] public string ExpectedHash { get; init; } = "";

    [FieldSchema(Description = "The replacement text of the whole node, its leading comments and attributes included.")]
    [JsonPropertyName("text")] public string Text { get; init; } = "";
}

public sealed record NodeReplaceRange
{
    [JsonPropertyName("start_byte")] public required int StartByte { get; init; }
    [JsonPropertyName("end_byte")] public required int EndByte { get; init; }
    [JsonPropertyName("start_line")] public required int StartLine { get; init; }
    [JsonPropertyName("end_line")] public required int EndLine { get; init; }
}

public sealed record NodeReplaceResponse
{
    [JsonPropertyName("range")] public required NodeReplaceRange Range { get; init; }
    [JsonPropertyName("new_hash")] public required string NewHash { get; init; }
}

public sealed class NodeReplaceTool : FileToolBase<NodeReplaceRequest, NodeReplaceResponse>
{
    private readonly StructuralEditEngine _engine;
    private readonly IIndexInvalidation? _indexInvalidation;

    public NodeReplaceTool(IFileSystemService fs, TreeSitterParserPool pool, IIndexInvalidation? indexInvalidation = null)
        : base(fs)
    {
        _engine = new StructuralEditEngine(fs, pool);
        _indexInvalidation = indexInvalidation;
    }

    public override string Name => "node_replace";
    public override string Description =>
        "Replaces one node of a file by selector and expected SHA-256 (from node_read): refused when the hash is stale "
        + "or when the replacement introduces a parse error that was not there before; the file is then untouched. "
        + "The replaced span includes the node's leading comments and attributes, so a replacement that omits them removes them; "
        + "a Markdown section replaces its sub-sections too. Line endings are normalised to the file's.";
    public override ToolAccess Access => ToolAccess.Edit;

    protected override async Task<NodeReplaceResponse> ExecuteTypedAsync(NodeReplaceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pathValidation = ResolveVirtualPath(request.Path, FileAccessRights.Write);
        if (!pathValidation.IsAllowed)
            throw new InvalidOperationException(pathValidation.DenialReason ?? "Invalid or unsafe file path");

        var operation = new NodeReplaceOperation(NodeSelector.Parse(request.Selector), request.ExpectedHash, request.Text);
        var virtualPath = _fileSystemService.ToVirtualPath(pathValidation.ResolvedPath!) ?? AbsolutizePath(request.Path);
        var outcome = await _engine.ApplyAsync(virtualPath, [operation], cancellationToken).ConfigureAwait(false);
        _indexInvalidation?.MarkDirty(virtualPath);
        return new NodeReplaceResponse
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
