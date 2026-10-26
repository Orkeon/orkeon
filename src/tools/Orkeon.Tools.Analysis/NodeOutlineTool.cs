using Orkeon.Tools.Abstractions.Security;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record NodeOutlineRequest
{
    [FieldSchema(Description = "Path of the file to outline, absolute under a mounted root or relative to the working root.", Example = "/workspace/src/Calc.cs")]
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";
}

public sealed record NodeOutlineEntryDto
{
    [JsonPropertyName("selector")] public required string Selector { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("start_byte")] public required int StartByte { get; init; }
    [JsonPropertyName("end_byte")] public required int EndByte { get; init; }
    [JsonPropertyName("start_line")] public required int StartLine { get; init; }
    [JsonPropertyName("end_line")] public required int EndLine { get; init; }
}

public sealed record NodeOutlineResponse
{
    [JsonPropertyName("members")]
    public required ImmutableList<NodeOutlineEntryDto> Members { get; init; }
}

public sealed class NodeOutlineTool : ToolBase<NodeOutlineRequest, NodeOutlineResponse>
{
    private readonly IFileSystemService _fs;
    private readonly TreeSitterParserPool _pool;

    public NodeOutlineTool(IFileSystemService fs, TreeSitterParserPool pool)
    {
        _fs = fs;
        _pool = pool;
    }

    public override string Name => "node_outline";
    public override string Description => "Outline of a file's members.";
    public override ToolAccess Access => ToolAccess.Read;

    // A missing path is named as such: the empty extension used to surface as
    // "No tree-sitter grammar for extension ''" (#200). The path the tool will read is judged
    // against the agent's root once made absolute, as every file tool does: '../p2' under a root
    // must not reach the sibling worktree.
    protected override string? ValidateTypedRequest(NodeOutlineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return WorkingDirectoryResolver.PathRequiredMessage;
        return AgentRootGuard.Check(_fs, WorkingDirectoryResolver.Absolutize(request.Path, _fs))?.DenialReason;
    }

    protected override async Task<NodeOutlineResponse> ExecuteTypedAsync(NodeOutlineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A relative path resolves against the working directory, as for every file tool (#199).
        var path = WorkingDirectoryResolver.Absolutize(request.Path, _fs);
        var file = await StructuralFile.LoadAsync(_fs, _pool, path).ConfigureAwait(false);
        return new NodeOutlineResponse
        {
            Members = file.Outline().Select(n => new NodeOutlineEntryDto
            {
                Selector = n.Selector,
                Kind = n.Kind,
                StartByte = n.Range.StartByte,
                EndByte = n.Range.EndByte,
                StartLine = n.Range.StartLine,
                EndLine = n.Range.EndLine,
            }).ToImmutableList(),
        };
    }
}
