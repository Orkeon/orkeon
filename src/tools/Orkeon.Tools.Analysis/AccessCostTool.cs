using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class AccessCostTool : ToolBase<AccessCostRequest, AccessCostResponse>
{
    private const int PageSize = 1000;
    private static readonly ImmutableArray<UniversalNodeKind> MemberKinds =
        [UniversalNodeKind.Function, UniversalNodeKind.Method, UniversalNodeKind.Constructor];

    private readonly IRaggableStore _store;
    private readonly IGitDiffProvider _gitDiff;
    private readonly AccessCostService _service = new();

    public AccessCostTool(IRaggableStore store, IGitDiffProvider gitDiff, ILogger<AccessCostTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gitDiff = gitDiff ?? throw new ArgumentNullException(nameof(gitDiff));
    }

    public override string Name => "access_cost";
    public override string Description => "Port-access cost of members, mirroring the access-cost.py script.";
    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<AccessCostResponse> ExecuteTypedAsync(AccessCostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var roots = request.Roots ?? [];
        IReadOnlyList<RaggableNode> members;
        if (request.Fqns is { Count: > 0 } fqns)
        {
            members = await _store.GetManyAsync(fqns, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            IReadOnlyList<string> files = request.ChangedFiles
                ?? await _gitDiff.GetWorkingTreeChangesAsync(roots.FirstOrDefault() ?? "/", cancellationToken).ConfigureAwait(false);
            if (files.Count == 0)
            {
                return new AccessCostResponse
                {
                    Errors = ["Provide fqns or changed_files: neither was given and the working tree has no changes."],
                };
            }

            var fileSet = files.ToHashSet(StringComparer.Ordinal);
            members = await MembersOfFilesAsync(fileSet, cancellationToken).ConfigureAwait(false);
        }

        var receivers = request.Receivers is { Length: > 0 } pattern
            ? new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            : AccessCostService.DefaultReceivers;
        var selected = (roots.Count == 0 ? members : members.Where(m => InRoots(m.VirtualFilePath, roots))).ToList();
        var report = _service.Compute(selected, receivers);
        return new AccessCostResponse
        {
            Report = report with
            {
                Members = [.. report.Members.Zip(selected, (cost, node) => cost with { Member = node.Fqn })],
            },
        };
    }

    private async Task<List<RaggableNode>> MembersOfFilesAsync(HashSet<string> files, CancellationToken ct)
    {
        var found = new List<RaggableNode>();
        for (var skip = 0; ; skip += PageSize)
        {
            var page = await _store.QueryAsync(
                new NodeQuery { Level = NodeLevel.L3_Symbol, Kinds = MemberKinds, Skip = skip, Take = PageSize },
                ct).ConfigureAwait(false);
            found.AddRange(page.Where(n => files.Contains(n.VirtualFilePath)));
            if (page.Count < PageSize) return found;
        }
    }

    private static bool InRoots(string path, ImmutableList<string> roots)
        => roots.Any(r => path.Equals(r, StringComparison.Ordinal)
            || path.StartsWith(r.EndsWith('/') ? r : r + "/", StringComparison.Ordinal));
}

public sealed record AccessCostRequest
{
    [JsonPropertyName("fqns")]
    public ImmutableList<string>? Fqns { get; init; }

    [JsonPropertyName("changed_files")]
    public ImmutableList<string>? ChangedFiles { get; init; }

    [JsonPropertyName("receivers")]
    public string? Receivers { get; init; }

    [JsonPropertyName("roots")]
    public ImmutableList<string>? Roots { get; init; }
}

public sealed record AccessCostResponse
{
    [JsonPropertyName("report")]
    public AccessCostReport? Report { get; init; }

    [JsonPropertyName("errors")]
    public ImmutableList<string> Errors { get; init; } = [];
}
