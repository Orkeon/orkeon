using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record NodeEnsureRequest
{
    [JsonPropertyName("op")] public string Op { get; init; } = "";
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("selector")] public string? Selector { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("module")] public string? Module { get; init; }
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("index")] public int? Index { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }
}

public sealed record NodeEnsureResponse
{
    [JsonPropertyName("applied")] public bool Applied { get; init; }
    [JsonPropertyName("range")] public required NodeReplaceRange Range { get; init; }
    [JsonPropertyName("new_hash")] public required string NewHash { get; init; }
}

public sealed class NodeEnsureTool : FileToolBase<NodeEnsureRequest, NodeEnsureResponse>
{
    private readonly StructuralEditEngine _engine;
    private readonly IIndexInvalidation? _indexInvalidation;

    public NodeEnsureTool(IFileSystemService fs, TreeSitterParserPool pool, IIndexInvalidation? indexInvalidation = null)
        : base(fs)
    {
        _engine = new StructuralEditEngine(fs, pool);
        _indexInvalidation = indexInvalidation;
    }

    public override string Name => "node_ensure";
    public override string Description =>
        "Applies one idempotent intent operation on one file: ensure_using / ensure_import (name, plus module for TypeScript) "
        + "adds the using or import line in its place only when absent; ensure_field / ensure_member (selector of the type, text) "
        + "adds a field, property or method next to its peers only when no member of that name exists. On Markdown: ensure_section (name = heading path, optional level) creates the missing headings; "
        + "insert_under_heading (selector, text) appends at the end of a section; replace_mermaid (selector, index 0-based, text) replaces the body of the n-th mermaid fence of a section. Calling it again changes "
        + "nothing and writes nothing: applied is false and range is the existing line or member. The insert is refused when it "
        + "introduces a parse error that was not there before, the file is then untouched.";
    public override ToolAccess Access => ToolAccess.Edit;

    protected override async Task<NodeEnsureResponse> ExecuteTypedAsync(NodeEnsureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pathValidation = ResolveVirtualPath(request.Path, FileAccessRights.Write);
        if (!pathValidation.IsAllowed)
            throw new InvalidOperationException(pathValidation.DenialReason ?? "Invalid or unsafe file path");

        var operation = IntentOperations.Create(request.Op, request.Selector, request.Name, request.Module, request.Text, request.Index, request.Level);
        var virtualPath = _fileSystemService.ToVirtualPath(pathValidation.ResolvedPath!) ?? request.Path;
        var outcome = await _engine.ApplyAsync(virtualPath, [operation], cancellationToken).ConfigureAwait(false);
        if (outcome.Applied) _indexInvalidation?.MarkDirty(virtualPath);
        return new NodeEnsureResponse
        {
            Applied = outcome.Applied,
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
