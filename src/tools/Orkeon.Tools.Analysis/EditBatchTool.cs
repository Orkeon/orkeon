using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record EditBatchOperation
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("selector")] public string Selector { get; init; } = "";
    [JsonPropertyName("expected_hash")] public string? ExpectedHash { get; init; }
    [JsonPropertyName("position")] public string? Position { get; init; }
    [JsonPropertyName("text")] public string Text { get; init; } = "";
    [JsonPropertyName("op")] public string? Op { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("module")] public string? Module { get; init; }
    [JsonPropertyName("index")] public int? Index { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }
}

public sealed record EditBatchRequest
{
    [JsonPropertyName("operations")] public IReadOnlyList<EditBatchOperation> Operations { get; init; } = [];
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
        + "'inside', 'before' or 'after'). An intent operation is {op, path} with op ensure_using / ensure_import (name, "
        + "module for TypeScript) or ensure_field / ensure_member (selector of the type, text): it adds the line or member only when "
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
            var virtualPath = _fileSystemService.ToVirtualPath(validation.ResolvedPath!) ?? operation.Path;
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
        if (!string.IsNullOrEmpty(operation.Op))
            return IntentOperations.Create(operation.Op, operation.Selector, operation.Name, operation.Module, operation.Text, operation.Index, operation.Level);

        var selector = NodeSelector.Parse(operation.Selector);
        switch (operation.Kind)
        {
            case "node_replace":
                return new NodeReplaceOperation(
                    selector,
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
                return new NodeInsertOperation(selector, position, operation.Text);
            default:
                throw new ArgumentException("kind must be 'node_replace' or 'node_insert', or op must name an intent operation.", nameof(operation));
        }
    }
}
