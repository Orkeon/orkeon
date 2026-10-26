using Orkeon.Tools.Abstractions.Security;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record NodeReadRequest
{
    [FieldSchema(Description = "Path of the file holding the member, absolute under a mounted root or relative to the working root.", Example = "/workspace/src/Calc.cs")]
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [FieldSchema(Description = "Member to read. Readable form: Ns.Type.Member(paramTypes), e.g. Demo.Calc.Add(int). "
        + "Markdown form: Section/Sub; sibling headings of the same name take a 1-based ordinal, e.g. Section/Notes[2]. Fallback form: @ts:<node-path>, the tree-sitter path root/<index>/<kind>/... returned when a readable selector is ambiguous.")]
    [JsonPropertyName("selector")]
    public string Selector { get; init; } = "";
}

public sealed record NodeReadResponse
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("selector")] public required string Selector { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("text")] public required string Text { get; init; }
    [JsonPropertyName("start_byte")] public required int StartByte { get; init; }
    [JsonPropertyName("end_byte")] public required int EndByte { get; init; }
    [JsonPropertyName("start_line")] public required int StartLine { get; init; }
    [JsonPropertyName("end_line")] public required int EndLine { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
}

public sealed class NodeReadTool : ToolBase<NodeReadRequest, NodeReadResponse>
{
    private readonly IFileSystemService _fs;
    private readonly TreeSitterParserPool _pool;

    public NodeReadTool(IFileSystemService fs, TreeSitterParserPool pool)
    {
        _fs = fs;
        _pool = pool;
    }

    public override string Name => "node_read";
    public override string Description => "Reads one member of a file by selector: text, range and SHA-256, uncut.";
    public override ToolAccess Access => ToolAccess.Read;

    // A missing path is named as such: the empty extension used to surface as
    // "No tree-sitter grammar for extension ''" (#200). The path the tool will read is judged
    // against the agent's root once made absolute, as every file tool does: '../p2' under a root
    // must not reach the sibling worktree.
    protected override string? ValidateTypedRequest(NodeReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return WorkingDirectoryResolver.PathRequiredMessage;
        return AgentRootGuard.Check(_fs, WorkingDirectoryResolver.Absolutize(request.Path, _fs))?.DenialReason;
    }

    protected override async Task<NodeReadResponse> ExecuteTypedAsync(NodeReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A relative path resolves against the working directory, as for every file tool (#199).
        var path = WorkingDirectoryResolver.Absolutize(request.Path, _fs);
        var file = await StructuralFile.LoadAsync(_fs, _pool, path).ConfigureAwait(false);
        var node = file.Resolve(NodeSelector.Parse(request.Selector));
        return new NodeReadResponse
        {
            Path = path,
            Selector = node.Selector,
            Kind = node.Kind,
            Text = node.Text,
            StartByte = node.Range.StartByte,
            EndByte = node.Range.EndByte,
            StartLine = node.Range.StartLine,
            EndLine = node.Range.EndLine,
            Sha256 = node.Sha256,
        };
    }
}
