using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record EditBatchOperation
{
    [FieldSchema(Description = "The operation: node_replace, node_insert or an intent operation.", Enum = new[] { "node_replace", "node_insert", "ensure_using", "ensure_import", "ensure_field", "ensure_member", "ensure_section", "insert_under_heading", "replace_mermaid" }, Example = "node_replace")]
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";

    [FieldSchema(Description = "Path of the file to edit, absolute under a mounted root or relative to the working root.", Example = "/workspace/src/Calc.cs")]
    [JsonPropertyName("path")] public string Path { get; init; } = "";

    [FieldSchema(Description = "Selector of the node (node_replace), of the anchor (node_insert), of the type (ensure_field, ensure_member) or of the Markdown section, as node_outline lists it.", IsRequired = false, Example = "Demo.Calc.Add(int)")]
    [JsonPropertyName("selector")] public string Selector { get; init; } = "";

    [FieldSchema(Description = "SHA-256 of the node's current text, as node_read returned it; required by node_replace.")]
    [JsonPropertyName("expected_hash")] public string? ExpectedHash { get; init; }

    [FieldSchema(Description = "Where the text goes relative to the anchor; required by node_insert.", Enum = new[] { "inside", "before", "after" }, Example = "inside")]
    [JsonPropertyName("position")] public string? Position { get; init; }

    [FieldSchema(Description = "The replacement, the insert, the member to add, the text to append or the new fence body.", IsRequired = false)]
    [JsonPropertyName("text")] public string Text { get; init; } = "";

    [FieldSchema(Description = "The namespace or module to import (ensure_using, ensure_import), or the heading path to create (ensure_section).", Example = "System.Text")]
    [JsonPropertyName("name")] public string? Name { get; init; }

    [FieldSchema(Description = "The module an import comes from, for TypeScript or JavaScript (ensure_import).", Example = "./calc")]
    [JsonPropertyName("module")] public string? Module { get; init; }

    [FieldSchema(Description = "0-based index of the mermaid fence to replace in the section (replace_mermaid).", Example = 0)]
    [JsonPropertyName("index")] public int? Index { get; init; }

    [FieldSchema(Description = "Heading level of the section to create (ensure_section).", Example = 2)]
    [JsonPropertyName("level")] public int? Level { get; init; }
}

public sealed record EditBatchRequest
{
    [FieldSchema(Description = "The operations, applied as one transaction; those on one file apply in order, each on the bytes the previous one produced.")]
    [JsonPropertyName("operations")] public IReadOnlyList<EditBatchOperation> Operations { get; init; } = [];

    [FieldSchema(Description = "What refuses the batch: 'parse' when an operation introduces a parse error, 'build' when dotnet build on the project above the edited files is red too.", IsRequired = false, Default = "parse", Enum = new[] { "parse", "build" }, Example = "parse")]
    [JsonPropertyName("validate")] public string Validate { get; init; } = "parse";
}

public sealed record EditBatchOperationResult
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("range")] public required NodeReplaceRange Range { get; init; }
    [JsonPropertyName("applied")] public bool Applied { get; init; }
}

public sealed record EditBatchResponse
{
    [JsonPropertyName("operations")] public required IReadOnlyList<EditBatchOperationResult> Operations { get; init; }
    [JsonPropertyName("validation")] public required string Validation { get; init; }
}

public sealed class EditBatchTool : FileToolBase<EditBatchRequest, EditBatchResponse>
{
    private readonly TreeSitterParserPool _pool;
    private readonly IBuildValidator? _buildValidator;
    private readonly IIndexInvalidation? _indexInvalidation;

    public EditBatchTool(
        IFileSystemService fs, TreeSitterParserPool pool, IBuildValidator? buildValidator = null, IIndexInvalidation? indexInvalidation = null)
        : base(fs)
    {
        _pool = pool;
        _buildValidator = buildValidator;
        _indexInvalidation = indexInvalidation;
    }

    public override string Name => "edit_batch";
    public override string Description =>
        "Applies node_replace, node_insert and idempotent intent operations over several files as one transaction. Each operation is "
        + "{kind, path, selector, text} plus expected_hash (required, node_replace only) or position (node_insert: "
        + "'inside', 'before' or 'after'). An intent operation is {kind, path} with kind ensure_using / ensure_import (name, "
        + "module for TypeScript or JavaScript) or ensure_field / ensure_member (selector of the type, text): it adds the line or member only when "
        + "absent and reports applied false when it was already there. Operations on one file apply in order, each on the bytes the previous one "
        + "produced. validate 'parse' (default) refuses the batch when an operation introduces a parse error; 'build' "
        + "also runs dotnet build on the project above the edited files and restores every file when it is red. "
        + "On any failure all files are left as they were.";
    public override ToolAccess Access => ToolAccess.Edit;

    protected override async Task<EditBatchResponse> ExecuteTypedAsync(EditBatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var build = request.Validate.ToUpperInvariant() switch
        {
            "PARSE" => false,
            "BUILD" => true,
            _ => throw new ArgumentException("validate must be 'parse' or 'build'.", nameof(request)),
        };

        var transaction = new EditTransaction(_fileSystemService, _pool, _buildValidator);
        var virtualPaths = new List<string>();
        var physicalPaths = new List<string>();
        foreach (var operation in request.Operations)
        {
            var validation = ResolveVirtualPath(operation.Path, FileAccessRights.Write);
            if (!validation.IsAllowed)
                throw new InvalidOperationException(validation.DenialReason ?? "Invalid or unsafe file path");
            var virtualPath = _fileSystemService.ToVirtualPath(validation.ResolvedPath!) ?? AbsolutizePath(operation.Path);
            if (!physicalPaths.Contains(validation.ResolvedPath!)) physicalPaths.Add(validation.ResolvedPath!);
            virtualPaths.Add(virtualPath);
            transaction.Add(virtualPath, ToOperation(operation));
        }

        var result = await transaction
            .CommitAsync(build ? EditValidation.Build(physicalPaths) : EditValidation.Parse, cancellationToken)
            .ConfigureAwait(false);
        if (result.Build?.Outcome != BuildOutcome.Red)
        {
            foreach (var virtualPath in virtualPaths.Distinct(StringComparer.Ordinal)) _indexInvalidation?.MarkDirty(virtualPath);
        }

        return new EditBatchResponse
        {
            Operations = [.. result.Operations.Select(o => new EditBatchOperationResult
            {
                Path = o.Path,
                Applied = o.Applied,
                Range = new NodeReplaceRange
                {
                    StartByte = o.Range.StartByte,
                    EndByte = o.Range.EndByte,
                    StartLine = o.Range.StartLine,
                    EndLine = o.Range.EndLine,
                },
            })],
            Validation = Describe(result.Build),
        };
    }

    private static string Describe(BuildValidation? build) => build switch
    {
        null => "parse ok",
        { Outcome: BuildOutcome.Green } => "build green",
        { Outcome: BuildOutcome.Red } => $"build red, all files restored: {build.Detail}",
        _ => "build check skipped",
    };

    private static IEditOperation ToOperation(EditBatchOperation operation)
    {
        switch (operation.Kind)
        {
            case "node_replace":
                return new NodeReplaceOperation(
                    NodeSelector.Parse(operation.Selector),
                    operation.ExpectedHash ?? throw new ArgumentException("expected_hash is required for node_replace.", nameof(operation)),
                    operation.Text);
            case "node_insert":
                var position = operation.Position?.ToUpperInvariant() switch
                {
                    "INSIDE" => InsertPosition.Inside,
                    "BEFORE" => InsertPosition.Before,
                    "AFTER" => InsertPosition.After,
                    _ => throw new ArgumentException("position must be 'inside', 'before' or 'after'.", nameof(operation)),
                };
                return new NodeInsertOperation(NodeSelector.Parse(operation.Selector), position, operation.Text);
            default:
                if (IntentOperations.Names.Contains(operation.Kind))
                    return IntentOperations.Create(operation.Kind, operation.Selector, operation.Name, operation.Module, operation.Text, operation.Index, operation.Level);
                throw new ArgumentException(
                    $"kind '{operation.Kind}' is not supported; use node_replace, node_insert, {string.Join(", ", IntentOperations.Names)}.", nameof(operation));
        }
    }
}
