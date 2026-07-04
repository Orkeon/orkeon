using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Crew;

/// <summary>
/// Implements <see cref="ICrewExecutionHook"/> and writes <c>AUTO_SUMMARY.md</c> to the
/// configured output mount whenever the crew finishes — whether it succeeded, was canceled
/// (timeout), or failed with an unexpected exception.
/// <para>
/// <b>File system access</b>: all writes go through <see cref="IFileSystemService.WriteAllTextAsync"/>
/// (UTF-8 without BOM, mount-scoped path resolution). The caller supplies the output mount path at construction time.
/// </para>
/// </summary>
public sealed partial class AutoSummaryWriter : ICrewExecutionHook
{
    private const string SummaryFileName = "AUTO_SUMMARY.md";

    private readonly IFileSystemService _fileSystem;
    private readonly string _outputMountPath;
    private readonly ILogger<AutoSummaryWriter> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AutoSummaryWriter"/>.
    /// </summary>
    /// <param name="fileSystem">The virtual file system service used for all file I/O.</param>
    /// <param name="outputMountPath">
    /// Virtual path of the output mount (e.g. <c>/output</c>). The summary file is written to
    /// <c>{outputMountPath}/AUTO_SUMMARY.md</c>.
    /// </param>
    /// <param name="logger">Structured logger.</param>
    public AutoSummaryWriter(
        IFileSystemService fileSystem,
        string outputMountPath,
        ILogger<AutoSummaryWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputMountPath);
        ArgumentNullException.ThrowIfNull(logger);

        _fileSystem = fileSystem;
        _outputMountPath = outputMountPath.TrimEnd('/');
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        // Individual task snapshots are accumulated by SequentialProcessStrategy;
        // this hook does not need to react to individual tasks.
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
        => WriteSummaryAsync(snapshot, ct);

    /// <inheritdoc />
    public System.Threading.Tasks.Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct)
        => WriteSummaryAsync(isPartial, ct);

    // ── Private helpers ────────────────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Crew-completion hook fault barrier: a failure writing AUTO_SUMMARY is logged and swallowed so it never affects the crew result the hook observes.")]
    private async System.Threading.Tasks.Task WriteSummaryAsync(
        CrewExecutionSnapshot snapshot,
        CancellationToken ct)
    {
        var virtualSummaryPath = $"{_outputMountPath}/{SummaryFileName}";
        try
        {
            // Snapshot output files from the mount (best-effort; never throws).
            var outputFiles = await CollectOutputFilesAsync(ct).ConfigureAwait(false);

            var content = BuildMarkdown(snapshot, outputFiles);

            await _fileSystem.WriteAllTextAsync(virtualSummaryPath, content, ct).ConfigureAwait(false);
            LogSummaryWritten(virtualSummaryPath, content.Length);
        }
        catch (FileAccessDeniedException ex)
        {
            LogAccessDenied(virtualSummaryPath, ex.Message);
        }
        catch (Exception ex)
        {
            // Never propagate — hook failures must not affect the caller.
            LogSummaryWriteFailed(ex);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort output enumeration: failure to list the output mount is logged and yields a partial file list rather than aborting summary generation.")]
    private async System.Threading.Tasks.Task<ImmutableList<OutputFileInfo>> CollectOutputFilesAsync(
        CancellationToken ct)
    {
        var files = ImmutableList.CreateBuilder<OutputFileInfo>();
        try
        {
            var opts = new VirtualEnumerationOptions(Recursive: true);
            await foreach (var entry in _fileSystem.EnumerateFilesAsync(_outputMountPath, opts, ct)
                               .ConfigureAwait(false))
            {
                if (entry.Kind == VirtualEntryKind.File && !entry.VirtualPath.EndsWith(SummaryFileName, StringComparison.Ordinal))
                {
                    files.Add(new OutputFileInfo
                    {
                        VirtualPath = entry.VirtualPath,
                        SizeBytes = entry.SizeBytes,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            LogOutputEnumerationFailed(ex);
        }
        return files.ToImmutable();
    }

    private static string BuildMarkdown(CrewExecutionSnapshot snapshot, ImmutableList<OutputFileInfo> outputFiles)
    {
        var sb = new StringBuilder();

        AppendHeader(sb, snapshot);
        AppendTasksSection(sb, snapshot);
        AppendOutputFilesSection(sb, outputFiles);
        AppendWarnings(sb, snapshot);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, CrewExecutionSnapshot snapshot)
    {
        sb.AppendLine("# AUTO_SUMMARY");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Status**: {snapshot.Status}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Crew**: {snapshot.CrewId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Started**: {snapshot.StartedAt:O}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Ended**: {snapshot.EndedAt:O}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Duration**: {snapshot.TotalDuration:g}");

        if (!string.IsNullOrEmpty(snapshot.FailureReason))
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Failure reason**: {snapshot.FailureReason}");
        }
    }

    private static void AppendTasksSection(StringBuilder sb, CrewExecutionSnapshot snapshot)
    {
        sb.AppendLine();
        sb.AppendLine("## Tasks");
        sb.AppendLine();

        if (snapshot.Tasks.IsEmpty)
        {
            sb.AppendLine("_(no tasks recorded)_");
            return;
        }

        // Tokens column format: total (cache_hit/cache_miss). Cache hit/miss are
        // surfaced only by providers that report them (DeepSeek). Friction #5.
        sb.AppendLine("| Task | Agent | Status | Duration | Tool calls | Tokens (total · hit/miss) |");
        sb.AppendLine("|------|-------|--------|----------|------------|---------------------------|");
        foreach (var task in snapshot.Tasks)
        {
            var status = task.Success ? "✓ completed" : "✗ failed";
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {task.TaskId} | {task.AgentRole} | {status} | {task.Duration:g} | {task.ToolCallCount} | {FormatTokens(task.TokensUsed, task.CacheHitTokens, task.CacheMissTokens)} |");
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Completed**: {snapshot.CompletedTaskCount} / {snapshot.Tasks.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Total tokens**: {snapshot.TotalTokensUsed}");
        AppendCacheStats(sb, snapshot);
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Total tool calls**: {snapshot.TotalToolCallCount}");
    }

    private static void AppendCacheStats(StringBuilder sb, CrewExecutionSnapshot snapshot)
    {
        if (snapshot.TotalCacheHitTokens <= 0 && snapshot.TotalCacheMissTokens <= 0)
            return;

        var totalCached = snapshot.TotalCacheHitTokens + snapshot.TotalCacheMissTokens;
        var ratio = totalCached > 0
            ? (double)snapshot.TotalCacheHitTokens / totalCached
            : 0.0;
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Prompt cache**: hit {snapshot.TotalCacheHitTokens} · miss {snapshot.TotalCacheMissTokens} · hit-ratio {ratio.ToString("P1", CultureInfo.InvariantCulture)}");
    }

    private static void AppendOutputFilesSection(StringBuilder sb, ImmutableList<OutputFileInfo> outputFiles)
    {
        sb.AppendLine();
        sb.AppendLine("## Output files");
        sb.AppendLine();

        if (outputFiles.IsEmpty)
        {
            sb.AppendLine("_(no files found in output mount)_");
            return;
        }

        sb.AppendLine("| Path | Size |");
        sb.AppendLine("|------|------|");
        foreach (var file in outputFiles)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {file.VirtualPath} | {file.SizeBytes:N0} bytes |");
        }
    }

    /// <summary>
    /// Formats the per-task tokens cell. When the provider reports prompt cache stats
    /// (DeepSeek), shows <c>total · hit/miss</c>; otherwise just the total.
    /// </summary>
    private static string FormatTokens(int total, long cacheHit, long cacheMiss)
    {
        if (cacheHit == 0 && cacheMiss == 0) return total.ToString(CultureInfo.InvariantCulture);
        return $"{total} · {cacheHit}/{cacheMiss}";
    }

    private static void AppendWarnings(StringBuilder sb, CrewExecutionSnapshot snapshot)
    {
        AppendUnknownFqnSection(sb, snapshot);
        AppendRewrittenFqnSection(sb, snapshot);
        AppendAmbiguousFqnSection(sb, snapshot);
    }

    private static void AppendUnknownFqnSection(StringBuilder sb, CrewExecutionSnapshot snapshot)
    {
        var tasksWithUnknown = snapshot.Tasks
            .Where(t => !t.UnknownFqns.IsDefaultOrEmpty)
            .ToList();

        if (tasksWithUnknown.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Warnings — Unknown FQNs");
        sb.AppendLine();
        sb.AppendLine("These FQNs appeared in deliverables but could not be resolved in the RaggableTree index.");
        sb.AppendLine();
        sb.AppendLine("| Task | Agent | Unknown FQNs |");
        sb.AppendLine("|------|-------|--------------|");
        foreach (var task in tasksWithUnknown)
        {
            var list = string.Join(", ", task.UnknownFqns.Select(f => $"`{f}`"));
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {task.TaskId} | {task.AgentRole} | {list} |");
        }
    }

    private static void AppendRewrittenFqnSection(StringBuilder sb, CrewExecutionSnapshot snapshot)
    {
        var tasksWithRewrites = snapshot.Tasks
            .Where(t => t.RewrittenFqns.Count > 0)
            .ToList();

        if (tasksWithRewrites.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Notes — Auto-rewritten bare FQNs");
        sb.AppendLine();
        sb.AppendLine("Bare-form citations (e.g. `ts::Symbol`) that resolved uniquely against a canonical long FQN.");
        sb.AppendLine();
        sb.AppendLine("| Task | Agent | Bare → Canonical |");
        sb.AppendLine("|------|-------|------------------|");
        foreach (var task in tasksWithRewrites)
        {
            var list = string.Join(", ", task.RewrittenFqns.Select(kv => $"`{kv.Key}` → `{kv.Value}`"));
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {task.TaskId} | {task.AgentRole} | {list} |");
        }
    }

    private static void AppendAmbiguousFqnSection(StringBuilder sb, CrewExecutionSnapshot snapshot)
    {
        var tasksWithAmbiguous = snapshot.Tasks
            .Where(t => !t.AmbiguousFqns.IsDefaultOrEmpty)
            .ToList();

        if (tasksWithAmbiguous.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Warnings — Ambiguous bare FQNs");
        sb.AppendLine();
        sb.AppendLine("Bare-form citations that match 2+ canonical FQNs and require operator review.");
        sb.AppendLine();
        sb.AppendLine("| Task | Agent | Bare FQN | Candidates |");
        sb.AppendLine("|------|-------|----------|------------|");
        foreach (var task in tasksWithAmbiguous)
        {
            foreach (var amb in task.AmbiguousFqns)
            {
                var candidates = string.Join(", ", amb.Candidates.Select(c => $"`{c}`"));
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {task.TaskId} | {task.AgentRole} | `{amb.BareFqn}` | {candidates} |");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AUTO_SUMMARY written to {VirtualPath} ({Length} chars)")]
    private partial void LogSummaryWritten(string virtualPath, int length);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Cannot write AUTO_SUMMARY to {VirtualPath}: {Reason}")]
    private partial void LogAccessDenied(string virtualPath, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to write AUTO_SUMMARY")]
    private partial void LogSummaryWriteFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Failed to enumerate output mount for AUTO_SUMMARY")]
    private partial void LogOutputEnumerationFailed(Exception ex);
}
