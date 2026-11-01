using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class AffectedTestsTool : ToolBase<AffectedTestsRequest, AffectedTestsResponse>
{
    private readonly IRaggableStore _store;
    private readonly IGitDiffProvider _gitDiff;

    public AffectedTestsTool(IRaggableStore store, IGitDiffProvider gitDiff, ILogger<AffectedTestsTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gitDiff = gitDiff ?? throw new ArgumentNullException(nameof(gitDiff));
    }

    public override string Name => "affected_tests";
    public override string Description => "Which tests cover this change? Returns the test projects and test classes the graph reaches from changed_files, or from the diff of the working tree against base (HEAD by default), with the changed files it could not resolve; all is true when the set could not be bounded and every test should run. For the symbols the change affects, impact_analysis.";
    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<AffectedTestsResponse> ExecuteTypedAsync(AffectedTestsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = _store.GetIndexedRoots().Select(r => r.VirtualRoot).FirstOrDefault() ?? "/";
        IReadOnlyList<string> changed = request.ChangedFiles ?? [];
        if (request.ChangedFiles is null)
        {
            var from = string.IsNullOrEmpty(request.Base) ? "HEAD" : request.Base;
            var committed = await _gitDiff.GetChangedFilesAsync(root, from, "HEAD", cancellationToken).ConfigureAwait(false);
            var working = await _gitDiff.GetWorkingTreeChangesAsync(root, cancellationToken).ConfigureAwait(false);
            changed = [.. committed.Union(working, StringComparer.Ordinal)];
        }

        var result = await AffectedTestsQuery.RunAsync(_store, changed, root, cancellationToken).ConfigureAwait(false);
        return new AffectedTestsResponse
        {
            All = result.All,
            TestProjects = result.TestProjects,
            TestClasses = result.TestClasses,
            Unresolved = result.Unresolved,
        };
    }
}

public sealed record AffectedTestsRequest
{
    [FieldSchema(Description = "Virtual paths of the changed files; omit them to read the diff against base and the working tree.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("changed_files")]
    public ImmutableList<string>? ChangedFiles { get; init; }

    [FieldSchema(Description = "Commit the diff is taken from when changed_files is omitted (default HEAD).", IsRequired = false)]
    [JsonPropertyName("base")]
    public string? Base { get; init; }
}

public sealed record AffectedTestsResponse
{
    [JsonPropertyName("all")]
    public bool All { get; init; }

    [JsonPropertyName("test_projects")]
    public ImmutableList<string> TestProjects { get; init; } = [];

    [JsonPropertyName("test_classes")]
    public ImmutableList<string> TestClasses { get; init; } = [];

    [JsonPropertyName("unresolved")]
    public ImmutableList<string> Unresolved { get; init; } = [];
}
